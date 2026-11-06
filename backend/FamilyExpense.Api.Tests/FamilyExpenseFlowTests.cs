using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FamilyExpense.Api.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FamilyExpense.Api.Tests;

public sealed class FamilyExpenseFlowTests
{
    [Fact]
    public async Task FamilyInviteTransferAndExpenseMaintainIsolatedBalances()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var head = await RegisterHead(client, "head@example.com", "River House");
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { email = "head@example.com", password = "FamilyPass123!" });
        loginResponse.EnsureSuccessStatusCode();
        var loggedIn = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(loggedIn);
        Assert.Equal(head.UserId, loggedIn.UserId);
        var invalidLogin = await client.PostAsJsonAsync("/api/auth/login", new { email = "head@example.com", password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, invalidLogin.StatusCode);

        client.SetBearer(head.Token);
        var categoriesResponse = await client.GetAsync("/api/expense-categories");
        categoriesResponse.EnsureSuccessStatusCode();
        var categories = await categoriesResponse.Content.ReadFromJsonAsync<List<ExpenseCategoryResponse>>();
        Assert.NotNull(categories);
        Assert.Contains(categories, category => category.Name == "Food");
        Assert.Contains(categories, category => category.Name == "Travel");
        Assert.Contains(categories, category => category.Name == "Health");
        Assert.Contains(categories, category => category.Name == "Other");
        var foodCategory = categories.Single(category => category.Name == "Food");

        var inviteResponse = await client.PostAsync("/api/family/invites", content: null);
        inviteResponse.EnsureSuccessStatusCode();
        var invite = await inviteResponse.Content.ReadFromJsonAsync<InviteResponse>();
        Assert.NotNull(invite);

        client.DefaultRequestHeaders.Authorization = null;
        var dependentResponse = await client.PostAsJsonAsync("/api/auth/register/dependent", new
        {
            displayName = "Sam River",
            email = "sam@example.com",
            password = "FamilyPass123!",
            inviteCode = invite.Code
        });
        dependentResponse.EnsureSuccessStatusCode();
        var dependent = await dependentResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(dependent);

        var reusedInvite = await client.PostAsJsonAsync("/api/auth/register/dependent", new
        {
            displayName = "Another Member",
            email = "another@example.com",
            password = "FamilyPass123!",
            inviteCode = invite.Code
        });
        Assert.Equal(HttpStatusCode.BadRequest, reusedInvite.StatusCode);

        client.SetBearer(head.Token);
        var topUp = await client.PostAsJsonAsync("/api/wallet/top-ups", new { amount = 50.25m, description = "Cash added" });
        topUp.EnsureSuccessStatusCode();
        var transfer = await client.PostAsJsonAsync("/api/wallet/transfers", new { recipientUserId = dependent.UserId, amount = 12.50m, description = "Weekly allowance" });
        transfer.EnsureSuccessStatusCode();
        var overdraw = await client.PostAsJsonAsync("/api/wallet/transfers", new { recipientUserId = dependent.UserId, amount = 100m, description = "Too much" });
        Assert.Equal(HttpStatusCode.Conflict, overdraw.StatusCode);

        client.SetBearer(dependent.Token);
        var missingCategory = await client.PostAsJsonAsync("/api/expenses", new { amount = 1m, description = "Missing category" });
        Assert.Equal(HttpStatusCode.BadRequest, missingCategory.StatusCode);
        var expense = await client.PostAsJsonAsync("/api/expenses", new { amount = 4.25m, description = "Lunch", categoryId = foodCategory.Id });
        expense.EnsureSuccessStatusCode();
        var deleteUsedCategory = await client.DeleteAsync($"/api/expense-categories/{foodCategory.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deleteUsedCategory.StatusCode);
        var newCategory = await client.PostAsJsonAsync("/api/expense-categories", new { name = "Games" });
        newCategory.EnsureSuccessStatusCode();
        var overdrawExpense = await client.PostAsJsonAsync("/api/expenses", new { amount = 100m, description = "Too much", categoryId = foodCategory.Id });
        Assert.Equal(HttpStatusCode.Conflict, overdrawExpense.StatusCode);

        var familyResponse = await client.GetAsync("/api/family");
        familyResponse.EnsureSuccessStatusCode();
        var family = await familyResponse.Content.ReadFromJsonAsync<FamilyResponse>();
        Assert.NotNull(family);
        Assert.Equal(825, family.CurrentBalanceMinor);
        Assert.Equal(2, family.Members.Count);
        Assert.Equal(3775, family.Members.Single(member => member.UserId == head.UserId).BalanceMinor);

        var historyResponse = await client.GetAsync("/api/transactions");
        historyResponse.EnsureSuccessStatusCode();
        var history = await historyResponse.Content.ReadFromJsonAsync<List<TransactionResponse>>();
        Assert.NotNull(history);
        Assert.Equal(3, history.Count);
        Assert.Contains(history, entry => entry.Type == "TopUp" && entry.AmountMinor == 5025);
        Assert.Contains(history, entry => entry.Type == "Transfer" && entry.AmountMinor == 1250);
        Assert.Contains(history, entry => entry.Type == "Expense" && entry.AmountMinor == 425 && entry.CategoryName == "Food");
    }

    [Fact]
    public async Task ExpenseCategoriesCanBeManagedButRemainFamilyScoped()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();
        var firstFamily = await RegisterHead(client, "categories@example.com", "Category Family");
        var secondFamily = await RegisterHead(client, "other@example.com", "Other Family");

        client.SetBearer(firstFamily.Token);
        var created = await client.PostAsJsonAsync("/api/expense-categories", new { name = "  Education  " });
        created.EnsureSuccessStatusCode();
        var category = await created.Content.ReadFromJsonAsync<ExpenseCategoryResponse>();
        Assert.NotNull(category);
        Assert.Equal("Education", category.Name);

        var duplicate = await client.PostAsJsonAsync("/api/expense-categories", new { name = "education" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var renamed = await client.PutAsJsonAsync($"/api/expense-categories/{category.Id}", new { name = "School" });
        renamed.EnsureSuccessStatusCode();
        var renamedCategory = await renamed.Content.ReadFromJsonAsync<ExpenseCategoryResponse>();
        Assert.NotNull(renamedCategory);
        Assert.Equal("School", renamedCategory.Name);

        client.SetBearer(secondFamily.Token);
        var foreignCategoryExpense = await client.PostAsJsonAsync("/api/expenses", new
        {
            amount = 1m,
            description = "Foreign category",
            categoryId = category.Id
        });
        Assert.Equal(HttpStatusCode.BadRequest, foreignCategoryExpense.StatusCode);
        var crossFamilyRename = await client.PutAsJsonAsync($"/api/expense-categories/{category.Id}", new { name = "Not yours" });
        Assert.Equal(HttpStatusCode.NotFound, crossFamilyRename.StatusCode);
        var crossFamilyDelete = await client.DeleteAsync($"/api/expense-categories/{category.Id}");
        Assert.Equal(HttpStatusCode.NotFound, crossFamilyDelete.StatusCode);

        client.SetBearer(firstFamily.Token);
        var deleted = await client.DeleteAsync($"/api/expense-categories/{category.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task ExistingDatabaseReceivesCategoriesAndLegacyExpensesAreAssignedToOther()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"family-expenses-legacy-{Guid.NewGuid():N}.db");
        string token;

        using (var factory = new TestApiFactory(databasePath, deleteDatabaseOnDispose: false))
        using (var client = factory.CreateClient())
        {
            var head = await RegisterHead(client, "legacy@example.com", "Legacy Family");
            token = head.Token;
            client.SetBearer(token);
            (await client.PostAsJsonAsync("/api/wallet/top-ups", new { amount = 20m })).EnsureSuccessStatusCode();

            var categoriesResponse = await client.GetAsync("/api/expense-categories");
            categoriesResponse.EnsureSuccessStatusCode();
            var categories = await categoriesResponse.Content.ReadFromJsonAsync<List<ExpenseCategoryResponse>>();
            Assert.NotNull(categories);
            var food = categories.Single(category => category.Name == "Food");
            (await client.PostAsJsonAsync("/api/expenses", new
            {
                amount = 2m,
                description = "Old expense",
                categoryId = food.Id
            })).EnsureSuccessStatusCode();
        }

        await CreateLegacyDatabaseSchema(databasePath);

        using (var factory = new TestApiFactory(databasePath))
        using (var client = factory.CreateClient())
        {
            client.SetBearer(token);
            var historyResponse = await client.GetAsync("/api/transactions");
            historyResponse.EnsureSuccessStatusCode();
            var history = await historyResponse.Content.ReadFromJsonAsync<List<TransactionResponse>>();
            Assert.NotNull(history);
            Assert.Contains(history, entry =>
                entry.Type == "Expense" && entry.Description == "Old expense" && entry.CategoryName == "Other");
        }
    }

    private static async Task CreateLegacyDatabaseSchema(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = OFF;
            DROP TABLE "ExpenseCategories";
            CREATE TABLE "WalletTransactions_Legacy" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_WalletTransactions" PRIMARY KEY,
                "FamilyId" TEXT NOT NULL,
                "Type" TEXT NOT NULL,
                "CreatedByUserId" TEXT NOT NULL,
                "FromUserId" TEXT NULL,
                "ToUserId" TEXT NULL,
                "AmountMinor" INTEGER NOT NULL,
                "Description" TEXT NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL
            );
            INSERT INTO "WalletTransactions_Legacy"
                ("Id", "FamilyId", "Type", "CreatedByUserId", "FromUserId", "ToUserId", "AmountMinor", "Description", "CreatedAtUtc")
            SELECT "Id", "FamilyId", "Type", "CreatedByUserId", "FromUserId", "ToUserId", "AmountMinor", "Description", "CreatedAtUtc"
            FROM "WalletTransactions";
            DROP TABLE "WalletTransactions";
            ALTER TABLE "WalletTransactions_Legacy" RENAME TO "WalletTransactions";
            CREATE INDEX "IX_WalletTransactions_FamilyId_CreatedAtUtc"
                ON "WalletTransactions" ("FamilyId", "CreatedAtUtc");
            """;
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task FamilyDataRequiresAuthenticationAndDoesNotCrossFamilyBoundaries()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var anonymousResponse = await client.GetAsync("/api/family");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        var firstFamilyHead = await RegisterHead(client, "first@example.com", "First Family");
        var secondFamilyHead = await RegisterHead(client, "second@example.com", "Second Family");
        Assert.NotEqual(firstFamilyHead.FamilyId, secondFamilyHead.FamilyId);

        client.SetBearer(firstFamilyHead.Token);
        var response = await client.GetAsync("/api/family");
        response.EnsureSuccessStatusCode();
        var family = await response.Content.ReadFromJsonAsync<FamilyResponse>();
        Assert.NotNull(family);
        Assert.Equal("First Family", family.FamilyName);
        Assert.Single(family.Members);
        Assert.Equal(firstFamilyHead.UserId, family.Members[0].UserId);
    }

    private static async Task<AuthResponse> RegisterHead(HttpClient client, string email, string familyName)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register/head", new
        {
            displayName = familyName + " Head",
            email,
            password = "FamilyPass123!",
            familyName
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }
}

internal sealed class TestApiFactory : WebApplicationFactory<Program>
{
    private readonly string databasePath;
    private readonly bool deleteDatabaseOnDispose;

    public TestApiFactory(string? databasePath = null, bool deleteDatabaseOnDispose = true)
    {
        this.databasePath = databasePath ?? Path.Combine(Path.GetTempPath(), $"family-expenses-{Guid.NewGuid():N}.db");
        this.deleteDatabaseOnDispose = deleteDatabaseOnDispose;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Jwt:Key", new string('t', 64));
        builder.UseSetting("ConnectionStrings:FamilyExpenses", $"Data Source={databasePath}");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            SqliteConnection.ClearAllPools();
            if (deleteDatabaseOnDispose && File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}

internal static class HttpClientAuthExtensions
{
    public static void SetBearer(this HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}
