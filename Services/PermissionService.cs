using Microsoft.EntityFrameworkCore;
using SocietyKhata.Api.Data;
using SocietyKhata.Api.Models;

namespace SocietyKhata.Api.Services;

public class PermissionService(AppDbContext db)
{
    public async Task<List<string>> GetRolePermissionKeysAsync(Guid roleId) =>
        await db.TenantRolePermissions
            .Where(rp => rp.TenantRoleId == roleId)
            .Select(rp => rp.PermissionKey)
            .ToListAsync();

    public async Task<List<string>> GetUserPermissionKeysAsync(Guid userId)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return [];
        return await GetRolePermissionKeysAsync(user.TenantRoleId);
    }
}
