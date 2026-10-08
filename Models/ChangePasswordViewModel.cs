using System.ComponentModel.DataAnnotations;

namespace BenefitCardsPortal.Models;

public sealed class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 10), DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage = "كلمتا المرور غير متطابقتين.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
