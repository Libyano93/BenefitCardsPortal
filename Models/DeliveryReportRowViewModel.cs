namespace BenefitCardsPortal.Models;

public sealed record DeliveryReportRowViewModel(
    string BeneficiaryName,
    string? SerialNumber,
    string? CardNumber,
    int Month,
    int Year,
    bool IsReceived,
    string? ReceivedBy,
    DateTime? ReceivedAtLocal);
