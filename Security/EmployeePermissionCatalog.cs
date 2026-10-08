namespace BenefitCardsPortal.Security;

public sealed record EmployeePermissionOption(string Role, string Label);

public static class EmployeePermissionCatalog
{
    public static readonly IReadOnlyList<EmployeePermissionOption> Options =
    [
        new(EmployeeAccountSeeder.BatchUploadRole, "رفع كشوف جديدة"),
        new(EmployeeAccountSeeder.BatchEditRole, "إضافة وتعديل السجلات"),
        new(EmployeeAccountSeeder.BatchApproveRole, "اعتماد الكشوف"),
        new(EmployeeAccountSeeder.CardReceiveRole, "تسجيل استلام البطاقات"),
        new(EmployeeAccountSeeder.BatchDeleteRole, "حذف دفعة مع رمز التأكيد"),
        new(EmployeeAccountSeeder.SettingsManageRole, "إدارة إعدادات النظام والبريد")
        ,new(EmployeeAccountSeeder.ReportsViewRole, "عرض تقارير التسليمات وتصديرها")
    ];

    public static bool IsKnown(string role) => Options.Any(option => option.Role == role);
}
