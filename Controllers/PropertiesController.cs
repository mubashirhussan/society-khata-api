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
        var property = Map(new Property
        {
            TenantId = User.GetTenantId(),
            Status = "available",
            ClientId = null
        }, req);
        db.Properties.Add(property);
        await db.SaveChangesAsync();
        await db.Entry(property).Reference(p => p.Client).LoadAsync();
        return Ok(ToDto(property));
    }

    [HttpPut("{id:int}")]
    [RequirePermission(PermissionKeys.PropertiesEdit)]
    public async Task<ActionResult<PropertyDto>> Update(int id, PropertyRequest req)
    {
        var property = await FindAsync(id);
        if (property is null) return NotFound();
        Map(property, req);
        await db.SaveChangesAsync();
        await db.Entry(property).Reference(p => p.Client).LoadAsync();
        return Ok(ToDto(property));
    }

    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionKeys.PropertiesDelete)]
    public async Task<IActionResult> Delete(int id)
    {
        var property = await FindAsync(id);
        if (property is null) return NotFound();
        var tenantId = User.GetTenantId();

        var payments = await db.Payments
            .Where(p => p.TenantId == tenantId && p.PropertyId == id)
            .ToListAsync();
        foreach (var payment in payments)
            payment.IsDeleted = true;

        var pendingDues = await db.InstallmentDues
            .Where(d => d.TenantId == tenantId && d.PropertyId == id && d.Status == "pending")
            .ToListAsync();
        foreach (var due in pendingDues)
            due.Status = "cancelled";

        property.IsDeleted = true;
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<Property?> FindAsync(int id) =>
        await db.Properties.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == User.GetTenantId());

    private static Property Map(Property p, PropertyRequest req)
    {
        p.PropertyNumber = req.PropertyNumber.Trim();
        p.PropertyType = req.PropertyType;
        p.Marla = req.Marla;
        p.LengthFeet = req.LengthFeet;
        p.WidthFeet = req.WidthFeet;
        p.TotalPrice = req.TotalPrice;
        p.BookingDate = req.BookingDate;
        p.Notes = req.Notes;
        return p;
    }

    private static PropertyDto ToDto(Property p) => new(
        p.Id, p.PropertyNumber, p.PropertyType, p.Marla, p.TotalPrice,
        p.BookingDate, p.Status, p.ClientId, p.Notes, p.CreatedAt,
        p.Client is null ? null : new ClientDto(p.Client.Id, p.Client.Name, p.Client.Cnic, p.Client.Phone, p.Client.Address, p.Client.FatherHusband, p.Client.Notes, p.Client.CreatedAt),
        p.LengthFeet, p.WidthFeet);
}
