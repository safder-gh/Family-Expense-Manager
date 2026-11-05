using System.Security.Claims;
using FamilyExpense.Api.Contracts;
using FamilyExpense.Api.Data;
using FamilyExpense.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FamilyExpense.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class WalletController(FamilyDbContext db, UserManager<ApplicationUser> users) : ControllerBase
{
    [HttpPost("wallet/top-ups")]
    [Authorize(Roles = "Head")]
    public async Task<IActionResult> TopUp(CreateTransactionRequest request)
    {
        if (!TryGetFamilyContext(out var userId, out var familyId))
        {
            return Unauthorized();
        }
        if (!TransactionAmount.TryGetMinorUnits(request.Amount, out var amount))
        {
            return BadRequest(new { message = "Amount must be positive and have no more than two decimal places." });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var updated = await db.Wallets
            .Where(wallet => wallet.OwnerUserId == userId && wallet.FamilyId == familyId)
            .ExecuteUpdateAsync(update => update.SetProperty(wallet => wallet.BalanceMinor, wallet => wallet.BalanceMinor + amount));
        if (updated != 1)
        {
            return NotFound(new { message = "The head wallet could not be found." });
        }

        db.WalletTransactions.Add(new WalletTransaction
        {
            FamilyId = familyId,
            Type = LedgerTransactionType.TopUp,
            CreatedByUserId = userId,
            ToUserId = userId,
            AmountMinor = amount,
            Description = TransactionAmount.NormalizeDescription(request.Description, "Manual top-up")
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Ok(new { balanceChangeMinor = amount });
    }

    [HttpPost("wallet/transfers")]
    [Authorize(Roles = "Head")]
    public async Task<IActionResult> Transfer(TransferRequest request)
    {
        if (!TryGetFamilyContext(out var userId, out var familyId))
        {
            return Unauthorized();
        }
        if (!TransactionAmount.TryGetMinorUnits(request.Amount, out var amount))
        {
            return BadRequest(new { message = "Amount must be positive and have no more than two decimal places." });
        }

        var recipient = await db.Users.SingleOrDefaultAsync(user =>
            user.Id == request.RecipientUserId && user.FamilyId == familyId);
        if (recipient is null || !await users.IsInRoleAsync(recipient, "Dependent"))
        {
            return BadRequest(new { message = "Select a dependent in your family." });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var debited = await db.Wallets
            .Where(wallet => wallet.OwnerUserId == userId && wallet.FamilyId == familyId && wallet.BalanceMinor >= amount)
            .ExecuteUpdateAsync(update => update.SetProperty(wallet => wallet.BalanceMinor, wallet => wallet.BalanceMinor - amount));
        if (debited != 1)
        {
            return Conflict(new { message = "The family head wallet has insufficient funds." });
        }

        var credited = await db.Wallets
            .Where(wallet => wallet.OwnerUserId == recipient.Id && wallet.FamilyId == familyId)
            .ExecuteUpdateAsync(update => update.SetProperty(wallet => wallet.BalanceMinor, wallet => wallet.BalanceMinor + amount));
        if (credited != 1)
        {
            return Conflict(new { message = "The recipient wallet could not be found." });
        }

        db.WalletTransactions.Add(new WalletTransaction
        {
            FamilyId = familyId,
            Type = LedgerTransactionType.Transfer,
            CreatedByUserId = userId,
            FromUserId = userId,
            ToUserId = recipient.Id,
            AmountMinor = amount,
            Description = TransactionAmount.NormalizeDescription(request.Description, $"Transfer to {recipient.DisplayName}")
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Ok(new { balanceChangeMinor = -amount });
    }

    [HttpPost("expenses")]
    public async Task<IActionResult> CreateExpense(CreateTransactionRequest request)
    {
        if (!TryGetFamilyContext(out var userId, out var familyId))
        {
            return Unauthorized();
        }
        if (!TransactionAmount.TryGetMinorUnits(request.Amount, out var amount))
        {
            return BadRequest(new { message = "Amount must be positive and have no more than two decimal places." });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var debited = await db.Wallets
            .Where(wallet => wallet.OwnerUserId == userId && wallet.FamilyId == familyId && wallet.BalanceMinor >= amount)
            .ExecuteUpdateAsync(update => update.SetProperty(wallet => wallet.BalanceMinor, wallet => wallet.BalanceMinor - amount));
        if (debited != 1)
        {
            return Conflict(new { message = "Your wallet has insufficient funds." });
        }

        db.WalletTransactions.Add(new WalletTransaction
        {
            FamilyId = familyId,
            Type = LedgerTransactionType.Expense,
            CreatedByUserId = userId,
            FromUserId = userId,
            AmountMinor = amount,
            Description = TransactionAmount.NormalizeDescription(request.Description, "Expense")
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Ok(new { balanceChangeMinor = -amount });
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactions()
    {
        if (!TryGetFamilyContext(out _, out var familyId))
        {
            return Unauthorized();
        }

        var transactions = await db.WalletTransactions
            .Where(entry => entry.FamilyId == familyId)
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .Take(100)
            .ToListAsync();
        var participantIds = transactions
            .SelectMany(entry => new[] { entry.CreatedByUserId, entry.FromUserId, entry.ToUserId })
            .Where(id => id is not null)
            .Distinct()
            .ToList();
        var names = await db.Users
            .Where(user => participantIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName);
        var result = transactions.Select(entry => new TransactionResponse(
            entry.Id,
            entry.Type.ToString(),
            entry.AmountMinor,
            entry.Description,
            entry.CreatedAtUtc,
            entry.FromUserId,
            entry.ToUserId,
            entry.FromUserId is not null ? names.GetValueOrDefault(entry.FromUserId) : null,
            entry.ToUserId is not null ? names.GetValueOrDefault(entry.ToUserId) : null,
            names.GetValueOrDefault(entry.CreatedByUserId, "Family member")));
        return Ok(result);
    }

    private bool TryGetFamilyContext(out string userId, out Guid familyId)
    {
        userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        familyId = Guid.Empty;
        return userId.Length > 0 && Guid.TryParse(User.FindFirstValue("family_id"), out familyId);
    }
}
