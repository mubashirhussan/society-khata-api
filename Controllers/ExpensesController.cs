using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SocietyKhata.Api.Authorization;
using SocietyKhata.Api.Data;
using SocietyKhata.Api.Dtos;
using SocietyKhata.Api.Models;
using SocietyKhata.Api.Services;

namespace SocietyKhata.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ExpensesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionKeys.ExpensesView)]
    public async Task<ActionResult<List<ExpenseDto>>> List()
    {
        var tenantId = User.GetTenantId();
        var items = await db.Expenses
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.ExpenseDate)
            .Select(e => new ExpenseDto(e.Id, e.Description, e.Amount, e.PaidTo, e.ExpenseDate, e.Notes, e.CreatedAt))
            .ToListAsync();
        return Ok(items);
    }

    [HttpPost]
    [RequirePermission(PermissionKeys.ExpensesCreate)]
    public async Task<ActionResult<ExpenseDto>> Create(ExpenseRequest req)
    {
        var expense = Map(new Expense { TenantId = User.GetTenantId() }, req);
        db.Expenses.Add(expense);
        await db.SaveChangesAsync();
        return Ok(new ExpenseDto(expense.Id, expense.Description, expense.Amount, expense.PaidTo, expense.ExpenseDate, expense.Notes, expense.CreatedAt));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionKeys.ExpensesEdit)]
    public async Task<ActionResult<ExpenseDto>> Update(Guid id, ExpenseRequest req)
    {
        var expense = await FindAsync(id);
        if (expense is null) return NotFound();
        Map(expense, req);
        await db.SaveChangesAsync();
        return Ok(new ExpenseDto(expense.Id, expense.Description, expense.Amount, expense.PaidTo, expense.ExpenseDate, expense.Notes, expense.CreatedAt));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionKeys.ExpensesDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var expense = await FindAsync(id);
        if (expense is null) return NotFound();
        db.Expenses.Remove(expense);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<Expense?> FindAsync(Guid id) =>
        await db.Expenses.FirstOrDefaultAsync(e => e.Id == id && e.TenantId == User.GetTenantId());

    private static Expense Map(Expense e, ExpenseRequest req)
    {
        e.Description = req.Description.Trim();
        e.Amount = req.Amount;
        e.PaidTo = req.PaidTo;
        e.ExpenseDate = req.ExpenseDate;
        e.Notes = req.Notes;
        return e;
    }
}
