using BenefitCardsPortal.Data;
using BenefitCardsPortal.Models;
using BenefitCardsPortal.Security;
using BenefitCardsPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BenefitCardsPortal.Controllers;

[Authorize(Roles = EmployeeAccountSeeder.AdminRole + "," + EmployeeAccountSeeder.SettingsManageRole)]
public sealed class SettingsController(IAuditPolicyStore policyStore, BenefitsDbContext db, ISmtpSettingsStore smtpSettings, ISmtpEmailSender emailSender, IBackupService backupService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var policy = await policyStore.GetAsync(cancellationToken);
        return View(ToViewModel(policy, await smtpSettings.GetAsync(cancellationToken)));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(AuditSettingsViewModel model, CancellationToken cancellationToken)
    {
        model.NotificationEmail = model.NotificationEmail?.Trim() ?? string.Empty;
        if (model.RetentionUnit == "hours" && model.RetentionAmount > 87600)
            ModelState.AddModelError(nameof(model.RetentionAmount), "الحد الأقصى 87600 ساعة.");
        if (model.RetentionUnit == "days" && model.RetentionAmount > 3650)
            ModelState.AddModelError(nameof(model.RetentionAmount), "الحد الأقصى 3650 يومًا.");
        model.SmtpConfigured = (await smtpSettings.GetAsync(cancellationToken)).IsConfigured;
        if (!ModelState.IsValid) return View(model);

        await policyStore.SaveAsync(new AuditPolicy(model.CleanupEnabled, model.RetentionAmount, model.RetentionUnit, model.NotificationEmail), cancellationToken);
        db.EmployeeAuditEvents.Add(new EmployeeAuditEvent
        {
            Actor = User.Identity?.Name ?? "موظف غير معروف",
            Action = "تغيير إعدادات السجل",
            Summary = $"تم تحديث مدة الاحتفاظ بسجلات العمليات إلى {model.RetentionAmount} {(model.RetentionUnit == "hours" ? "ساعة" : "يوم")}; التنظيف التلقائي {(model.CleanupEnabled ? "مفعّل" : "متوقف")}."
        });
        await db.SaveChangesAsync(cancellationToken);
        TempData["Notice"] = "تم حفظ إعدادات سجل العمليات والتنبيهات.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Email(CancellationToken cancellationToken)
    {
        var smtp = await smtpSettings.GetAsync(cancellationToken);
        var policy = await policyStore.GetAsync(cancellationToken);
        return View(new EmailSettingsViewModel
        {
            Host = smtp.Host,
            Port = smtp.Port,
            EnableSsl = smtp.EnableSsl,
            FromAddress = smtp.FromAddress,
            UserName = smtp.UserName,
            NotificationEmail = policy.NotificationEmail,
            HasSavedPassword = await smtpSettings.HasSavedPasswordAsync(cancellationToken),
            SmtpConfigured = smtp.IsConfigured,
            EnvironmentOverrides = smtpSettings.HasEnvironmentOverrides
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Email(EmailSettingsViewModel model, bool sendTest = false, CancellationToken cancellationToken = default)
    {
        model.Host = model.Host.Trim();
        model.FromAddress = model.FromAddress.Trim();
        model.UserName = model.UserName.Trim();
        model.NotificationEmail = model.NotificationEmail.Trim();
        var current = await smtpSettings.GetAsync(cancellationToken);
        if (!model.ClearPassword && !string.IsNullOrWhiteSpace(model.UserName) && string.IsNullOrWhiteSpace(model.Password) && string.IsNullOrWhiteSpace(current.Password))
            ModelState.AddModelError(nameof(model.Password), "أدخل كلمة مرور البريد للمصادقة أو اترك اسم المستخدم فارغًا لخادم الإرسال الداخلي.");
        if (!ModelState.IsValid)
        {
            model.HasSavedPassword = await smtpSettings.HasSavedPasswordAsync(cancellationToken);
            model.EnvironmentOverrides = smtpSettings.HasEnvironmentOverrides;
            model.SmtpConfigured = current.IsConfigured;
            return View(model);
        }

        await smtpSettings.SaveAsync(model, cancellationToken);
        var policy = await policyStore.GetAsync(cancellationToken);
        await policyStore.SaveAsync(policy with { NotificationEmail = model.NotificationEmail }, cancellationToken);
        db.EmployeeAuditEvents.Add(new EmployeeAuditEvent
        {
            Actor = User.Identity?.Name ?? "مسؤول غير معروف",
            Action = "تحديث إعدادات البريد",
            Summary = $"تحديث خادم البريد {model.Host}:{model.Port}؛ تنبيهات سجل العمليات إلى {model.NotificationEmail}."
        });
        await db.SaveChangesAsync(cancellationToken);

        if (sendTest)
        {
            try
            {
                await emailSender.SendAsync(model.NotificationEmail, "رسالة اختبار - بوابة بطاقات المستفيدين",
                    "نجح إعداد البريد في بوابة بطاقات المستفيدين. ستصل إلى هذا العنوان تنبيهات سجل العمليات.", cancellationToken);
                TempData["Notice"] = $"تم حفظ الإعدادات وإرسال رسالة اختبار إلى {model.NotificationEmail}.";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                TempData["Error"] = $"حُفظت الإعدادات، لكن تعذر إرسال الرسالة التجريبية: {exception.Message}";
            }
        }
        else TempData["Notice"] = "تم حفظ إعدادات البريد. أرسل رسالة تجريبية للتحقق من الاتصال.";
        return RedirectToAction(nameof(Email));
    }

    [HttpGet]
    public async Task<IActionResult> Backup(CancellationToken cancellationToken)
    {
        var policy = await backupService.GetPolicyAsync(cancellationToken);
        return View(new BackupSettingsViewModel
        {
            Enabled = policy.Enabled,
            DailyTime = policy.DailyTime,
            RetentionDays = policy.RetentionDays,
            Backups = backupService.ListBackups()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Backup(BackupSettingsViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            model.Backups = backupService.ListBackups();
            return View(model);
        }
        await backupService.SavePolicyAsync(new BackupPolicy(model.Enabled, model.DailyTime, model.RetentionDays), cancellationToken);
        db.EmployeeAuditEvents.Add(new EmployeeAuditEvent
        {
            Actor = User.Identity?.Name ?? "مسؤول غير معروف",
            Action = "تحديث إعدادات النسخ الاحتياطي",
            Summary = $"النسخ التلقائي {(model.Enabled ? "مفعّل" : "متوقف")}; الموعد اليومي {model.DailyTime} بتوقيت ليبيا؛ الاحتفاظ {model.RetentionDays} يومًا."
        });
        await db.SaveChangesAsync(cancellationToken);
        TempData["Notice"] = "تم حفظ إعدادات النسخ الاحتياطي.";
        return RedirectToAction(nameof(Backup));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBackupNow(CancellationToken cancellationToken)
    {
        try
        {
            var fileName = await backupService.CreateBackupAsync(cancellationToken);
            db.EmployeeAuditEvents.Add(new EmployeeAuditEvent
            {
                Actor = User.Identity?.Name ?? "مسؤول غير معروف",
                Action = "إنشاء نسخة احتياطية",
                Summary = $"إنشاء نسخة احتياطية يدوية لقاعدة البيانات والكشوف: {fileName}"
            });
            await db.SaveChangesAsync(cancellationToken);
            TempData["Notice"] = $"تم إنشاء النسخة الاحتياطية {fileName}.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            TempData["Error"] = $"تعذر إنشاء النسخة الاحتياطية: {exception.Message}";
        }
        return RedirectToAction(nameof(Backup));
    }

    [HttpGet]
    public IActionResult DownloadBackup(string fileName)
    {
        var path = backupService.FindBackupPath(fileName);
        if (path is null) return NotFound();
        return PhysicalFile(path, "application/zip", Path.GetFileName(path));
    }

    private static AuditSettingsViewModel ToViewModel(AuditPolicy policy, SmtpMailSettings smtp) => new()
    {
        CleanupEnabled = policy.CleanupEnabled,
        RetentionAmount = policy.RetentionAmount,
        RetentionUnit = policy.RetentionUnit,
        NotificationEmail = policy.NotificationEmail,
        SmtpConfigured = smtp.IsConfigured
    };
}
