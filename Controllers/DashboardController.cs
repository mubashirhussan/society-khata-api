using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SocietyKhata.Api.Authorization;
using SocietyKhata.Api.Data;
using SocietyKhata.Api.Dtos;
using SocietyKhata.Api.Services;

namespace SocietyKhata.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class DashboardController(AppDbContext db) : ControllerBase
{
    [HttpGet("stats")]
    [RequirePermission(PermissionKeys.DashboardView)]
    public async Task<ActionResult<DashboardStatsDto>> Stats()
    {
        var tenantId = User.GetTenantId();
        var properties = await db.Properties.Where(p => p.TenantId == tenantId).ToListAsync();
        var totalReceived = await db.Payments.Where(p => p.TenantId == tenantId).SumAsync(p => (decimal?)p.Amount) ?? 0;
        var totalExpenses = await db.Expenses.Where(e => e.TenantId == tenantId).SumAsync(e => (decimal?)e.Amount) ?? 0;
        var totalPropertyValue = properties.Sum(p => p.TotalPrice);

        return Ok(new DashboardStatsDto(
            properties.Count(p => p.PropertyType == "plot"),
            properties.Count(p => p.PropertyType == "shop"),
            properties.Count(p => p.Status is "sold" or "booked"),
            totalReceived,
            totalExpenses,
            properties.Count,
            totalPropertyValue,
            totalPropertyValue - totalReceived));
    }
}
