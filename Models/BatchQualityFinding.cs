namespace BenefitCardsPortal.Models;

public sealed record BatchQualityFinding(string Code, string Message, int Count, bool IsBlocking);
