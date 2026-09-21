using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SocietyKhata.Api.Data;
using SocietyKhata.Api.Dtos;
using SocietyKhata.Api.Models;
using SocietyKhata.Api.Services;

namespace SocietyKhata.Api.Controllers;

[ApiController]
[Authorize(Policy = "PlatformManager")]
[Route("api/platform")]
public class PlatformController(AppDbContext db, TenantLogoStorage logos) : ControllerBase
{
    [HttpGet("societies")]
    public async Task<ActionResult<SocietiesOverviewResponse>> Societies()
    {
        var societies = await db.Tenants
            .Where(t => !t.IsPlatformTenant)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.Phone,
                t.CreatedAt,
                UserCount = t.Users.Count,
                t.IsActive,
                Admin = t.Users
                    .Where(u => u.TenantRole!.Name == RoleNames.Admin)
                    .OrderBy(u => u.CreatedAt)
                    .Select(u => new { u.Id, u.Email })
                    .FirstOrDefault()
            })
            .ToListAsync();

        var result = societies
            .Select(s => new SocietyOverviewDto(
                s.Id, s.Name, s.Phone, s.CreatedAt, s.UserCount, s.IsActive, s.Admin?.Id, s.Admin?.Email))
            .ToList();

        return Ok(new SocietiesOverviewResponse(result.Count, result));
    }

    [HttpPost("societies/{id:int}/active")]
    public async Task<IActionResult> SetActive(int id, SetSocietyActiveRequest req)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id && !t.IsPlatformTenant);
        if (tenant is null) return NotFound();

        tenant.IsActive = req.IsActive;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("societies/{id:int}/reset-admin-password")]
    public async Task<IActionResult> ResetAdminPassword(int id, ResetPasswordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 6)
            return BadRequest(new { error = "Password must be at least 6 characters" });

        var admin = await db.Users
            .Where(u => u.TenantId == id && u.TenantRole!.Name == RoleNames.Admin)
            .OrderBy(u => u.CreatedAt)
            .FirstOrDefaultAsync();
        if (admin is null) return NotFound(new { error = "No admin user found for this society" });

        admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("societies/{id:int}")]
    public async Task<IActionResult> DeleteSociety(int id)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id && !t.IsPlatformTenant);
        if (tenant is null) return NotFound();

        await using var tx = await db.Database.BeginTransactionAsync();

        await db.PaymentInstallmentAllocations.Where(a => a.TenantId == id).ExecuteDeleteAsync();
        await db.InstallmentDues.Where(d => d.TenantId == id).ExecuteDeleteAsync();
        await db.Payments.IgnoreQueryFilters().Where(p => p.TenantId == id).ExecuteDeleteAsync();
        await db.Properties.IgnoreQueryFilters().Where(p => p.TenantId == id).ExecuteDeleteAsync();
        await db.Expenses.Where(e => e.TenantId == id).ExecuteDeleteAsync();
        await db.Clients.Where(c => c.TenantId == id).ExecuteDeleteAsync();
        await db.Users.Where(u => u.TenantId == id).ExecuteDeleteAsync();
        await db.TenantRoles.Where(r => r.TenantId == id).ExecuteDeleteAsync();
        db.Tenants.Remove(tenant);
        await db.SaveChangesAsync();

        await tx.CommitAsync();

        var logoDir = logos.GetDirectory(id);
        if (Directory.Exists(logoDir))
            Directory.Delete(logoDir, recursive: true);

        return NoContent();
    }
}
