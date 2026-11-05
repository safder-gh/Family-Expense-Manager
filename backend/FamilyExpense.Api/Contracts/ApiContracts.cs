using System.ComponentModel.DataAnnotations;
using FamilyExpense.Api.Models;

namespace FamilyExpense.Api.Contracts;

public sealed class RegisterHeadRequest
{
    [Required, StringLength(80, MinimumLength = 2)]
    public required string DisplayName { get; init; }

    [Required, EmailAddress, StringLength(256)]
    public required string Email { get; init; }

    [Required, StringLength(100)]
    public required string Password { get; init; }

    [Required, StringLength(100, MinimumLength = 2)]
    public required string FamilyName { get; init; }
}

public sealed class RegisterDependentRequest
{
    [Required, StringLength(80, MinimumLength = 2)]
    public required string DisplayName { get; init; }

    [Required, EmailAddress, StringLength(256)]
    public required string Email { get; init; }

    [Required, StringLength(100)]
    public required string Password { get; init; }

    [Required, StringLength(128)]
    public required string InviteCode { get; init; }
}

public sealed class LoginRequest
{
    [Required, EmailAddress]
    public required string Email { get; init; }

    [Required]
    public required string Password { get; init; }
}

public class CreateTransactionRequest
{
    [Range(typeof(decimal), "0.01", "100000000")]
    public decimal Amount { get; init; }

    [StringLength(160)]
    public string? Description { get; init; }
}

public sealed class TransferRequest : CreateTransactionRequest
{
    [Required]
    public required string RecipientUserId { get; init; }
}

public sealed record AuthResponse(string Token, string UserId, string DisplayName, string Email, string Role, Guid FamilyId, string FamilyName);
public sealed record InviteResponse(string Code, DateTime ExpiresAtUtc);
public sealed record MemberResponse(string UserId, string DisplayName, string Email, string Role, long BalanceMinor);
public sealed record FamilyResponse(Guid FamilyId, string FamilyName, string CurrentUserId, string CurrentRole, long CurrentBalanceMinor, IReadOnlyList<MemberResponse> Members);
public sealed record TransactionResponse(Guid Id, string Type, long AmountMinor, string Description, DateTime CreatedAtUtc, string? FromUserId, string? ToUserId, string? FromDisplayName, string? ToDisplayName, string CreatedByDisplayName);

public static class TransactionAmount
{
    public static bool TryGetMinorUnits(decimal amount, out long minorUnits)
    {
        minorUnits = 0;
        if (amount <= 0 || decimal.Round(amount, 2) != amount || amount > 100_000_000m)
        {
            return false;
        }

        minorUnits = decimal.ToInt64(amount * 100m);
        return true;
    }

    public static string NormalizeDescription(string? description, string fallback) =>
        string.IsNullOrWhiteSpace(description) ? fallback : description.Trim();
}
