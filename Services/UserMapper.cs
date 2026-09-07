using Microsoft.EntityFrameworkCore;
using SocietyKhata.Api.Data;
using SocietyKhata.Api.Dtos;
using SocietyKhata.Api.Models;

namespace SocietyKhata.Api.Services;

public static class UserMapper
{
    public static UserDto ToDto(User user, Tenant tenant, List<string> permissions, bool hasLogo = false) =>
        new(
            user.Id,
            user.Email,
            user.TenantRoleId,
            user.TenantRole?.Name ?? RoleNames.Admin,
            user.FullName,
            user.TenantId,
            tenant.Name,
            permissions,
            hasLogo);

    public static async Task<UserDto?> LoadUserDtoAsync(AppDbContext db, Guid userId, TenantLogoStorage? logos = null)
    {
        var user = await db.Users
            .Include(u => u.Tenant)
            .Include(u => u.TenantRole)
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);

        if (user?.Tenant is null) return null;

        var permissions = await db.TenantRolePermissions
            .Where(rp => rp.TenantRoleId == user.TenantRoleId)
            .Select(rp => rp.PermissionKey)
            .ToListAsync();

        var hasLogo = logos?.HasLogo(user.TenantId) ?? false;
        return ToDto(user, user.Tenant, permissions, hasLogo);
    }
}
