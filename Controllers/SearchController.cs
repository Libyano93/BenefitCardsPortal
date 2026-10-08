using BenefitCardsPortal.Models;
using BenefitCardsPortal.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BenefitCardsPortal.Controllers;

public class SearchController(ICitizenSearchService searchService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (TempData["AfterReceiptSearch"] is bool afterReceipt && afterReceipt)
        {
            var model = new CitizenSearchViewModel
            {
                Query = TempData["ReceiptSearchQuery"] as string,
                HasSearched = true
            };
            model.Results = await searchService.SearchAsync(model.Query, cancellationToken);
            return View(model);
        }
        return View(new CitizenSearchViewModel());
    }

    [HttpPost]
    [EnableRateLimiting("CitizenSearch")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(CitizenSearchViewModel model, CancellationToken cancellationToken)
    {
        model.HasSearched = true;
        model.Query = model.Query?.Trim();
        if (string.IsNullOrWhiteSpace(model.Query))
        {
            ModelState.AddModelError(nameof(model.Query), "اكتب الاسم أو الرقم الوطني للبحث.");
            return View(model);
        }
        model.Results = await searchService.SearchAsync(model.Query, cancellationToken);
        return View(model);
    }
}
