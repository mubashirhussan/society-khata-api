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
            .Where(c => c.TenantId == tenantId && !c.IsDeleted)
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
            Caste = req.Caste,
            Notes = req.Notes
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return Ok(ToDto(client));
    }

    [HttpPut("{id:int}")]
    [RequirePermission(PermissionKeys.PropertiesEdit)]
    public async Task<ActionResult<ClientDto>> Update(int id, ClientRequest req)
    {
        var client = await FindAsync(id);
        if (client is null) return NotFound();

        client.Name = req.Name.Trim();
        client.Cnic = req.Cnic;
        client.Phone = req.Phone;
        client.Address = req.Address;
        client.FatherHusband = req.FatherHusband;
        client.Caste = req.Caste;
        client.Notes = req.Notes;
        await db.SaveChangesAsync();
        return Ok(ToDto(client));
    }

    [HttpPost("{id:int}/picture")]
    [RequirePermission(PermissionKeys.PropertiesCreate)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ActionResult<ClientDto>> UploadPicture(int id, IFormFile? picture)
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

    [HttpGet("{id:int}/picture")]
    [RequirePermission(PermissionKeys.PropertiesView)]
    public async Task<IActionResult> GetPicture(int id)
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

    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionKeys.PropertiesDelete)]
    public async Task<IActionResult> Delete(int id)
    {
        var tenantId = User.GetTenantId();
        // Deliberately not filtered by !IsDeleted: a client soft-deleted by an older, buggier
        // version of this endpoint can be left with orphaned payments/dues never cleaned up.
        // Re-running delete on that same client must finish the cleanup instead of 404ing,
        // since IsDeleted = true again is a harmless no-op if it was already deleted properly.
        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId);
        if (client is null) return NotFound();

        var properties = await db.Properties
            .Where(p => p.TenantId == tenantId && p.ClientId == id)
            .ToListAsync();
        var propertyIds = properties.Select(p => p.Id).ToHashSet();

        // Catch every payment tied to this client — whether through a property it currently
        // owns, or directly via the payment's own ClientId. The two can drift apart (e.g. a
        // property's ownership field getting reset while its payment history stays put), so
        // relying on the property link alone can leave orphaned payments behind after delete.
        var payments = await db.Payments
            .Where(p => p.TenantId == tenantId
                && (p.ClientId == id || (p.PropertyId.HasValue && propertyIds.Contains(p.PropertyId.Value))))
            .ToListAsync();
        foreach (var payment in payments)
            payment.IsDeleted = true;

        var pendingDues = await db.InstallmentDues
            .Where(d => d.TenantId == tenantId && d.ClientId == id && d.Status == "pending")
            .ToListAsync();
        foreach (var due in pendingDues)
            due.Status = "cancelled";

        foreach (var property in properties)
        {
            property.Status = "available";
            property.ClientId = null;
            property.BookingDate = null;
        }

        DeletePictureFiles(client);
        client.IsDeleted = true;
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<Client?> FindAsync(int id) =>
        await db.Clients.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == User.GetTenantId() && !c.IsDeleted);

    private ClientDto ToDto(Client c) =>
        new(c.Id, c.Name, c.Cnic, c.Phone, c.Address, c.FatherHusband, c.Notes, c.CreatedAt, FindPicturePath(c) is not null, c.Caste);

    private string GetPictureDirectory(int tenantId) =>
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
