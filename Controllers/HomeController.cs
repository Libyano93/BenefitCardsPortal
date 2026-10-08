using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using BenefitCardsPortal.Models;
using BenefitCardsPortal.Data;
using Microsoft.EntityFrameworkCore;

namespace BenefitCardsPortal.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly BenefitsDbContext _db;

    public HomeController(ILogger<HomeController> logger, BenefitsDbContext db)
    {
        _logger = logger;
        _db = db;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var records = _db.BenefitCardRecords.Where(x => x.BenefitBatch != null && x.BenefitBatch.IsReviewed);
        var total = await records.CountAsync(ct);
        var received = await records.CountAsync(x => x.IsReceived, ct);
        var batches = await _db.BenefitBatches.CountAsync(x => x.IsReviewed, ct);
        return View(new HomeDashboardViewModel(batches, total, received, total - received));
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
