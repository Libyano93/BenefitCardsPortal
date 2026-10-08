namespace BenefitCardsPortal.Models;

public sealed record SmtpMailSettings(string Host, int Port, bool EnableSsl, string FromAddress, string UserName, string Password)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress) &&
        (string.IsNullOrWhiteSpace(UserName) || !string.IsNullOrWhiteSpace(Password));
}
