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
public class PropertiesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionKeys.PropertiesView)]
    public async Task<ActionResult<List<PropertyDto>>> List()
    {
        var tenantId = User.GetTenantId();
        var items = await db.Properties
            .Include(p => p.Client)
            .Where(p => p.TenantId == tenantId)
            .OrderBy(p => p.PropertyNumber)
            .ToListAsync();
        return Ok(items.Select(ToDto).ToList());
    }

    [HttpPost]
    [RequirePermission(PermissionKeys.PropertiesCreate)]
    public async Task<ActionResult<PropertyDto>> Create(PropertyRequest req)
    {
        var property = Map(new Property { TenantId = User.GetTenantId() }, req);
        db.Properties.Add(property);
        await db.SaveChangesAsync();
        await db.Entry(property).Reference(p => p.Client).LoadAsync();
        return Ok(ToDto(property));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionKeys.PropertiesEdit)]
    public async Task<ActionResult<PropertyDto>> Update(Guid id, PropertyRequest req)
    {
        var property = await FindAsync(id);
        if (property is null) return NotFound();
        Map(property, req);
        await db.SaveChangesAsync();
        await db.Entry(property).Reference(p => p.Client).LoadAsync();
        return Ok(ToDto(property));
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionKeys.PropertiesDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var property = await FindAsync(id);
        if (property is null) return NotFound();
        db.Properties.Remove(property);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<Property?> FindAsync(Guid id) =>
        await db.Properties.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == User.GetTenantId());

    private static Property Map(Property p, PropertyRequest req)
    {
        p.PropertyNumber = req.PropertyNumber.Trim();
        p.PropertyType = req.PropertyType;
        p.Marla = req.Marla;
        p.TotalPrice = req.TotalPrice;
        p.BookingDate = req.BookingDate;
        p.Status = req.Status;
        p.ClientId = req.ClientId;
        p.Notes = req.Notes;
        return p;
    }

    private static PropertyDto ToDto(Property p) => new(
        p.Id, p.PropertyNumber, p.PropertyType, p.Marla, p.TotalPrice,
        p.BookingDate, p.Status, p.ClientId, p.Notes, p.CreatedAt,
        p.Client is null ? null : new ClientDto(p.Client.Id, p.Client.Name, p.Client.Cnic, p.Client.Phone, p.Client.Address, p.Client.FatherHusband, p.Client.Notes, p.Client.CreatedAt));
}
