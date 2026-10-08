using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace BenefitCardsPortal.Security;

public sealed class EmployeeAccountGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserManager<PortalUser> userManager)
    {
        if (context.User.Identity?.IsAuthenticated == true && context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>() is { } action)
        {
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = userId is null ? null : await userManager.FindByIdAsync(userId);
            if (user is null || !user.IsActive)
            {
                await context.SignOutAsync(IdentityConstants.ApplicationScheme);
                context.Response.Redirect("/Account/Login?disabled=true");
                return;
            }

            var isPasswordChangeEndpoint = action.ControllerName == "Account" &&
                (action.ActionName == "ChangePassword" || action.ActionName == "Logout");
            if (user.MustChangePassword && !isPasswordChangeEndpoint)
            {
                context.Response.Redirect("/Account/ChangePassword");
                return;
            }
        }

        await next(context);
    }
}
