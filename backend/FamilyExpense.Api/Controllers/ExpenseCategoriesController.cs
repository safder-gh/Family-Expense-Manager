using System.Security.Claims;
using FamilyExpense.Api.Contracts;
using FamilyExpense.Api.Data;
using FamilyExpense.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FamilyExpense.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/expense-categories")]
public sealed class ExpenseCategoriesController(FamilyDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetCategories()
    {
        if (!TryGetFamilyId(out var familyId))
        {
            return Unauthorized();
        }

        var categories = await db.ExpenseCategories
            .Where(category => category.FamilyId == familyId)
            .OrderBy(category => category.Name)
            .Select(category => new ExpenseCategoryResponse(category.Id, category.Name))
            .ToListAsync();
        return Ok(categories);
    }

    [HttpPost]
    public async Task<IActionResult> CreateCategory(ExpenseCategoryRequest request)
    {
        if (!TryGetFamilyId(out var familyId))
        {
            return Unauthorized();
        }

        var name = request.Name.Trim();
        if (name.Length == 0)
        {
            return BadRequest(new { message = "Category name cannot be empty." });
        }

        if (await db.ExpenseCategories.AnyAsync(category =>
                category.FamilyId == familyId && category.Name == name))
        {
            return Conflict(new { message = "A category with this name already exists in your family." });
        }

        var category = new ExpenseCategory { FamilyId = familyId, Name = name };
        db.ExpenseCategories.Add(category);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetCategories), new { }, new ExpenseCategoryResponse(category.Id, category.Name));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> RenameCategory(Guid id, ExpenseCategoryRequest request)
    {
        if (!TryGetFamilyId(out var familyId))
        {
            return Unauthorized();
        }

        var name = request.Name.Trim();
        if (name.Length == 0)
        {
            return BadRequest(new { message = "Category name cannot be empty." });
        }

        var category = await db.ExpenseCategories.SingleOrDefaultAsync(candidate =>
            candidate.Id == id && candidate.FamilyId == familyId);
        if (category is null)
        {
            return NotFound(new { message = "Category not found in your family." });
        }

        if (await db.ExpenseCategories.AnyAsync(candidate =>
                candidate.FamilyId == familyId && candidate.Id != id && candidate.Name == name))
        {
            return Conflict(new { message = "A category with this name already exists in your family." });
        }

        category.Name = name;
        await db.SaveChangesAsync();
        return Ok(new ExpenseCategoryResponse(category.Id, category.Name));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid id)
    {
        if (!TryGetFamilyId(out var familyId))
        {
            return Unauthorized();
        }

        var category = await db.ExpenseCategories.SingleOrDefaultAsync(candidate =>
            candidate.Id == id && candidate.FamilyId == familyId);
        if (category is null)
        {
            return NotFound(new { message = "Category not found in your family." });
        }

        if (await db.WalletTransactions.AnyAsync(entry =>
                entry.FamilyId == familyId && entry.CategoryId == category.Id))
        {
            return Conflict(new { message = "A category used by an expense cannot be deleted." });
        }

        db.ExpenseCategories.Remove(category);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private bool TryGetFamilyId(out Guid familyId) =>
        Guid.TryParse(User.FindFirstValue("family_id"), out familyId);
}
