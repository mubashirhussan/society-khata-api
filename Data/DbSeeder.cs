using Microsoft.EntityFrameworkCore;
using SocietyKhata.Api.Models;

namespace SocietyKhata.Api.Data;

public static class DbSeeder
{
    private static readonly (string Key, string Name, string Group)[] PermissionDefs =
    [
        (PermissionKeys.DashboardView, "View Dashboard", "Dashboard"),
        (PermissionKeys.PropertiesView, "View Properties", "Properties"),
        (PermissionKeys.PropertiesCreate, "Create Properties", "Properties"),
        (PermissionKeys.PropertiesEdit, "Edit Properties", "Properties"),
        (PermissionKeys.PropertiesDelete, "Delete Properties", "Properties"),
        (PermissionKeys.PaymentsView, "View Payments", "Payments"),
        (PermissionKeys.PaymentsCreate, "Create Payments", "Payments"),
        (PermissionKeys.PaymentsEdit, "Edit Payments", "Payments"),
        (PermissionKeys.PaymentsDelete, "Delete Payments", "Payments"),
        (PermissionKeys.ExpensesView, "View Expenses", "Expenses"),
        (PermissionKeys.ExpensesCreate, "Create Expenses", "Expenses"),
        (PermissionKeys.ExpensesEdit, "Edit Expenses", "Expenses"),
        (PermissionKeys.ExpensesDelete, "Delete Expenses", "Expenses"),
        (PermissionKeys.ReportsView, "View Reports", "Reports"),
        (PermissionKeys.UsersView, "View Users", "Users"),
        (PermissionKeys.UsersManage, "Manage Users", "Users"),
        (PermissionKeys.RolesManage, "Manage Roles & Permissions", "Roles"),
    ];

    public static async Task SeedAsync(AppDbContext db)
    {
        foreach (var (key, name, group) in PermissionDefs)
        {
            if (!await db.Permissions.AnyAsync(p => p.Key == key))
                db.Permissions.Add(new Permission { Key = key, Name = name, Group = group });
        }
        await db.SaveChangesAsync();

        var tenants = await db.Tenants.ToListAsync();
        foreach (var tenant in tenants)
            await EnsureTenantRolesAsync(db, tenant.Id);
    }

    public static async Task<(TenantRole Admin, TenantRole Accountant)> CreateTenantRolesAsync(AppDbContext db, Guid tenantId)
    {
        var adminRole = new TenantRole { TenantId = tenantId, Name = RoleNames.Admin, IsSystem = true };
        var accountantRole = new TenantRole { TenantId = tenantId, Name = RoleNames.Accountant, IsSystem = true };

        db.TenantRoles.AddRange(adminRole, accountantRole);
        await db.SaveChangesAsync();

        await SetRolePermissionsAsync(db, adminRole.Id, PermissionKeys.All);
        await SetRolePermissionsAsync(db, accountantRole.Id, PermissionKeys.AccountantDefaults);

        return (adminRole, accountantRole);
    }

    public static async Task EnsureTenantRolesAsync(AppDbContext db, Guid tenantId)
    {
        if (await db.TenantRoles.AnyAsync(r => r.TenantId == tenantId))
            return;

        await CreateTenantRolesAsync(db, tenantId);
    }

    public static async Task SetRolePermissionsAsync(AppDbContext db, Guid roleId, IEnumerable<string> keys)
    {
        var existing = await db.TenantRolePermissions.Where(rp => rp.TenantRoleId == roleId).ToListAsync();
        db.TenantRolePermissions.RemoveRange(existing);

        foreach (var key in keys.Distinct())
        {
            db.TenantRolePermissions.Add(new TenantRolePermission
            {
                TenantRoleId = roleId,
                PermissionKey = key
            });
        }

        await db.SaveChangesAsync();
    }
}
