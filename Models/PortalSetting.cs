using System.ComponentModel.DataAnnotations;

namespace BenefitCardsPortal.Models;

public sealed class PortalSetting
{
    [Key, StringLength(100)] public string Key { get; set; } = string.Empty;
    [Required, StringLength(1000)] public string Value { get; set; } = string.Empty;
}
