using System.Security.Cryptography;
using System.Text;
using FamilyExpense.Api.Contracts;
using FamilyExpense.Api.Data;
using FamilyExpense.Api.Models;
using FamilyExpense.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace FamilyExpense.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    FamilyDbContext db,
    UserManager<ApplicationUser> users,
    JwtTokenService tokens) : ControllerBase
{
    [HttpPost("register/head")]
    public async Task<IActionResult> RegisterHead(RegisterHeadRequest request)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        var family = new Family { Name = request.FamilyName.Trim() };
        var user = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(),
            Family = family,
            EmailConfirmed = true
        };
        var createResult = await users.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return BadRequest(new { errors = createResult.Errors.Select(error => error.Description) });
        }

        var roleResult = await users.AddToRoleAsync(user, "Head");
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException($"Could not assign the family-head role: {string.Join("; ", roleResult.Errors.Select(error => error.Description))}");
        }

        db.Wallets.Add(new Wallet { FamilyId = family.Id, OwnerUserId = user.Id });
        db.ExpenseCategories.AddRange(ExpenseCategory.DefaultNames.Select(name =>
            new ExpenseCategory { FamilyId = family.Id, Name = name }));
        await db.SaveChangesAsync();
        var token = await tokens.CreateTokenAsync(user, family.Name);
        await transaction.CommitAsync();
        return Ok(new AuthResponse(token, user.Id, user.DisplayName, user.Email!, "Head", family.Id, family.Name));
    }

    [HttpPost("register/dependent")]
    public async Task<IActionResult> RegisterDependent(RegisterDependentRequest request)
    {
        var codeHash = HashCode(request.InviteCode);
        var now = DateTime.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync();
        var invite = await db.FamilyInvites.SingleOrDefaultAsync(candidate =>
            candidate.CodeHash == codeHash &&
            candidate.UsedAtUtc == null &&
            candidate.ExpiresAtUtc > now);
        if (invite is null)
        {
            return BadRequest(new { message = "The invite code is invalid, expired, or already used." });
        }

        var claimed = await db.FamilyInvites
            .Where(candidate => candidate.Id == invite.Id && candidate.UsedAtUtc == null && candidate.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(update => update.SetProperty(candidate => candidate.UsedAtUtc, now));
        if (claimed != 1)
        {
            return BadRequest(new { message = "The invite code is invalid, expired, or already used." });
        }

        var family = await db.Families.SingleAsync(candidate => candidate.Id == invite.FamilyId);
        var user = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(),
            FamilyId = family.Id,
            EmailConfirmed = true
        };
        var createResult = await users.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return BadRequest(new { errors = createResult.Errors.Select(error => error.Description) });
        }

        var roleResult = await users.AddToRoleAsync(user, "Dependent");
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException($"Could not assign the dependent role: {string.Join("; ", roleResult.Errors.Select(error => error.Description))}");
        }

        db.Wallets.Add(new Wallet { FamilyId = family.Id, OwnerUserId = user.Id });
        await db.SaveChangesAsync();
        var token = await tokens.CreateTokenAsync(user, family.Name);
        await transaction.CommitAsync();
        return Ok(new AuthResponse(token, user.Id, user.DisplayName, user.Email!, "Dependent", family.Id, family.Name));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !await users.CheckPasswordAsync(user, request.Password) || user.FamilyId is null)
        {
            return Unauthorized(new { message = "Email or password is incorrect." });
        }

        var family = await db.Families.SingleOrDefaultAsync(candidate => candidate.Id == user.FamilyId);
        if (family is null)
        {
            return Unauthorized(new { message = "This account is not associated with an active family." });
        }

        var roles = await users.GetRolesAsync(user);
        if (roles.Count != 1 || roles[0] is not ("Head" or "Dependent"))
        {
            return Unauthorized(new { message = "This account does not have a valid family role." });
        }

        var token = await tokens.CreateTokenAsync(user, family.Name);
        return Ok(new AuthResponse(token, user.Id, user.DisplayName, user.Email!, roles[0], family.Id, family.Name));
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
