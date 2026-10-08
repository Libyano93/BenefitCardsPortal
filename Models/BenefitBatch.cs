using System.ComponentModel.DataAnnotations;

namespace BenefitCardsPortal.Models;

public class BenefitBatch
{
    public int Id { get; set; }
    [Range(1, 12)] public int ValidityMonth { get; set; }
    [Range(2000, 2100)] public int ValidityYear { get; set; }
    [Required, StringLength(260)] public string SourceFileName { get; set; } = string.Empty;
    [StringLength(520)] public string StoredFilePath { get; set; } = string.Empty;
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsReviewed { get; set; }
    public ICollection<BenefitCardRecord> Records { get; set; } = new List<BenefitCardRecord>();
}
