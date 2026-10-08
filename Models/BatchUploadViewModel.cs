using System.ComponentModel.DataAnnotations;

namespace BenefitCardsPortal.Models;

public class BatchUploadViewModel
{
    [Range(1, 12, ErrorMessage = "اختر شهرًا صحيحًا.")] public int Month { get; set; }
    [Range(2020, 2100, ErrorMessage = "أدخل سنة صحيحة.")] public int Year { get; set; }
    public IFormFile? File { get; set; }
    public string? DraftId { get; set; }
    public string? SourceFileName { get; set; }
    public string? OcrNotice { get; set; }
    public bool HasDuplicatePeriod { get; set; }
    public bool ConfirmDuplicatePeriod { get; set; }
    public IReadOnlyList<BatchQualityFinding> ReviewFindings { get; set; } = Array.Empty<BatchQualityFinding>();
    public List<AddCardViewModel> Rows { get; set; } = [];
}
