namespace BenefitCardsPortal.Services;

public interface ISmtpEmailSender
{
    Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default);
}
