namespace BenefitCardsPortal.Models;

public sealed record EmployeeAccountRowViewModel(string Id, string DisplayName, string Email, bool IsEmployee, bool IsAdmin, bool IsCurrentUser, bool IsActive, bool MustChangePassword, IReadOnlySet<string> Permissions);
