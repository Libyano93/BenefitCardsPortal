using BenefitCardsPortal.Data;
using BenefitCardsPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace BenefitCardsPortal.Services;

public sealed class AuditPolicyStore(BenefitsDbContext db) : IAuditPolicyStore
{
    private const string EnabledKey = "AuditCleanupEnabled";
    private const string AmountKey = "AuditRetentionAmount";
    private const string UnitKey = "AuditRetentionUnit";
    private const string EmailKey = "AuditNotificationEmail";

    public async Task<AuditPolicy> GetAsync(CancellationToken cancellationToken = default)
    {
        var values = await db.PortalSettings.AsNoTracking().ToDictionaryAsync(setting => setting.Key, setting => setting.Value, cancellationToken);
        return new AuditPolicy(
            !values.TryGetValue(EnabledKey, out var enabled) || !bool.TryParse(enabled, out var isEnabled) || isEnabled,
            values.TryGetValue(AmountKey, out var amount) && int.TryParse(amount, out var parsedAmount) ? Math.Clamp(parsedAmount, 1, 87600) : 60,
            values.TryGetValue(UnitKey, out var unit) && unit == "hours" ? "hours" : "days",
            values.TryGetValue(EmailKey, out var email) && !string.IsNullOrWhiteSpace(email) ? email : "sutest410@gmail.com");
    }

    public async Task SaveAsync(AuditPolicy policy, CancellationToken cancellationToken = default)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [EnabledKey] = policy.CleanupEnabled.ToString(),
            [AmountKey] = policy.RetentionAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [UnitKey] = policy.RetentionUnit,
            [EmailKey] = policy.NotificationEmail.Trim()
        };
        foreach (var (key, value) in values)
        {
            var setting = await db.PortalSettings.FindAsync([key], cancellationToken);
            if (setting is null) db.PortalSettings.Add(new PortalSetting { Key = key, Value = value });
            else setting.Value = value;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
