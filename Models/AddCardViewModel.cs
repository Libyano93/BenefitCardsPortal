using System.ComponentModel.DataAnnotations;

namespace BenefitCardsPortal.Models;

public class AddCardViewModel
{
    public int BatchId { get; set; }
    [StringLength(20)] public string? SourceRowNumber { get; set; }
    [StringLength(200)] public string? CardName { get; set; }
    [Required, StringLength(200)] public string BeneficiaryName { get; set; } = string.Empty;
    [StringLength(32)] public string? NationalNumber { get; set; }
    [StringLength(40)] public string? CardNumber { get; set; }
    [StringLength(40)] public string? SerialNumber { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
}
