using BenefitCardsPortal.Models;

namespace BenefitCardsPortal.Services;

public interface IAuditPolicyStore
{
    Task<AuditPolicy> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AuditPolicy policy, CancellationToken cancellationToken = default);
}
