namespace BenefitCardsPortal.Models;

public sealed class ReceiptViewModel
{
    public int RecordId { get; init; }
    public string BeneficiaryName { get; init; } = string.Empty;
    public string? CardName { get; init; }
    public string? SerialNumber { get; init; }
    public string? SourceRowNumber { get; init; }
    public string? CardNumber { get; init; }
    public int Month { get; init; }
    public int Year { get; init; }
    public DateTime ReceivedAtLocal { get; init; }
    public string ReceivedBy { get; init; } = string.Empty;
}
