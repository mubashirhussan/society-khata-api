using Microsoft.EntityFrameworkCore;
using SocietyKhata.Api.Models;

namespace SocietyKhata.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<TenantRole> TenantRoles => Set<TenantRole>();
    public DbSet<TenantRolePermission> TenantRolePermissions => Set<TenantRolePermission>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Expense> Expenses => Set<Expense>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Permission>()
            .HasKey(p => p.Key);

        modelBuilder.Entity<TenantRolePermission>()
            .HasKey(rp => new { rp.TenantRoleId, rp.PermissionKey });

        modelBuilder.Entity<TenantRolePermission>()
            .HasOne(rp => rp.TenantRole)
            .WithMany(r => r.RolePermissions)
            .HasForeignKey(rp => rp.TenantRoleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TenantRolePermission>()
            .HasOne(rp => rp.Permission)
            .WithMany(p => p.RolePermissions)
            .HasForeignKey(rp => rp.PermissionKey)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TenantRole>()
            .HasIndex(r => new { r.TenantId, r.Name })
            .IsUnique();

        modelBuilder.Entity<TenantRole>()
            .HasOne(r => r.Tenant)
            .WithMany()
            .HasForeignKey(r => r.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<User>()
            .HasIndex(u => new { u.TenantId, u.Email })
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasOne(u => u.Tenant)
            .WithMany(t => t.Users)
            .HasForeignKey(u => u.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<User>()
            .HasOne(u => u.TenantRole)
            .WithMany(r => r.Users)
            .HasForeignKey(u => u.TenantRoleId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Client>()
            .HasIndex(c => c.TenantId);

        modelBuilder.Entity<Property>()
            .HasIndex(p => p.TenantId);

        modelBuilder.Entity<Property>()
            .HasOne(p => p.Client)
            .WithMany(c => c.Properties)
            .HasForeignKey(p => p.ClientId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Payment>()
            .HasIndex(p => p.TenantId);

        modelBuilder.Entity<Payment>()
            .HasOne(p => p.Client)
            .WithMany()
            .HasForeignKey(p => p.ClientId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Payment>()
            .HasOne(p => p.Property)
            .WithMany()
            .HasForeignKey(p => p.PropertyId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Expense>()
            .HasIndex(e => e.TenantId);
    }
}
