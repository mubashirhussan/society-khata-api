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
public class ClientsController(AppDbContext db, IWebHostEnvironment environment) : ControllerBase
{
    private static readonly Dictionary<string, string> PictureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp"
    };

    [HttpGet]
    [RequirePermission(PermissionKeys.PropertiesView)]
    public async Task<ActionResult<List<ClientDto>>> List()
    {
        var tenantId = User.GetTenantId();
        var items = await db.Clients
            .Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.Name)
            .ToListAsync();
        return Ok(items.Select(ToDto).ToList());
    }

    [HttpPost]
    [RequirePermission(PermissionKeys.PropertiesCreate)]
    public async Task<ActionResult<ClientDto>> Create(ClientRequest req)
    {
        var client = new Client
        {
            TenantId = User.GetTenantId(),
            Name = req.Name.Trim(),
            Cnic = req.Cnic,
            Phone = req.Phone,
            Address = req.Address,
            FatherHusband = req.FatherHusband,
            Notes = req.Notes
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return Ok(ToDto(client));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionKeys.PropertiesEdit)]
    public async Task<ActionResult<ClientDto>> Update(Guid id, ClientRequest req)
    {
        var client = await FindAsync(id);
        if (client is null) return NotFound();

        client.Name = req.Name.Trim();
        client.Cnic = req.Cnic;
        client.Phone = req.Phone;
        client.Address = req.Address;
        client.FatherHusband = req.FatherHusband;
        client.Notes = req.Notes;
        await db.SaveChangesAsync();
        return Ok(ToDto(client));
    }

    [HttpPost("{id:guid}/picture")]
    [RequirePermission(PermissionKeys.PropertiesCreate)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ActionResult<ClientDto>> UploadPicture(Guid id, IFormFile? picture)
    {
        var client = await FindAsync(id);
        if (client is null) return NotFound();
        if (picture is null || picture.Length == 0 || picture.Length > 5 * 1024 * 1024)
            return BadRequest("Please choose an image smaller than 5 MB.");
        if (!PictureExtensions.TryGetValue(picture.ContentType, out var extension))
            return BadRequest("Only JPG, PNG, and WebP images are supported.");

        var directory = GetPictureDirectory(client.TenantId);
        Directory.CreateDirectory(directory);
        DeletePictureFiles(client);

        var path = Path.Combine(directory, $"{client.Id}{extension}");
        await using var stream = System.IO.File.Create(path);
        await picture.CopyToAsync(stream);

        return Ok(ToDto(client));
    }

    [HttpGet("{id:guid}/picture")]
    [RequirePermission(PermissionKeys.PropertiesView)]
    public async Task<IActionResult> GetPicture(Guid id)
    {
        var client = await FindAsync(id);
        if (client is null) return NotFound();

        var path = FindPicturePath(client);
        if (path is null) return NotFound();

        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
        return PhysicalFile(path, contentType);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionKeys.PropertiesDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var client = await FindAsync(id);
        if (client is null) return NotFound();
        DeletePictureFiles(client);
        db.Clients.Remove(client);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<Client?> FindAsync(Guid id) =>
        await db.Clients.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == User.GetTenantId());

    private ClientDto ToDto(Client c) =>
        new(c.Id, c.Name, c.Cnic, c.Phone, c.Address, c.FatherHusband, c.Notes, c.CreatedAt, FindPicturePath(c) is not null);

    private string GetPictureDirectory(Guid tenantId) =>
        Path.Combine(environment.ContentRootPath, "uploads", "client-pictures", tenantId.ToString());

    private string? FindPicturePath(Client client)
    {
        var directory = GetPictureDirectory(client.TenantId);
        if (!Directory.Exists(directory)) return null;

        return PictureExtensions.Values
            .Select(extension => Path.Combine(directory, $"{client.Id}{extension}"))
            .FirstOrDefault(System.IO.File.Exists);
    }

    private void DeletePictureFiles(Client client)
    {
        var directory = GetPictureDirectory(client.TenantId);
        foreach (var extension in PictureExtensions.Values)
        {
            var path = Path.Combine(directory, $"{client.Id}{extension}");
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }
}
