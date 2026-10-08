namespace BenefitCardsPortal.Models;

public sealed class EmployeeAuditViewModel
{
    public required IReadOnlyList<EmployeeAuditEvent> Events { get; init; }
    public string? Action { get; init; }
    public string? Employee { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
}
