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
public class ClientsController(AppDbContext db) : ControllerBase
{
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

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionKeys.PropertiesDelete)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var client = await FindAsync(id);
        if (client is null) return NotFound();
        db.Clients.Remove(client);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<Client?> FindAsync(Guid id) =>
        await db.Clients.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == User.GetTenantId());

    private static ClientDto ToDto(Client c) =>
        new(c.Id, c.Name, c.Cnic, c.Phone, c.Address, c.FatherHusband, c.Notes, c.CreatedAt);
}
