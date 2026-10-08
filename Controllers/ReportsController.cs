using System.Globalization;
using System.Text;
using BenefitCardsPortal.Data;
using BenefitCardsPortal.Models;
using BenefitCardsPortal.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BenefitCardsPortal.Controllers;

[Authorize(Roles = EmployeeAccountSeeder.ReportsViewRole + "," + EmployeeAccountSeeder.AdminRole)]
public sealed class ReportsController(BenefitsDbContext db) : Controller
{
    private const int MaximumRows = 5000;
    private const int PageSize = 50;

    [HttpGet]
    public async Task<IActionResult> Index(int? month, int? year, string status = "all", int page = 1, CancellationToken ct = default)
    {
        var report = await BuildReportAsync(month, year, status, page, paginate: true, ct);
        await LogReportAccessAsync("عرض تقرير التسليمات", report, ct);
        return View(report);
    }

    [HttpGet]
    public async Task<IActionResult> Print(int? month, int? year, string status = "all", CancellationToken ct = default)
    {
        var report = await BuildReportAsync(month, year, status, 1, paginate: false, ct);
        await LogReportAccessAsync("طباعة تقرير التسليمات", report, ct);
        return View("Print", report);
    }

    [HttpGet]
    public async Task<IActionResult> DownloadCsv(int? month, int? year, string status = "all", CancellationToken ct = default)
    {
        var report = await BuildReportAsync(month, year, status, 1, paginate: false, ct);
        await LogReportAccessAsync("تصدير تقرير التسليمات", report, ct);
        var csv = new StringBuilder();
        csv.AppendLine("الاسم,رقم التسلسل,جزء من رقم البطاقة,الشهر,السنة,الحالة,الموظف,وقت التسليم");
        foreach (var row in report.Rows)
            csv.AppendLine(string.Join(',', new[]
            {
                Csv(row.BeneficiaryName), Csv(row.SerialNumber), Csv(row.CardNumber),
                Csv(row.Month.ToString(CultureInfo.InvariantCulture)), Csv(row.Year.ToString(CultureInfo.InvariantCulture)),
                Csv(row.IsReceived ? "تم الاستلام" : "لم يستلم"), Csv(row.ReceivedBy),
                Csv(row.ReceivedAtLocal?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
            }));

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"تقرير-التسليمات-{DateTime.Now:yyyyMMdd-HHmm}.csv");
    }

    private async Task<DeliveryReportViewModel> BuildReportAsync(int? month, int? year, string? status, int page, bool paginate, CancellationToken ct)
    {
        month = month is >= 1 and <= 12 ? month : null;
        status = status is "received" or "pending" ? status : "all";
        page = Math.Max(1, page);

        var batches = db.BenefitBatches.AsNoTracking().Where(batch => batch.IsReviewed);
        if (month is not null) batches = batches.Where(batch => batch.ValidityMonth == month);
        if (year is not null) batches = batches.Where(batch => batch.ValidityYear == year);

        var batchIds = await batches.Select(batch => batch.Id).ToListAsync(ct);
        var years = await db.BenefitBatches.AsNoTracking().Where(batch => batch.IsReviewed)
            .Select(batch => batch.ValidityYear).Distinct().OrderByDescending(value => value).ToListAsync(ct);
        var records = db.BenefitCardRecords.AsNoTracking().Where(record => batchIds.Contains(record.BenefitBatchId));
        var total = await records.CountAsync(ct);
        var received = await records.CountAsync(record => record.IsReceived, ct);
        var reversals = batchIds.Count == 0 ? 0 : await db.EmployeeAuditEvents.AsNoTracking()
            .CountAsync(entry => entry.Action == "إلغاء تسجيل الاستلام" && entry.BatchId != null && batchIds.Contains(entry.BatchId.Value), ct);

        var filtered = status == "received" ? records.Where(record => record.IsReceived)
            : status == "pending" ? records.Where(record => !record.IsReceived) : records;
        var matchingCount = await filtered.CountAsync(ct);
        var pageCount = (int)Math.Ceiling(Math.Min(matchingCount, MaximumRows) / (double)PageSize);
        page = Math.Min(page, Math.Max(1, pageCount));
        var rowQuery = filtered.OrderBy(record => record.BeneficiaryName).ThenBy(record => record.Id).Take(MaximumRows);
        if (paginate) rowQuery = rowQuery.Skip((page - 1) * PageSize).Take(PageSize);
        var rows = await rowQuery
            .Select(record => new
            {
                record.BeneficiaryName,
                record.SerialNumber,
                record.CardNumber,
                Month = record.BenefitBatch!.ValidityMonth,
                Year = record.BenefitBatch.ValidityYear,
                record.IsReceived,
                record.ReceivedBy,
                record.ReceivedAtUtc
            }).ToListAsync(ct);

        return new DeliveryReportViewModel
        {
            Month = month,
            Year = year,
            Status = status,
            TotalCount = total,
            ReceivedCount = received,
            PendingCount = total - received,
            ReversalCount = reversals,
            MatchingCount = matchingCount,
            Page = paginate ? page : 1,
            PageSize = paginate ? PageSize : MaximumRows,
            Years = years,
            Rows = rows.Select(row => new DeliveryReportRowViewModel(
                row.BeneficiaryName, row.SerialNumber, row.CardNumber, row.Month, row.Year, row.IsReceived,
                row.ReceivedBy, row.ReceivedAtUtc is null ? null : DateTime.SpecifyKind(row.ReceivedAtUtc.Value, DateTimeKind.Utc).AddHours(2))).ToArray()
        };
    }

    private async Task LogReportAccessAsync(string action, DeliveryReportViewModel report, CancellationToken ct)
    {
        db.EmployeeAuditEvents.Add(new EmployeeAuditEvent
        {
            Actor = User.Identity?.Name ?? "موظف غير معروف",
            Action = action,
            Summary = $"الفترة {(report.Month?.ToString() ?? "كل الأشهر")}/{(report.Year?.ToString() ?? "كل السنوات")}; الحالة {report.Status}; عدد السجلات المطابقة {report.MatchingCount}."
        });
        await db.SaveChangesAsync(ct);
    }

    private static string Csv(string? value)
    {
        var safe = value ?? string.Empty;
        if (safe.Length > 0 && safe[0] is '=' or '+' or '-' or '@' or '\t' or '\r') safe = "'" + safe;
        return "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
