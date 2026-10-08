using System.ComponentModel.DataAnnotations;

namespace BenefitCardsPortal.Models;

public class BenefitCardRecord
{
    public int Id { get; set; }
    public int BenefitBatchId { get; set; }
    public BenefitBatch? BenefitBatch { get; set; }
    [StringLength(20)] public string? SourceRowNumber { get; set; }
    [StringLength(200)] public string? CardName { get; set; }
    [Required, StringLength(200)] public string BeneficiaryName { get; set; } = string.Empty;
    public string BeneficiaryNameNormalized { get; set; } = string.Empty;
    // Text preserves leading zeroes in national numbers.
    [StringLength(32)] public string? NationalNumber { get; set; }
    [StringLength(40)] public string? CardNumber { get; set; }
    [StringLength(40)] public string? SerialNumber { get; set; }
    [StringLength(500)] public string? Notes { get; set; }
    public bool IsReceived { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }
    [StringLength(200)] public string? ReceivedBy { get; set; }
}
