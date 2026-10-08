namespace BenefitCardsPortal.Models;

public sealed class EmployeeBatchDetailsViewModel
{
    public required BenefitBatch Batch { get; init; }
    public required IReadOnlyList<BenefitCardRecord> Records { get; init; }
    public required IReadOnlyList<BatchQualityFinding> Findings { get; init; }
    public string? Name { get; init; }
    public string Status { get; init; } = "all";
}
