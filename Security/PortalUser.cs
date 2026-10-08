using Microsoft.AspNetCore.Identity;

namespace BenefitCardsPortal.Security;

public class PortalUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
}
