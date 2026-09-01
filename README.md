# Society Khata API

ASP.NET Core 9 Web API + PostgreSQL (multi-tenant, role-based permissions).

## Frontend repo

https://github.com/mubashirhussan/society-khata-web

## Run locally

1. Create PostgreSQL database `society_khata`
2. Copy connection settings into `appsettings.Development.json` (gitignored):

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=society_khata;Username=postgres;Password=YOUR_PASSWORD"
  }
}
```

3. Run:

```bash
dotnet run --launch-profile http
```

API: http://localhost:5084

## Stack

- JWT auth
- Multi-tenant (`TenantId`)
- Roles & permissions (Admin / Accountant defaults)
