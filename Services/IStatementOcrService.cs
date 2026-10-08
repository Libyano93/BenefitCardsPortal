namespace BenefitCardsPortal.Services;

public interface IStatementOcrService
{
    Task<IReadOnlyList<ExtractedCardRow>> ExtractAsync(string storedFilePath, CancellationToken cancellationToken = default);
}

public sealed record ExtractedCardRow(string? SourceRowNumber, string? SerialNumber, string BeneficiaryName, string? CardName, string? CardNumber, string? NationalNumber, string? Notes, double Confidence);
