using BenefitCardsPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace BenefitCardsPortal.Services;

public sealed class AuditLogCleanupService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AuditLogCleanupService> logger) : BackgroundService
{
    private bool _smtpWarningLogged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = Math.Clamp(configuration.GetValue("AuditLogCleanup:RunIntervalMinutes", 1), 1, 10080);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await RemoveExpiredEventsAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Audit log cleanup failed; the next scheduled run will retry."); }
        }
    }

    private async Task RemoveExpiredEventsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BenefitsDbContext>();
        var smtp = await scope.ServiceProvider.GetRequiredService<ISmtpSettingsStore>().GetAsync(cancellationToken);
        var emailSender = scope.ServiceProvider.GetRequiredService<ISmtpEmailSender>();
        var policy = await scope.ServiceProvider.GetRequiredService<IAuditPolicyStore>().GetAsync(cancellationToken);
        if (!policy.CleanupEnabled) return;
        if (string.IsNullOrWhiteSpace(policy.NotificationEmail) || !smtp.IsConfigured)
        {
            if (!_smtpWarningLogged) logger.LogWarning("Audit logs are retained because SMTP notification settings are missing or incomplete.");
            _smtpWarningLogged = true;
            return;
        }

        var cutoff = DateTime.UtcNow.Subtract(policy.Retention);
        var expiredCount = await db.EmployeeAuditEvents.CountAsync(entry => entry.OccurredAtUtc < cutoff, cancellationToken);
        if (expiredCount == 0) return;

        // Send before deletion so temporary SMTP outages never silently remove logs without notification.
        await emailSender.SendAsync(policy.NotificationEmail, $"تنظيف سجل عمليات الموظفين - {expiredCount} سجل",
            $"سيحذف النظام {expiredCount} سجل عملية تجاوز مدة الاحتفاظ ({policy.RetentionDescription}).\nالحد الزمني: {cutoff:yyyy-MM-dd HH:mm} UTC.\nالموعد: {DateTime.UtcNow.AddHours(2):yyyy-MM-dd HH:mm} بتوقيت ليبيا.", cancellationToken);
        var deletedCount = await db.EmployeeAuditEvents.Where(entry => entry.OccurredAtUtc < cutoff).ExecuteDeleteAsync(cancellationToken);
        logger.LogInformation("Removed {Count} employee audit events older than {CutoffUtc} after sending the configured notification.", deletedCount, cutoff);
    }

}
