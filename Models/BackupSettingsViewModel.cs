using System.ComponentModel.DataAnnotations;

namespace BenefitCardsPortal.Models;

public sealed class BackupSettingsViewModel
{
    public bool Enabled { get; set; } = true;
    [Required, RegularExpression("^([01]\\d|2[0-3]):[0-5]\\d$", ErrorMessage = "أدخل الوقت بصيغة ساعة:دقيقة، مثل 02:00.")]
    public string DailyTime { get; set; } = "02:00";
    [Range(1, 3650, ErrorMessage = "مدة الاحتفاظ يجب أن تكون من يوم إلى 3650 يومًا.")]
    public int RetentionDays { get; set; } = 30;
    public IReadOnlyList<BackupFileViewModel> Backups { get; set; } = Array.Empty<BackupFileViewModel>();
}
