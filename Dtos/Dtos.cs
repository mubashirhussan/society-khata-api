namespace SocietyKhata.Api.Dtos;

public record RegisterRequest(string TenantName, string Email, string Password, string? FullName, string? Phone);
public record LoginRequest(string Email, string Password);
public record CreateUserRequest(string Email, string Password, Guid RoleId, string? FullName);
public record AuthResponse(string Token, UserDto User);
public record UserDto(
    Guid Id, string Email, Guid RoleId, string RoleName, string? FullName,
    Guid TenantId, string TenantName, List<string> Permissions);
public record UserListDto(Guid Id, string Email, Guid RoleId, string RoleName, string? FullName, bool IsActive, DateTime CreatedAt);

public record PermissionDto(string Key, string Name, string Group);
public record PermissionGroupDto(string Group, List<PermissionDto> Permissions);
public record RoleDto(Guid Id, string Name, bool IsSystem, List<string> Permissions);
public record UpdateRolePermissionsRequest(List<string> PermissionKeys);

public record ClientDto(Guid Id, string Name, string? Cnic, string? Phone, string? Address, string? FatherHusband, string? Notes, DateTime CreatedAt, bool HasPicture = false);
public record ClientRequest(string Name, string? Cnic, string? Phone, string? Address, string? FatherHusband, string? Notes);

public record PropertyDto(
    Guid Id, string PropertyNumber, string PropertyType, decimal? Marla, decimal TotalPrice,
    DateOnly? BookingDate, string Status, Guid? ClientId, string? Notes, DateTime CreatedAt, ClientDto? Client);

public record PropertyRequest(
    string PropertyNumber, string PropertyType, decimal? Marla, decimal TotalPrice,
    DateOnly? BookingDate, string Status, Guid? ClientId, string? Notes);

public record PaymentDto(
    Guid Id, string? ReceiptNo, Guid? ClientId, Guid? PropertyId, decimal Amount,
    DateOnly PaymentDate, string? Notes, DateTime CreatedAt, ClientDto? Client, PropertyDto? Property);

public record InstallmentScheduleItem(DateOnly DueDate, decimal Amount);

public record PaymentRequest(
    string? ReceiptNo, Guid? ClientId, Guid? PropertyId, decimal Amount,
    DateOnly PaymentDate, string? Notes, string PaymentMethod = "installment",
    List<InstallmentScheduleItem>? InstallmentSchedule = null, Guid? InstallmentDueId = null,
    string? PlanFrequency = null);

public record PaymentLedgerDto(
    Guid Id, string RowType, string Status, string? ReceiptNo,
    Guid? ClientId, Guid? PropertyId, decimal Amount, DateOnly Date,
    string? Notes, Guid? PaymentId, string? PlanFrequency,
    ClientDto? Client, PropertyDto? Property);

public record ExpenseDto(Guid Id, string Description, decimal Amount, string? PaidTo, DateOnly ExpenseDate, string? Notes, DateTime CreatedAt);
public record ExpenseRequest(string Description, decimal Amount, string? PaidTo, DateOnly ExpenseDate, string? Notes);

public record DashboardStatsDto(
    int TotalPlots, int TotalShops, int TotalSales, decimal TotalReceived,
    decimal TotalExpenses, int TotalProperties, decimal TotalPropertyValue, decimal TotalOutstanding);
