using BenefitCardsPortal.Models;

namespace BenefitCardsPortal.Services;

public interface ICitizenSearchService
{
    Task<IReadOnlyList<BenefitCardRecord>> SearchAsync(string? query, CancellationToken cancellationToken = default);
}
