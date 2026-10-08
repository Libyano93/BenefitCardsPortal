using BenefitCardsPortal.Models;

namespace BenefitCardsPortal.Services;

public interface IBackupService
{
    Task<BackupPolicy> GetPolicyAsync(CancellationToken cancellationToken = default);
    Task SavePolicyAsync(BackupPolicy policy, CancellationToken cancellationToken = default);
    Task<string> CreateBackupAsync(CancellationToken cancellationToken = default);
    Task RunScheduledBackupIfDueAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<BackupFileViewModel> ListBackups();
    string? FindBackupPath(string fileName);
}
