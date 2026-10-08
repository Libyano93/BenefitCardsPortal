using Microsoft.AspNetCore.Identity;
using BenefitCardsPortal.Data;
using BenefitCardsPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace BenefitCardsPortal.Security;

public static class EmployeeAccountSeeder
{
    public const string EmployeeRole = "Employee";
    public const string AdminRole = "PortalAdmin";
    public const string BatchUploadRole = "BatchUploader";
    public const string BatchEditRole = "BatchEditor";
    public const string BatchApproveRole = "BatchApprover";
    public const string CardReceiveRole = "CardReceiver";
    public const string BatchDeleteRole = "BatchDeleter";
    public const string SettingsManageRole = "SettingsManager";
    public const string ReportsViewRole = "ReportsViewer";

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var userManager = services.GetRequiredService<UserManager<PortalUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var roles = new[] { EmployeeRole, AdminRole }.Concat(EmployeePermissionCatalog.Options.Select(option => option.Role));
        foreach (var role in roles)
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));

        await MigrateExistingEmployeesAsync(services.GetRequiredService<BenefitsDbContext>(), userManager);
        await MigrateReportsPermissionAsync(services.GetRequiredService<BenefitsDbContext>(), userManager);

        var email = configuration["InitialEmployee:Email"];
        var password = configuration["InitialEmployee:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new PortalUser { UserName = email, Email = email, DisplayName = "موظف الفرع", EmailConfirmed = true, IsActive = true, MustChangePassword = true };
            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
                throw new InvalidOperationException("تعذر إنشاء حساب الموظف الأولي: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }
        if (!await userManager.IsInRoleAsync(user, EmployeeRole))
            await userManager.AddToRoleAsync(user, EmployeeRole);
        if (configuration.GetValue("InitialEmployee:MakeAdmin", false) && !await userManager.IsInRoleAsync(user, AdminRole))
            await userManager.AddToRoleAsync(user, AdminRole);
        if (!configuration.GetValue("InitialEmployee:MakeAdmin", false))
            foreach (var permission in EmployeePermissionCatalog.Options)
                if (!await userManager.IsInRoleAsync(user, permission.Role))
                    await userManager.AddToRoleAsync(user, permission.Role);
    }

    private static async Task MigrateExistingEmployeesAsync(BenefitsDbContext db, UserManager<PortalUser> userManager)
    {
        const string migrationKey = "EmployeePermissionsMigratedV1";
        if (await db.PortalSettings.AnyAsync(setting => setting.Key == migrationKey)) return;
        var employees = await userManager.GetUsersInRoleAsync(EmployeeRole);
        foreach (var employee in employees)
            foreach (var permission in EmployeePermissionCatalog.Options)
                if (!await userManager.IsInRoleAsync(employee, permission.Role))
                    await userManager.AddToRoleAsync(employee, permission.Role);
        db.PortalSettings.Add(new PortalSetting { Key = migrationKey, Value = "true" });
        await db.SaveChangesAsync();
    }

    private static async Task MigrateReportsPermissionAsync(BenefitsDbContext db, UserManager<PortalUser> userManager)
    {
        const string migrationKey = "ReportsPermissionMigratedV1";
        if (await db.PortalSettings.AnyAsync(setting => setting.Key == migrationKey)) return;
        var employees = await userManager.GetUsersInRoleAsync(EmployeeRole);
        foreach (var employee in employees)
            if (!await userManager.IsInRoleAsync(employee, ReportsViewRole))
                await userManager.AddToRoleAsync(employee, ReportsViewRole);
        db.PortalSettings.Add(new PortalSetting { Key = migrationKey, Value = "true" });
        await db.SaveChangesAsync();
    }
}
