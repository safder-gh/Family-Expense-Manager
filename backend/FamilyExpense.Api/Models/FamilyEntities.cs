using Microsoft.AspNetCore.Identity;

namespace FamilyExpense.Api.Models;

public sealed class ApplicationUser : IdentityUser
{
    public required string DisplayName { get; set; }
    public Guid? FamilyId { get; set; }
    public Family? Family { get; set; }
}

public sealed class Family
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<ApplicationUser> Members { get; set; } = new List<ApplicationUser>();
}

public sealed class Wallet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FamilyId { get; set; }
    public required string OwnerUserId { get; set; }
    public long BalanceMinor { get; set; }
}

public enum LedgerTransactionType
{
    TopUp,
    Transfer,
    Expense
}

public sealed class WalletTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FamilyId { get; set; }
    public LedgerTransactionType Type { get; set; }
    public required string CreatedByUserId { get; set; }
    public string? FromUserId { get; set; }
    public string? ToUserId { get; set; }
    public long AmountMinor { get; set; }
    public required string Description { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class FamilyInvite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FamilyId { get; set; }
    public required string CodeHash { get; set; }
    public required string CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
}
