using System.ComponentModel.DataAnnotations;

namespace BenefitCardsPortal.Models;

public sealed class EmployeeAuditEvent
{
    public long Id { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    [Required, StringLength(200)] public string Actor { get; set; } = string.Empty;
    [Required, StringLength(80)] public string Action { get; set; } = string.Empty;
    public int? BatchId { get; set; }
    public int? RecordId { get; set; }
    [StringLength(500)] public string Summary { get; set; } = string.Empty;
}
