using System.ComponentModel.DataAnnotations;

namespace BenefitCardsPortal.Models;

public sealed class AuditSettingsViewModel
{
    public bool CleanupEnabled { get; set; } = true;
    [Range(1, 87600, ErrorMessage = "أدخل مدة بين 1 و87600.")] public int RetentionAmount { get; set; } = 60;
    [Required, RegularExpression("^(hours|days)$", ErrorMessage = "اختر الساعات أو الأيام.")] public string RetentionUnit { get; set; } = "days";
    [Required, EmailAddress, StringLength(254)] public string NotificationEmail { get; set; } = "sutest410@gmail.com";
    public bool SmtpConfigured { get; set; }
}
