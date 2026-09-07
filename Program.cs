using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SocietyKhata.Api.Authorization;
using SocietyKhata.Api.Data;
using SocietyKhata.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddSingleton<TenantLogoStorage>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

var jwtKey = builder.Configuration["Jwt:Key"] ?? "SocietyKhata_Dev_Secret_Key_Change_In_Production_Min32!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "SocietyKhata",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "SocietyKhata",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization(options =>
{
    foreach (var permission in PermissionKeys.All)
    {
        options.AddPolicy($"perm:{permission}", policy =>
            policy.Requirements.Add(new PermissionRequirement(permission)));
    }
});

builder.Services.AddCors(opt =>
{
    opt.AddPolicy("Frontend", p =>
        p.WithOrigins(
                builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                ?? ["http://localhost:3000"])
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS "InstallmentDues" (
            "Id" uuid NOT NULL,
            "TenantId" uuid NOT NULL,
            "ClientId" uuid NOT NULL,
            "PropertyId" uuid NOT NULL,
            "DueDate" date NOT NULL,
            "Amount" numeric NOT NULL,
            "AmountPaid" numeric NOT NULL DEFAULT 0,
            "Status" text NOT NULL DEFAULT 'pending',
            "PlanFrequency" text NULL,
            "PaymentId" uuid NULL,
            "CreatedAt" timestamp with time zone NOT NULL,
            CONSTRAINT "PK_InstallmentDues" PRIMARY KEY ("Id"),
            CONSTRAINT "FK_InstallmentDues_Clients_ClientId"
                FOREIGN KEY ("ClientId") REFERENCES "Clients" ("Id") ON DELETE RESTRICT,
            CONSTRAINT "FK_InstallmentDues_Properties_PropertyId"
                FOREIGN KEY ("PropertyId") REFERENCES "Properties" ("Id") ON DELETE RESTRICT,
            CONSTRAINT "FK_InstallmentDues_Payments_PaymentId"
                FOREIGN KEY ("PaymentId") REFERENCES "Payments" ("Id") ON DELETE SET NULL
        );
        ALTER TABLE "InstallmentDues"
            ADD COLUMN IF NOT EXISTS "PlanFrequency" text NULL;
        ALTER TABLE "InstallmentDues"
            ADD COLUMN IF NOT EXISTS "AmountPaid" numeric NOT NULL DEFAULT 0;
        ALTER TABLE "Payments"
            ADD COLUMN IF NOT EXISTS "InstallmentDueId" uuid NULL;
        UPDATE "InstallmentDues"
            SET "AmountPaid" = "Amount"
            WHERE "Status" = 'paid' AND "AmountPaid" = 0 AND "PaymentId" IS NOT NULL;
        UPDATE "Payments" p
            SET "InstallmentDueId" = d."Id"
            FROM "InstallmentDues" d
            WHERE d."PaymentId" = p."Id" AND p."InstallmentDueId" IS NULL;
        CREATE INDEX IF NOT EXISTS "IX_InstallmentDues_TenantId_Status_DueDate"
            ON "InstallmentDues" ("TenantId", "Status", "DueDate");
        CREATE INDEX IF NOT EXISTS "IX_InstallmentDues_ClientId"
            ON "InstallmentDues" ("ClientId");
        CREATE INDEX IF NOT EXISTS "IX_InstallmentDues_PropertyId"
            ON "InstallmentDues" ("PropertyId");
        CREATE INDEX IF NOT EXISTS "IX_InstallmentDues_PaymentId"
            ON "InstallmentDues" ("PaymentId");
        CREATE INDEX IF NOT EXISTS "IX_Payments_InstallmentDueId"
            ON "Payments" ("InstallmentDueId");
        """);
    await DbSeeder.SeedAsync(db);
}

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
