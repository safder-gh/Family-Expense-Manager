using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FamilyExpense.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace FamilyExpense.Api.Services;

public sealed class JwtTokenService(IConfiguration configuration, UserManager<ApplicationUser> userManager)
{
    public async Task<string> CreateTokenAsync(ApplicationUser user, string familyName)
    {
        var familyId = user.FamilyId ?? throw new InvalidOperationException("A signed-in user must belong to a family.");
        var role = (await userManager.GetRolesAsync(user)).Single();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"] ?? "FamilyExpense.Api",
            audience: configuration["Jwt:Audience"] ?? "FamilyExpense.Client",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(JwtRegisteredClaimNames.Email, user.Email!),
                new Claim(ClaimTypes.Role, role),
                new Claim("family_id", familyId.ToString())
            ],
            notBefore: now,
            expires: now.AddMinutes(30),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
