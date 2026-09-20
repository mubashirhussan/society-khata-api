namespace SocietyKhata.Api.Dtos;

public record RegisterRequest(string TenantName, string Email, string Password, string? FullName, string? Phone);
public record LoginRequest(string Email, string Password);
public record CreateUserRequest(string Email, string Password, int RoleId, string? FullName);
public record AuthResponse(string Token, UserDto User);
public record UserDto(
    int Id, string Email, int RoleId, string RoleName, string? FullName,
    int TenantId, string TenantName, List<string> Permissions, bool HasLogo = false,
    bool IsPlatformManager = false);
public record UserListDto(int Id, string Email, int RoleId, string RoleName, string? FullName, bool IsActive, DateTime CreatedAt);

public record PermissionDto(string Key, string Name, string Group);
public record PermissionGroupDto(string Group, List<PermissionDto> Permissions);
public record RoleDto(int Id, string Name, bool IsSystem, List<string> Permissions);
public record UpdateRolePermissionsRequest(List<string> PermissionKeys);

public record ClientDto(int Id, string Name, string? Cnic, string? Phone, string? Address, string? FatherHusband, string? Notes, DateTime CreatedAt, bool HasPicture = false, string? Caste = null);
public record ClientRequest(string Name, string? Cnic, string? Phone, string? Address, string? FatherHusband, string? Notes, string? Caste = null);

public record PropertyDto(
    int Id, string PropertyNumber, string PropertyType, decimal? Marla, decimal TotalPrice,
    DateOnly? BookingDate, string Status, int? ClientId, string? Notes, DateTime CreatedAt, ClientDto? Client,
    decimal? LengthFeet = null, decimal? WidthFeet = null);

public record PropertyRequest(
    string PropertyNumber, string PropertyType, decimal? Marla, decimal TotalPrice,
    DateOnly? BookingDate, string Status, int? ClientId, string? Notes,
    decimal? LengthFeet = null, decimal? WidthFeet = null);

public record PaymentDto(
    int Id, string? ReceiptNo, int? ClientId, int? PropertyId, decimal Amount,
    DateOnly PaymentDate, string? Notes, DateTime CreatedAt, ClientDto? Client, PropertyDto? Property);

public record InstallmentScheduleItem(DateOnly DueDate, decimal Amount);

public record PaymentRequest(
    string? ReceiptNo, int? ClientId, int? PropertyId, decimal Amount,
    DateOnly PaymentDate, string? Notes, string PaymentMethod = "installment",
    List<InstallmentScheduleItem>? InstallmentSchedule = null, int? InstallmentDueId = null,
    string? PlanFrequency = null);

public record PaymentLedgerDto(
    int Id, string RowType, string Status, string? ReceiptNo,
    int? ClientId, int? PropertyId, decimal Amount, DateOnly Date,
    string? Notes, int? PaymentId, string? PlanFrequency,
    ClientDto? Client, PropertyDto? Property);

public record PaymentClientSummaryDto(
    int ClientId, ClientDto Client, List<string> PropertyNumbers,
    decimal TotalPlotAmount, decimal TotalReceived, decimal PendingAmount, DateOnly LastPaymentDate);

public record PendingInstallmentDto(
    int Id, DateOnly? Date, int? PropertyId, PropertyDto? Property, decimal Amount, string Status);

public record PaymentClientDetailDto(
    ClientDto Client, List<PaymentDto> Payments, List<PendingInstallmentDto> PendingInstallments,
    decimal TotalAmount, decimal TotalReceived, decimal TotalPending, List<string> PlanFrequencies);

public record ExpenseDto(int Id, string Description, decimal Amount, string? PaidTo, DateOnly ExpenseDate, string? Notes, DateTime CreatedAt);
public record ExpenseRequest(string Description, decimal Amount, string? PaidTo, DateOnly ExpenseDate, string? Notes);

public record DashboardStatsDto(
    int TotalPlots, int TotalShops, int TotalSales, decimal TotalReceived,
    decimal TotalExpenses, int TotalProperties, decimal TotalPropertyValue, decimal TotalOutstanding);

public record SocietyOverviewDto(int Id, string Name, string? Phone, DateTime CreatedAt, int UserCount);
public record SocietiesOverviewResponse(int TotalCount, List<SocietyOverviewDto> Societies);
