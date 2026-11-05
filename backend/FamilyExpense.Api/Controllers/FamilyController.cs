using System.Security.Claims;
using System.Security.Cryptography;
using FamilyExpense.Api.Contracts;
using FamilyExpense.Api.Data;
using FamilyExpense.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace FamilyExpense.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/family")]
public sealed class FamilyController(FamilyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetFamily()
    {
        if (!TryGetFamilyContext(out var userId, out var familyId))
        {
            return Unauthorized();
        }

        var family = await db.Families.SingleOrDefaultAsync(candidate => candidate.Id == familyId);
        if (family is null)
        {
            return NotFound();
        }

        var members = await db.Users
            .Where(user => user.FamilyId == familyId)
            .OrderBy(user => user.DisplayName)
            .Select(user => new { user.Id, user.DisplayName, user.Email })
            .ToListAsync();
        var walletBalances = await db.Wallets
            .Where(wallet => wallet.FamilyId == familyId)
            .ToDictionaryAsync(wallet => wallet.OwnerUserId, wallet => wallet.BalanceMinor);
        var memberRoles = await db.UserRoles
            .Join(db.Roles, userRole => userRole.RoleId, role => role.Id, (userRole, role) => new { userRole.UserId, role.Name })
            .Where(userRole => members.Select(member => member.Id).Contains(userRole.UserId))
            .ToDictionaryAsync(userRole => userRole.UserId, userRole => userRole.Name ?? "Dependent");
        var resultMembers = members.Select(member => new MemberResponse(
            member.Id,
            member.DisplayName,
            member.Email ?? "",
            memberRoles.GetValueOrDefault(member.Id, "Dependent"),
            walletBalances.GetValueOrDefault(member.Id))).ToList();
        var current = resultMembers.SingleOrDefault(member => member.UserId == userId);
        if (current is null)
        {
            return Unauthorized();
        }

        return Ok(new FamilyResponse(family.Id, family.Name, userId, current.Role, current.BalanceMinor, resultMembers));
    }

    [HttpPost("invites")]
    [Authorize(Roles = "Head")]
    public async Task<IActionResult> CreateInvite()
    {
        if (!TryGetFamilyContext(out var userId, out var familyId))
        {
            return Unauthorized();
        }

        var familyExists = await db.Families.AnyAsync(family => family.Id == familyId);
        if (!familyExists)
        {
            return NotFound();
        }

        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expiresAt = DateTime.UtcNow.AddHours(24);
        db.FamilyInvites.Add(new FamilyInvite
        {
            FamilyId = familyId,
            CodeHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code))),
            CreatedByUserId = userId,
            ExpiresAtUtc = expiresAt
        });
        await db.SaveChangesAsync();
        return Ok(new InviteResponse(code, expiresAt));
    }

    private bool TryGetFamilyContext(out string userId, out Guid familyId)
    {
        userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        familyId = Guid.Empty;
        return userId.Length > 0 && Guid.TryParse(User.FindFirstValue("family_id"), out familyId);
    }
}
