using BenefitCardsPortal.Models;

namespace BenefitCardsPortal.Services;

public interface ISmtpSettingsStore
{
    Task<SmtpMailSettings> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(EmailSettingsViewModel model, CancellationToken cancellationToken = default);
    Task<bool> HasSavedPasswordAsync(CancellationToken cancellationToken = default);
    bool HasEnvironmentOverrides { get; }
}
