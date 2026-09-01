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
public class RolesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionKeys.RolesManage)]
    public async Task<ActionResult<List<RoleDto>>> List()
    {
        var tenantId = User.GetTenantId();
        var roles = await db.TenantRoles
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Name)
            .ToListAsync();

        var result = new List<RoleDto>();
        foreach (var role in roles)
        {
            var keys = await db.TenantRolePermissions
                .Where(rp => rp.TenantRoleId == role.Id)
                .Select(rp => rp.PermissionKey)
                .ToListAsync();
            result.Add(new RoleDto(role.Id, role.Name, role.IsSystem, keys));
        }

        return Ok(result);
    }

    [HttpPut("{id:guid}/permissions")]
    [RequirePermission(PermissionKeys.RolesManage)]
    public async Task<ActionResult<RoleDto>> UpdatePermissions(Guid id, UpdateRolePermissionsRequest req)
    {
        var tenantId = User.GetTenantId();
        var role = await db.TenantRoles.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId);
        if (role is null) return NotFound();

        var validKeys = await db.Permissions.Select(p => p.Key).ToListAsync();
        var keys = req.PermissionKeys.Where(validKeys.Contains).Distinct().ToList();
        await DbSeeder.SetRolePermissionsAsync(db, role.Id, keys);

        return Ok(new RoleDto(role.Id, role.Name, role.IsSystem, keys));
    }
}

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class PermissionsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionKeys.RolesManage)]
    public async Task<ActionResult<List<PermissionGroupDto>>> List()
    {
        var permissions = await db.Permissions
            .OrderBy(p => p.Group)
            .ThenBy(p => p.Name)
            .Select(p => new PermissionDto(p.Key, p.Name, p.Group))
            .ToListAsync();

        var grouped = permissions
            .GroupBy(p => p.Group)
            .Select(g => new PermissionGroupDto(g.Key, g.ToList()))
            .ToList();

        return Ok(grouped);
    }
}
