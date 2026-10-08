using System.Globalization;
using BenefitCardsPortal.Data;
using BenefitCardsPortal.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace BenefitCardsPortal.Services;

public sealed class SmtpSettingsStore(BenefitsDbContext db, IDataProtectionProvider protectionProvider, IConfiguration configuration) : ISmtpSettingsStore
{
    private const string Prefix = "SmtpSetting:";
    private readonly IDataProtector _protector = protectionProvider.CreateProtector("BenefitCardsPortal.SmtpPassword.v1");
    public bool HasEnvironmentOverrides => new[] { "Smtp__Host", "Smtp__Port", "Smtp__EnableSsl", "Smtp__FromAddress", "Smtp__UserName", "Smtp__Password" }.Any(key => Environment.GetEnvironmentVariable(key) is not null);

    public async Task<SmtpMailSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var saved = await db.PortalSettings.AsNoTracking().Where(setting => setting.Key.StartsWith(Prefix))
            .ToDictionaryAsync(setting => setting.Key[Prefix.Length..], setting => setting.Value, cancellationToken);
        string Read(string key, string fallback) => saved.TryGetValue(key, out var value) ? value :
            Environment.GetEnvironmentVariable($"Smtp__{key}") ?? configuration[$"Smtp:{key}"] ?? fallback;
        var password = saved.TryGetValue("Password", out var protectedPassword)
            ? (string.IsNullOrWhiteSpace(protectedPassword) ? string.Empty : _protector.Unprotect(protectedPassword))
            : Environment.GetEnvironmentVariable("Smtp__Password") ?? configuration["Smtp:Password"] ?? string.Empty;
        return new SmtpMailSettings(
            Read("Host", string.Empty),
            int.TryParse(Read("Port", "587"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ? Math.Clamp(port, 1, 65535) : 587,
            bool.TryParse(Read("EnableSsl", "true"), out var ssl) && ssl,
            Read("FromAddress", string.Empty),
            Read("UserName", string.Empty),
            password);
    }

    public async Task<bool> HasSavedPasswordAsync(CancellationToken cancellationToken = default)
    {
        var setting = await db.PortalSettings.AsNoTracking().FirstOrDefaultAsync(value => value.Key == Prefix + "Password", cancellationToken);
        return !string.IsNullOrWhiteSpace(setting?.Value);
    }

    public async Task SaveAsync(EmailSettingsViewModel model, CancellationToken cancellationToken = default)
    {
        await SaveValueAsync("Host", model.Host.Trim(), cancellationToken);
        await SaveValueAsync("Port", model.Port.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await SaveValueAsync("EnableSsl", model.EnableSsl.ToString(), cancellationToken);
        await SaveValueAsync("FromAddress", model.FromAddress.Trim(), cancellationToken);
        await SaveValueAsync("UserName", model.UserName.Trim(), cancellationToken);
        if (model.ClearPassword) await SaveValueAsync("Password", string.Empty, cancellationToken);
        else if (!string.IsNullOrWhiteSpace(model.Password)) await SaveValueAsync("Password", _protector.Protect(model.Password), cancellationToken);
    }

    private async Task SaveValueAsync(string key, string value, CancellationToken cancellationToken)
    {
        var fullKey = Prefix + key;
        var setting = await db.PortalSettings.FindAsync([fullKey], cancellationToken);
        if (setting is null) db.PortalSettings.Add(new PortalSetting { Key = fullKey, Value = value });
        else setting.Value = value;
        await db.SaveChangesAsync(cancellationToken);
    }
}
