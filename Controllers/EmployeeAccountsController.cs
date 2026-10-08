using BenefitCardsPortal.Data;
using BenefitCardsPortal.Models;
using BenefitCardsPortal.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BenefitCardsPortal.Controllers;

[Authorize(Roles = EmployeeAccountSeeder.AdminRole)]
public sealed class EmployeeAccountsController(
    UserManager<PortalUser> userManager,
    BenefitsDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        // Include every identity account so a missing role link cannot hide a newly created employee.
        var employees = await userManager.Users.OrderBy(user => user.Email).ToListAsync(cancellationToken);
        var rows = new List<EmployeeAccountRowViewModel>(employees.Count);
        foreach (var employee in employees)
            rows.Add(new EmployeeAccountRowViewModel(
                employee.Id,
                string.IsNullOrWhiteSpace(employee.DisplayName) ? employee.UserName ?? "موظف" : employee.DisplayName,
                employee.Email ?? string.Empty,
                await userManager.IsInRoleAsync(employee, EmployeeAccountSeeder.EmployeeRole),
                await userManager.IsInRoleAsync(employee, EmployeeAccountSeeder.AdminRole),
                employee.Id == userManager.GetUserId(User),
                employee.IsActive,
                employee.MustChangePassword,
                new HashSet<string>(await GetAssignedPermissionsAsync(employee), StringComparer.Ordinal)));
        ViewBag.PermissionOptions = EmployeePermissionCatalog.Options;
        return View(rows);
    }

    [HttpGet]
    public IActionResult Create() => View(new RegisterEmployeeViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RegisterEmployeeViewModel model, CancellationToken cancellationToken)
    {
        model.Email = model.Email.Trim();
        model.Permissions = (model.Permissions ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (model.Permissions.Any(permission => !EmployeePermissionCatalog.IsKnown(permission)))
            ModelState.AddModelError(nameof(model.Permissions), "تحتوي الصلاحيات المختارة على قيمة غير معروفة.");
        if (model.IsAdmin) model.Permissions.Clear();
        if (await userManager.FindByEmailAsync(model.Email) is not null)
            ModelState.AddModelError(nameof(model.Email), "هذا البريد مسجل مسبقًا.");
        if (!ModelState.IsValid) return View(model);

        var employee = new PortalUser { UserName = model.Email, Email = model.Email, DisplayName = model.DisplayName.Trim(), EmailConfirmed = true, IsActive = true, MustChangePassword = true };
        var result = await userManager.CreateAsync(employee, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        var employeeRoleResult = await userManager.AddToRoleAsync(employee, EmployeeAccountSeeder.EmployeeRole);
        var adminRoleResult = model.IsAdmin
            ? await userManager.AddToRoleAsync(employee, EmployeeAccountSeeder.AdminRole)
            : IdentityResult.Success;
        var permissionResults = new List<IdentityResult>();
        if (!model.IsAdmin)
            foreach (var permission in model.Permissions)
                permissionResults.Add(await userManager.AddToRoleAsync(employee, permission));
        if (!employeeRoleResult.Succeeded || !adminRoleResult.Succeeded || permissionResults.Any(result => !result.Succeeded))
        {
            foreach (var error in employeeRoleResult.Errors.Concat(adminRoleResult.Errors).Concat(permissionResults.SelectMany(result => result.Errors))) ModelState.AddModelError(string.Empty, error.Description);
            await userManager.DeleteAsync(employee);
            return View(model);
        }

        var permissionsSummary = model.IsAdmin ? "كامل الصلاحيات" : string.Join("، ", EmployeePermissionCatalog.Options.Where(option => model.Permissions.Contains(option.Role)).Select(option => option.Label));
        LogEvent("إنشاء حساب موظف", $"إنشاء حساب موظف {employee.DisplayName} ({employee.Email}) مع الصلاحيات: {(string.IsNullOrWhiteSpace(permissionsSummary) ? "عرض لوحة الموظف فقط" : permissionsSummary)}");
        await db.SaveChangesAsync(cancellationToken);
        TempData["Notice"] = $"تم إنشاء حساب الموظف {employee.DisplayName}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(string userId, bool activate, CancellationToken cancellationToken)
    {
        var employee = await userManager.FindByIdAsync(userId);
        if (employee is null) return NotFound();
        if (employee.IsActive == activate) return RedirectToAction(nameof(Index));
        if (!activate && employee.Id == userManager.GetUserId(User))
        {
            TempData["Error"] = "لا يمكنك تعطيل حسابك الحالي.";
            return RedirectToAction(nameof(Index));
        }
        var isAdmin = await userManager.IsInRoleAsync(employee, EmployeeAccountSeeder.AdminRole);
        if (!activate && isAdmin)
        {
            var admins = await userManager.GetUsersInRoleAsync(EmployeeAccountSeeder.AdminRole);
            if (admins.Count(admin => admin.IsActive) <= 1)
            {
                TempData["Error"] = "يجب أن يبقى مسؤول نشط واحد على الأقل.";
                return RedirectToAction(nameof(Index));
            }
        }
        employee.IsActive = activate;
        var result = await userManager.UpdateAsync(employee);
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join("، ", result.Errors.Select(error => error.Description));
            return RedirectToAction(nameof(Index));
        }
        await userManager.UpdateSecurityStampAsync(employee);
        LogEvent(activate ? "إعادة تفعيل حساب موظف" : "تعطيل حساب موظف", $"{(activate ? "إعادة تفعيل" : "تعطيل")} حساب {employee.Email} مع الاحتفاظ بسجلاته وصلاحياته");
        await db.SaveChangesAsync(cancellationToken);
        TempData["Notice"] = activate ? "تم تفعيل حساب الموظف." : "تم تعطيل الحساب مع الاحتفاظ بسجلاته.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPermissions(string userId, string[] permissions, CancellationToken cancellationToken)
    {
        var employee = await userManager.FindByIdAsync(userId);
        if (employee is null) return NotFound();
        if (!await userManager.IsInRoleAsync(employee, EmployeeAccountSeeder.EmployeeRole)) return BadRequest();
        if (await userManager.IsInRoleAsync(employee, EmployeeAccountSeeder.AdminRole))
        {
            TempData["Error"] = "حساب المسؤول يمتلك جميع الصلاحيات التشغيلية تلقائيًا.";
            return RedirectToAction(nameof(Index));
        }
        var selected = permissions.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        if (selected.Any(permission => !EmployeePermissionCatalog.IsKnown(permission))) return BadRequest();
        foreach (var option in EmployeePermissionCatalog.Options)
        {
            var hasPermission = await userManager.IsInRoleAsync(employee, option.Role);
            if (hasPermission == selected.Contains(option.Role)) continue;
            var result = selected.Contains(option.Role)
                ? await userManager.AddToRoleAsync(employee, option.Role)
                : await userManager.RemoveFromRoleAsync(employee, option.Role);
            if (!result.Succeeded)
            {
                TempData["Error"] = string.Join("، ", result.Errors.Select(error => error.Description));
                return RedirectToAction(nameof(Index));
            }
        }
        var labels = EmployeePermissionCatalog.Options.Where(option => selected.Contains(option.Role)).Select(option => option.Label);
        await userManager.UpdateSecurityStampAsync(employee);
        LogEvent("تغيير صلاحيات موظف", $"تحديث صلاحيات {employee.Email}: {(selected.Count == 0 ? "عرض لوحة الموظف فقط" : string.Join("، ", labels))}");
        await db.SaveChangesAsync(cancellationToken);
        TempData["Notice"] = "تم حفظ صلاحيات الموظف.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetAdminRole(string userId, bool grant, CancellationToken cancellationToken)
    {
        var employee = await userManager.FindByIdAsync(userId);
        if (employee is null) return NotFound();
        var isEmployee = await userManager.IsInRoleAsync(employee, EmployeeAccountSeeder.EmployeeRole);
        var isAdmin = await userManager.IsInRoleAsync(employee, EmployeeAccountSeeder.AdminRole);
        if (grant == isAdmin) return RedirectToAction(nameof(Index));
        if (grant && !isEmployee)
        {
            var employeeRoleResult = await userManager.AddToRoleAsync(employee, EmployeeAccountSeeder.EmployeeRole);
            if (!employeeRoleResult.Succeeded)
            {
                TempData["Error"] = string.Join("، ", employeeRoleResult.Errors.Select(error => error.Description));
                return RedirectToAction(nameof(Index));
            }
        }

        if (!grant)
        {
            var currentUserId = userManager.GetUserId(User);
            var admins = await userManager.GetUsersInRoleAsync(EmployeeAccountSeeder.AdminRole);
            if (employee.Id == currentUserId || admins.Count <= 1)
            {
                TempData["Error"] = employee.Id == currentUserId
                    ? "لا يمكنك إزالة صلاحية المسؤول عن حسابك الحالي."
                    : "يجب أن يبقى في النظام مسؤول واحد على الأقل.";
                return RedirectToAction(nameof(Index));
            }
        }

        var result = grant
            ? await userManager.AddToRoleAsync(employee, EmployeeAccountSeeder.AdminRole)
            : await userManager.RemoveFromRoleAsync(employee, EmployeeAccountSeeder.AdminRole);
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join("، ", result.Errors.Select(error => error.Description));
            return RedirectToAction(nameof(Index));
        }

        await userManager.UpdateSecurityStampAsync(employee);
        LogEvent(grant ? "منح صلاحية مسؤول" : "إزالة صلاحية مسؤول", $"{(grant ? "منح" : "إزالة")} صلاحية المسؤول عن {employee.Email}");
        await db.SaveChangesAsync(cancellationToken);
        TempData["Notice"] = grant ? "تم منح صلاحية المسؤول." : "تمت إزالة صلاحية المسؤول.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetEmployeeRole(string userId, CancellationToken cancellationToken)
    {
        var employee = await userManager.FindByIdAsync(userId);
        if (employee is null) return NotFound();
        if (await userManager.IsInRoleAsync(employee, EmployeeAccountSeeder.EmployeeRole)) return RedirectToAction(nameof(Index));
        var result = await userManager.AddToRoleAsync(employee, EmployeeAccountSeeder.EmployeeRole);
        if (!result.Succeeded)
        {
            TempData["Error"] = string.Join("، ", result.Errors.Select(error => error.Description));
            return RedirectToAction(nameof(Index));
        }
        await userManager.UpdateSecurityStampAsync(employee);
        LogEvent("تفعيل حساب موظف", $"تفعيل حساب {employee.Email} كموظف");
        await db.SaveChangesAsync(cancellationToken);
        TempData["Notice"] = "تم تفعيل الحساب كموظف، وأصبح ظاهرًا ضمن قائمة الموظفين.";
        return RedirectToAction(nameof(Index));
    }

    private void LogEvent(string action, string summary) => db.EmployeeAuditEvents.Add(new EmployeeAuditEvent
    {
        Actor = User.Identity?.Name ?? "مسؤول غير معروف",
        Action = action,
        Summary = summary,
        OccurredAtUtc = DateTime.UtcNow
    });

    private async Task<IReadOnlyList<string>> GetAssignedPermissionsAsync(PortalUser employee)
    {
        var assigned = new List<string>();
        foreach (var option in EmployeePermissionCatalog.Options)
            if (await userManager.IsInRoleAsync(employee, option.Role)) assigned.Add(option.Role);
        return assigned;
    }
}
