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
public class UsersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionKeys.UsersView)]
    public async Task<ActionResult<List<UserListDto>>> List()
    {
        var tenantId = User.GetTenantId();
        var users = await db.Users
            .Include(u => u.TenantRole)
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.Email)
            .Select(u => new UserListDto(
                u.Id, u.Email, u.TenantRoleId, u.TenantRole!.Name, u.FullName, u.IsActive, u.CreatedAt))
            .ToListAsync();
        return Ok(users);
    }

    [HttpGet("roles")]
    [RequirePermission(PermissionKeys.UsersManage)]
    public async Task<ActionResult<List<RoleDto>>> RolesForAssign()
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

    [HttpPost]
    [RequirePermission(PermissionKeys.UsersManage)]
    public async Task<ActionResult<UserListDto>> Create(CreateUserRequest req)
    {
        var tenantId = User.GetTenantId();
        var email = req.Email.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(u => u.TenantId == tenantId && u.Email == email))
            return BadRequest(new { error = "Email already exists in this society" });

        var role = await db.TenantRoles.FirstOrDefaultAsync(r => r.Id == req.RoleId && r.TenantId == tenantId);
        if (role is null) return BadRequest(new { error = "Invalid role" });

        var user = new User
        {
            TenantId = tenantId,
            TenantRoleId = role.Id,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            FullName = req.FullName
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return Ok(new UserListDto(user.Id, user.Email, role.Id, role.Name, user.FullName, user.IsActive, user.CreatedAt));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionKeys.UsersManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var tenantId = User.GetTenantId();
        var currentUserId = User.GetUserId();
        if (id == currentUserId)
            return BadRequest(new { error = "Cannot delete your own account" });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId);
        if (user is null) return NotFound();

        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
