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
        var expense = await client.PostAsJsonAsync("/api/expenses", new { amount = 4.25m, description = "Lunch" });
        expense.EnsureSuccessStatusCode();
        var overdrawExpense = await client.PostAsJsonAsync("/api/expenses", new { amount = 100m, description = "Too much" });
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
        Assert.Contains(history, entry => entry.Type == "Expense" && entry.AmountMinor == 425);
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
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"family-expenses-{Guid.NewGuid():N}.db");

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
            if (File.Exists(databasePath))
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
