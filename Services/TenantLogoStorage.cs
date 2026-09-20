namespace SocietyKhata.Api.Services;

public class TenantLogoStorage(IWebHostEnvironment environment)
{
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp"
    };

    public IReadOnlyDictionary<string, string> AllowedContentTypes => Extensions;

    public string GetDirectory(int tenantId) =>
        Path.Combine(environment.ContentRootPath, "uploads", "tenant-logos", tenantId.ToString());

    public string? FindPath(int tenantId)
    {
        var directory = GetDirectory(tenantId);
        if (!Directory.Exists(directory)) return null;

        return Extensions.Values
            .Select(extension => Path.Combine(directory, $"logo{extension}"))
            .FirstOrDefault(File.Exists);
    }

    public bool HasLogo(int tenantId) => FindPath(tenantId) is not null;

    public async Task SaveAsync(int tenantId, IFormFile logo)
    {
        if (!Extensions.TryGetValue(logo.ContentType, out var extension))
            throw new InvalidOperationException("Only JPG, PNG, and WebP images are supported.");

        var directory = GetDirectory(tenantId);
        Directory.CreateDirectory(directory);
        Delete(tenantId);

        var path = Path.Combine(directory, $"logo{extension}");
        await using var stream = File.Create(path);
        await logo.CopyToAsync(stream);
    }

    public void Delete(int tenantId)
    {
        var directory = GetDirectory(tenantId);
        if (!Directory.Exists(directory)) return;

        foreach (var extension in Extensions.Values)
        {
            var path = Path.Combine(directory, $"logo{extension}");
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
