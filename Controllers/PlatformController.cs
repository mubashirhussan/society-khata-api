using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SocietyKhata.Api.Data;
using SocietyKhata.Api.Dtos;

namespace SocietyKhata.Api.Controllers;

[ApiController]
[Authorize(Policy = "PlatformManager")]
[Route("api/platform")]
public class PlatformController(AppDbContext db) : ControllerBase
{
    [HttpGet("societies")]
    public async Task<ActionResult<SocietiesOverviewResponse>> Societies()
    {
        var societies = await db.Tenants
            .Where(t => !t.IsPlatformTenant)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new SocietyOverviewDto(t.Id, t.Name, t.Phone, t.CreatedAt, t.Users.Count))
            .ToListAsync();

        return Ok(new SocietiesOverviewResponse(societies.Count, societies));
    }
}
