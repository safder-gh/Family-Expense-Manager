using FamilyExpense.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FamilyExpense.Api.Data;

public sealed class FamilyDbContext(DbContextOptions<FamilyDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Family> Families => Set<Family>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<FamilyInvite> FamilyInvites => Set<FamilyInvite>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>()
            .HasOne(user => user.Family)
            .WithMany(family => family.Members)
            .HasForeignKey(user => user.FamilyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Wallet>()
            .HasIndex(wallet => wallet.OwnerUserId)
            .IsUnique();
        builder.Entity<Wallet>()
            .HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<Wallet>(wallet => wallet.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Wallet>()
            .ToTable(table => table.HasCheckConstraint("CK_Wallets_BalanceMinor_NonNegative", "BalanceMinor >= 0"));

        builder.Entity<WalletTransaction>()
            .Property(transaction => transaction.Type)
            .HasConversion<string>();
        builder.Entity<WalletTransaction>()
            .HasIndex(transaction => new { transaction.FamilyId, transaction.CreatedAtUtc });

        builder.Entity<FamilyInvite>()
            .HasIndex(invite => invite.CodeHash)
            .IsUnique();
    }
}
