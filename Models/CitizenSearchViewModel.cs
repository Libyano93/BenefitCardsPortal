using System.ComponentModel.DataAnnotations;

namespace BenefitCardsPortal.Models;

public class CitizenSearchViewModel
{
    [StringLength(200)] public string? Query { get; set; }
    public IReadOnlyList<BenefitCardRecord> Results { get; set; } = Array.Empty<BenefitCardRecord>();
    public bool HasSearched { get; set; }
}
