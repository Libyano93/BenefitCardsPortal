namespace BenefitCardsPortal.Models;

public sealed record AuditPolicy(bool CleanupEnabled, int RetentionAmount, string RetentionUnit, string NotificationEmail)
{
    public TimeSpan Retention => RetentionUnit == "hours" ? TimeSpan.FromHours(RetentionAmount) : TimeSpan.FromDays(RetentionAmount);
    public string RetentionDescription => RetentionUnit == "hours" ? $"{RetentionAmount} ساعة" : $"{RetentionAmount} يوم";
}
