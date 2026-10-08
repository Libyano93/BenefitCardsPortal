namespace BenefitCardsPortal.Models;

public sealed record BackupPolicy(bool Enabled, string DailyTime, int RetentionDays);
