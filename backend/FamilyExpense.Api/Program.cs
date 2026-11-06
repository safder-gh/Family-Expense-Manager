using System.Security.Claims;
using System.Text;
using FamilyExpense.Api.Data;
using FamilyExpense.Api.Models;
using FamilyExpense.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException("Configure Jwt:Key with a secret of at least 32 bytes using user-secrets or environment variables.");
}

var issuer = builder.Configuration["Jwt:Issuer"] ?? "FamilyExpense.Api";
var audience = builder.Configuration["Jwt:Audience"] ?? "FamilyExpense.Client";
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services.AddDbContext<FamilyDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("FamilyExpenses") ?? "Data Source=family-expenses.db"));
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<FamilyDbContext>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
var app = builder.Build();

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FamilyDbContext>();
    await db.Database.EnsureCreatedAsync();
    await EnsureExpenseCategorySchemaAsync(db);
    await SeedExpenseCategoriesAsync(db);

    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Head", "Dependent" })
    {
        if (!await roles.RoleExistsAsync(role))
        {
            var result = await roles.CreateAsync(new IdentityRole(role));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Could not create the {role} role: {string.Join("; ", result.Errors.Select(error => error.Description))}");
            }
        }
    }
}

app.Run();

static async Task EnsureExpenseCategorySchemaAsync(FamilyDbContext db)
{
    await db.Database.OpenConnectionAsync();
    try
    {
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS "ExpenseCategories" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_ExpenseCategories" PRIMARY KEY,
                "FamilyId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                CONSTRAINT "FK_ExpenseCategories_Families_FamilyId"
                    FOREIGN KEY ("FamilyId") REFERENCES "Families" ("Id") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ExpenseCategories_FamilyId_Name"
                ON "ExpenseCategories" ("FamilyId", "Name" COLLATE NOCASE);
            """;
        await command.ExecuteNonQueryAsync();

        command.CommandText = "PRAGMA table_info('WalletTransactions')";
        var hasCategoryId = false;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (reader.GetString(1) == "CategoryId")
                {
                    hasCategoryId = true;
                    break;
                }
            }
        }

        if (!hasCategoryId)
        {
            command.CommandText = "ALTER TABLE \"WalletTransactions\" ADD COLUMN \"CategoryId\" TEXT NULL";
            await command.ExecuteNonQueryAsync();
        }
    }
    finally
    {
        await db.Database.CloseConnectionAsync();
    }
}

static async Task SeedExpenseCategoriesAsync(FamilyDbContext db)
{
    var families = await db.Families.Select(family => family.Id).ToListAsync();
    foreach (var familyId in families)
    {
        var names = await db.ExpenseCategories
            .Where(category => category.FamilyId == familyId)
            .Select(category => category.Name)
            .ToListAsync();
        foreach (var name in ExpenseCategory.DefaultNames)
        {
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                db.ExpenseCategories.Add(new ExpenseCategory { FamilyId = familyId, Name = name });
            }
        }
    }

    await db.SaveChangesAsync();

    var categories = await db.ExpenseCategories
        .Where(category => category.Name == "Other")
        .ToDictionaryAsync(category => category.FamilyId, category => category.Id);
    var legacyExpenses = await db.WalletTransactions
        .Where(entry => entry.Type == LedgerTransactionType.Expense && entry.CategoryId == null)
        .ToListAsync();
    foreach (var expense in legacyExpenses)
    {
        if (categories.TryGetValue(expense.FamilyId, out var categoryId))
        {
            expense.CategoryId = categoryId;
        }
    }

    await db.SaveChangesAsync();
}

public partial class Program;
