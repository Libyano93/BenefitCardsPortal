using BenefitCardsPortal.Models;
using BenefitCardsPortal.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BenefitCardsPortal.Controllers;

public class AccountController(SignInManager<PortalUser> signInManager) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null, bool disabled = false)
    {
        if (disabled) ModelState.AddModelError(string.Empty, "هذا الحساب معطل. راجع مسؤول النظام.");
        return View(new EmployeeLoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(EmployeeLoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var result = await signInManager.PasswordSignInAsync(model.Email, model.Password, false, true);
        if (result.Succeeded)
        {
            var user = await signInManager.UserManager.FindByEmailAsync(model.Email);
            if (user is not null && !user.IsActive)
            {
                await signInManager.SignOutAsync();
                ModelState.AddModelError(string.Empty, "هذا الحساب معطل. راجع مسؤول النظام.");
                return View(model);
            }
            if (user is not null && await signInManager.UserManager.IsInRoleAsync(user, EmployeeAccountSeeder.EmployeeRole))
            {
                if (user.MustChangePassword) return RedirectToAction(nameof(ChangePassword));
                return LocalRedirect(Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl! : Url.Action("Index", "Employee")!);
            }
            await signInManager.SignOutAsync();
        }
        ModelState.AddModelError(string.Empty, "بيانات الدخول غير صحيحة أو لا يملك الحساب صلاحية الموظف.");
        return View(model);
    }

    [Authorize, HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var user = await signInManager.UserManager.GetUserAsync(User);
        if (user is null || !user.IsActive) return Challenge();
        var change = await signInManager.UserManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!change.Succeeded)
        {
            foreach (var error in change.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }
        user.MustChangePassword = false;
        var update = await signInManager.UserManager.UpdateAsync(user);
        if (!update.Succeeded)
        {
            foreach (var error in update.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }
        await signInManager.RefreshSignInAsync(user);
        TempData["Notice"] = "تم تغيير كلمة المرور بنجاح.";
        return RedirectToAction("Index", "Employee");
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }
}
