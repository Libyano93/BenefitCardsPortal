using System.ComponentModel.DataAnnotations;

namespace BenefitCardsPortal.Models;

public sealed class EmailSettingsViewModel
{
    [Required, StringLength(250)] public string Host { get; set; } = string.Empty;
    [Range(1, 65535)] public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    [Required, EmailAddress, StringLength(254)] public string FromAddress { get; set; } = string.Empty;
    [StringLength(254)] public string UserName { get; set; } = string.Empty;
    [DataType(DataType.Password), StringLength(500)] public string? Password { get; set; }
    public bool ClearPassword { get; set; }
    [Required, EmailAddress, StringLength(254)] public string NotificationEmail { get; set; } = "sutest410@gmail.com";
    public bool HasSavedPassword { get; set; }
    public bool SmtpConfigured { get; set; }
    public bool EnvironmentOverrides { get; set; }
}
