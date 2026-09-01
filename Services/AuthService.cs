using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SocietyKhata.Api.Data;
using SocietyKhata.Api.Dtos;
using SocietyKhata.Api.Models;

namespace SocietyKhata.Api.Services;

public class AuthService(AppDbContext db, IConfiguration config, PermissionService permissions)
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email))
            throw new InvalidOperationException("Email already registered");

        var tenant = new Tenant
        {
            Name = req.TenantName.Trim(),
            Phone = req.Phone
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var (adminRole, _) = await DbSeeder.CreateTenantRolesAsync(db, tenant.Id);

        var user = new User
        {
            TenantId = tenant.Id,
            TenantRoleId = adminRole.Id,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            FullName = req.FullName
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        user.Tenant = tenant;
        user.TenantRole = adminRole;
        return await CreateAuthResponseAsync(user, tenant);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users
            .Include(u => u.Tenant)
            .Include(u => u.TenantRole)
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive);

        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password");

        return await CreateAuthResponseAsync(user, user.Tenant!);
    }

    public async Task<UserDto?> GetMeAsync(Guid userId) =>
        await UserMapper.LoadUserDtoAsync(db, userId);

    private async Task<AuthResponse> CreateAuthResponseAsync(User user, Tenant tenant)
    {
        var permissionKeys = await permissions.GetRolePermissionKeysAsync(user.TenantRoleId);
        var token = GenerateToken(user, permissionKeys);
        var dto = UserMapper.ToDto(user, tenant, permissionKeys);
        return new AuthResponse(token, dto);
    }

    private string GenerateToken(User user, List<string> permissionKeys)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.TenantRole?.Name ?? RoleNames.Admin),
            new("tenant_id", user.TenantId.ToString()),
            new("role_id", user.TenantRoleId.ToString()),
        };

        foreach (var permission in permissionKeys)
            claims.Add(new Claim("permission", permission));

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
