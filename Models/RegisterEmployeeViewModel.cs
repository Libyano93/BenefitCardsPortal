using System.ComponentModel.DataAnnotations;

namespace BenefitCardsPortal.Models;

public sealed class RegisterEmployeeViewModel
{
    [Required, StringLength(120)] public string DisplayName { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = string.Empty;
    [Required, StringLength(100, MinimumLength = 10)] public string Password { get; set; } = string.Empty;
    [Required, Compare(nameof(Password), ErrorMessage = "كلمتا المرور غير متطابقتين.")] public string ConfirmPassword { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public List<string> Permissions { get; set; } = [];
}
