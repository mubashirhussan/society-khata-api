namespace SocietyKhata.Api.Data;

public static class PermissionKeys
{
    public const string DashboardView = "dashboard.view";

    public const string PropertiesView = "properties.view";
    public const string PropertiesCreate = "properties.create";
    public const string PropertiesEdit = "properties.edit";
    public const string PropertiesDelete = "properties.delete";

    public const string PaymentsView = "payments.view";
    public const string PaymentsCreate = "payments.create";
    public const string PaymentsEdit = "payments.edit";
    public const string PaymentsDelete = "payments.delete";

    public const string ExpensesView = "expenses.view";
    public const string ExpensesCreate = "expenses.create";
    public const string ExpensesEdit = "expenses.edit";
    public const string ExpensesDelete = "expenses.delete";

    public const string ReportsView = "reports.view";

    public const string UsersView = "users.view";
    public const string UsersManage = "users.manage";

    public const string RolesManage = "roles.manage";

    public static readonly string[] All =
    [
        DashboardView,
        PropertiesView, PropertiesCreate, PropertiesEdit, PropertiesDelete,
        PaymentsView, PaymentsCreate, PaymentsEdit, PaymentsDelete,
        ExpensesView, ExpensesCreate, ExpensesEdit, ExpensesDelete,
        ReportsView,
        UsersView, UsersManage,
        RolesManage
    ];

    public static readonly string[] AccountantDefaults =
    [
        DashboardView,
        PropertiesView,
        PaymentsView, PaymentsCreate, PaymentsEdit, PaymentsDelete,
        ExpensesView, ExpensesCreate, ExpensesEdit, ExpensesDelete,
        ReportsView
    ];
}
