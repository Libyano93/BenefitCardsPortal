using BenefitCardsPortal.Data;
using BenefitCardsPortal.Models;
using BenefitCardsPortal.Security;
using BenefitCardsPortal.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BenefitCardsPortal.Controllers;

[Authorize(Roles = EmployeeAccountSeeder.EmployeeRole)]
public class EmployeeController(BenefitsDbContext db, IWebHostEnvironment environment, IStatementOcrService ocrService, IConfiguration configuration) : Controller
{
    private const long MaxUploadBytes = 20 * 1024 * 1024;

    [HttpGet]
    public async Task<IActionResult> Index(int? month, int? year, string status = "all", CancellationToken ct = default)
    {
        ViewBag.DeletionEnabled = !string.IsNullOrWhiteSpace(configuration["BatchDeletion:Passcode"]);
        ViewBag.SelectedMonth = month;
        ViewBag.SelectedYear = year;
        ViewBag.SelectedStatus = status;
        ViewBag.Years = await db.BenefitBatches.Select(x => x.ValidityYear).Distinct().OrderByDescending(x => x).ToListAsync(ct);
        var query = db.BenefitBatches.Include(x => x.Records).AsQueryable();
        if (month is >= 1 and <= 12) query = query.Where(x => x.ValidityMonth == month);
        if (year is not null) query = query.Where(x => x.ValidityYear == year);
        if (status == "reviewed") query = query.Where(x => x.IsReviewed);
        else if (status == "pending") query = query.Where(x => !x.IsReviewed);
        var batches = await query.OrderByDescending(x => x.UploadedAtUtc).ToListAsync(ct);
        return View(batches);
    }

    [HttpGet]
    public async Task<IActionResult> Audit(string? actionName, string? employee, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var query = db.EmployeeAuditEvents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(actionName)) query = query.Where(x => x.Action == actionName);
        if (!string.IsNullOrWhiteSpace(employee)) query = query.Where(x => x.Actor.Contains(employee.Trim()));
        if (from is not null) query = query.Where(x => x.OccurredAtUtc >= from.Value.Date.AddHours(-2));
        if (to is not null) query = query.Where(x => x.OccurredAtUtc < to.Value.Date.AddDays(1).AddHours(-2));
        var events = await query.OrderByDescending(x => x.OccurredAtUtc).Take(500).ToListAsync(ct);
        return View(new EmployeeAuditViewModel { Events = events, Action = actionName, Employee = employee, From = from, To = to });
    }

    [HttpGet]
    [Authorize(Roles = EmployeeAccountSeeder.CardReceiveRole + "," + EmployeeAccountSeeder.AdminRole)]
    public async Task<IActionResult> ReceiptReversals(string? employee, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var query = db.EmployeeAuditEvents.AsNoTracking().Where(entry => entry.Action == "إلغاء تسجيل الاستلام");
        if (!string.IsNullOrWhiteSpace(employee)) query = query.Where(entry => entry.Actor.Contains(employee.Trim()));
        if (from is not null) query = query.Where(entry => entry.OccurredAtUtc >= from.Value.Date.AddHours(-2));
        if (to is not null) query = query.Where(entry => entry.OccurredAtUtc < to.Value.Date.AddDays(1).AddHours(-2));
        var events = await query.OrderByDescending(entry => entry.OccurredAtUtc).Take(500).ToListAsync(ct);
        return View(new EmployeeAuditViewModel { Events = events, Action = "إلغاء تسجيل الاستلام", Employee = employee, From = from, To = to });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = EmployeeAccountSeeder.BatchDeleteRole + "," + EmployeeAccountSeeder.AdminRole)]
    public async Task<IActionResult> DeleteBatch(int id, string passcode, CancellationToken ct)
    {
        var configured = configuration["BatchDeletion:Passcode"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            TempData["Error"] = "الحذف غير مفعل. أضف رمز الحذف في إعدادات التشغيل أولًا.";
            return RedirectToAction(nameof(Index));
        }
        var submittedBytes = Encoding.UTF8.GetBytes(passcode ?? string.Empty);
        var expectedBytes = Encoding.UTF8.GetBytes(configured);
        if (submittedBytes.Length != expectedBytes.Length || !CryptographicOperations.FixedTimeEquals(submittedBytes, expectedBytes))
        {
            TempData["Error"] = "رمز الحذف غير صحيح؛ لم تُحذف الدفعة.";
            return RedirectToAction(nameof(Index));
        }
        var batch = await db.BenefitBatches.FindAsync([id], ct);
        if (batch is null) return NotFound();
        LogEvent("حذف دفعة", $"حذف الدفعة {batch.ValidityMonth}/{batch.ValidityYear} والملف {batch.SourceFileName}", batch.Id);
        db.BenefitBatches.Remove(batch);
        await db.SaveChangesAsync(ct);
        var path = Path.Combine(environment.ContentRootPath, "App_Data", "Uploads", batch.StoredFilePath);
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch (IOException) { }
        TempData["Notice"] = "تم حذف الدفعة وسجلاتها بعد التحقق من الرمز.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Roles = EmployeeAccountSeeder.BatchUploadRole + "," + EmployeeAccountSeeder.AdminRole)]
    public IActionResult Upload() => View(new BatchUploadViewModel { Month = DateTime.Now.Month, Year = DateTime.Now.Year });

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(MaxUploadBytes)]
    [Authorize(Roles = EmployeeAccountSeeder.BatchUploadRole + "," + EmployeeAccountSeeder.AdminRole)]
    public async Task<IActionResult> PreviewUpload(BatchUploadViewModel model, CancellationToken ct)
    {
        if (model.File is null || model.File.Length == 0) ModelState.AddModelError(nameof(model.File), "اختر ملف الكشف.");
        if (model.File?.Length > MaxUploadBytes) ModelState.AddModelError(nameof(model.File), "حجم الملف يتجاوز 20 ميغابايت.");
        if (!ModelState.IsValid) return View(model);

        var (extension, _) = await DetectFileTypeAsync(model.File!, ct);
        if (extension is null)
        {
            ModelState.AddModelError(nameof(model.File), "الملف غير مدعوم. ارفع PDF أو صورة PNG/JPG سليمة.");
            return View(model);
        }

        CleanupExpiredDrafts();
        var uploadDirectory = Path.Combine(environment.ContentRootPath, "App_Data", "Uploads");
        var draftsDirectory = GetDraftsDirectory();
        Directory.CreateDirectory(uploadDirectory);
        Directory.CreateDirectory(draftsDirectory);
        var draftId = Guid.NewGuid().ToString("N");
        var storageName = $"draft-{draftId}{extension}";
        var storagePath = Path.Combine(uploadDirectory, storageName);
        await using (var stream = System.IO.File.Create(storagePath))
            await model.File!.CopyToAsync(stream, ct);

        IReadOnlyList<ExtractedCardRow> rows = [];
        string? ocrNotice = null;
        try
        {
            rows = await ocrService.ExtractAsync(storageName, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ocrNotice = "لم تُقرأ البيانات تلقائيًا. سيبقى الكشف محفوظًا مؤقتًا؛ يمكنك إدخال الصفوف التي تريدها يدويًا أو إلغاء المعاينة.";
        }

        var draft = new UploadDraft(draftId, model.Month, model.Year, Path.GetFileName(model.File!.FileName), storageName, User.Identity?.Name ?? string.Empty, DateTime.UtcNow);
        await System.IO.File.WriteAllTextAsync(GetDraftMetadataPath(draftId), JsonSerializer.Serialize(draft), ct);
        model.DraftId = draftId;
        model.SourceFileName = draft.SourceFileName;
        model.File = null;
        model.OcrNotice = ocrNotice;
        model.ReviewFindings = BatchQualityChecker.Analyze(rows.Select(row => (row.BeneficiaryName, row.SerialNumber, row.CardNumber, row.NationalNumber)));
        model.HasDuplicatePeriod = await db.BenefitBatches.AnyAsync(batch => batch.ValidityMonth == model.Month && batch.ValidityYear == model.Year, ct);
        model.Rows = rows.Select(row => new AddCardViewModel
        {
            SourceRowNumber = row.SourceRowNumber,
            SerialNumber = row.SerialNumber,
            BeneficiaryName = row.BeneficiaryName,
            CardName = row.CardName,
            NationalNumber = row.NationalNumber,
            CardNumber = row.CardNumber,
            Notes = row.Notes
        }).ToList();
        ModelState.Clear();
        return View("Upload", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = EmployeeAccountSeeder.BatchUploadRole + "," + EmployeeAccountSeeder.AdminRole)]
    public async Task<IActionResult> CommitUpload(BatchUploadViewModel model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.DraftId) || !Guid.TryParseExact(model.DraftId, "N", out _)) return BadRequest();
        if (model.Rows.Count is 0 or > 5000)
        {
            ModelState.AddModelError(string.Empty, "أضف صفًا واحدًا على الأقل، والحد الأقصى 5000 سجل للدفعة.");
            return await ReturnUploadPreviewAsync(model, ct);
        }
        if (!ModelState.IsValid) return await ReturnUploadPreviewAsync(model, ct);

        var draft = await ReadDraftAsync(model.DraftId, ct);
        if (draft is null || !string.Equals(draft.Owner, User.Identity?.Name, StringComparison.Ordinal)) return NotFound();
        if (DateTime.UtcNow - draft.CreatedAtUtc > TimeSpan.FromHours(24))
        {
            DeleteDraftFiles(draft);
            TempData["Error"] = "انتهت صلاحية معاينة هذا الكشف؛ ارفعه من جديد.";
            return RedirectToAction(nameof(Upload));
        }
        var sourcePath = Path.Combine(environment.ContentRootPath, "App_Data", "Uploads", draft.StoredFilePath);
        if (!System.IO.File.Exists(sourcePath)) return NotFound();
        model.HasDuplicatePeriod = await db.BenefitBatches.AnyAsync(batch => batch.ValidityMonth == draft.Month && batch.ValidityYear == draft.Year, ct);
        if (model.HasDuplicatePeriod && !model.ConfirmDuplicatePeriod)
        {
            ModelState.AddModelError(nameof(model.ConfirmDuplicatePeriod), "أكد المتابعة بعد مراجعة تنبيه وجود كشف سابق لهذا الشهر والسنة.");
            return await ReturnUploadPreviewAsync(model, ct);
        }

        var batch = new BenefitBatch
        {
            ValidityMonth = draft.Month,
            ValidityYear = draft.Year,
            SourceFileName = draft.SourceFileName,
            StoredFilePath = draft.StoredFilePath,
            UploadedAtUtc = DateTime.UtcNow
        };
        foreach (var row in model.Rows)
            db.BenefitCardRecords.Add(new BenefitCardRecord
            {
                BenefitBatch = batch,
                SourceRowNumber = CleanOptional(row.SourceRowNumber),
                BeneficiaryName = row.BeneficiaryName.Trim(),
                CardName = CleanOptional(row.CardName),
                NationalNumber = CleanOptional(row.NationalNumber),
                CardNumber = CleanOptional(row.CardNumber),
                SerialNumber = CleanOptional(row.SerialNumber),
                Notes = CleanOptional(row.Notes),
                IsReceived = false
            });
        db.BenefitBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        LogEvent("رفع دفعة", $"رفع الدفعة {batch.ValidityMonth}/{batch.ValidityYear} وبها {model.Rows.Count} سجلًا؛ الملف {batch.SourceFileName}", batch.Id);
        await db.SaveChangesAsync(ct);
        try { System.IO.File.Delete(GetDraftMetadataPath(draft.DraftId)); } catch (IOException) { }
        TempData["Notice"] = $"تم رفع دفعة {draft.Month}/{draft.Year} بعد مراجعة {model.Rows.Count} سجلًا.";
        return RedirectToAction(nameof(Details), new { id = batch.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = EmployeeAccountSeeder.BatchUploadRole + "," + EmployeeAccountSeeder.AdminRole)]
    public async Task<IActionResult> CancelDraft(string draftId, CancellationToken ct)
    {
        var draft = await ReadDraftAsync(draftId, ct);
        if (draft is not null && string.Equals(draft.Owner, User.Identity?.Name, StringComparison.Ordinal)) DeleteDraftFiles(draft);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Roles = EmployeeAccountSeeder.BatchUploadRole + "," + EmployeeAccountSeeder.AdminRole)]
    public async Task<IActionResult> PreviewDraftFile(string draftId, CancellationToken ct)
    {
        var draft = await ReadDraftAsync(draftId, ct);
        if (draft is null || !string.Equals(draft.Owner, User.Identity?.Name, StringComparison.Ordinal)) return NotFound();
        var path = Path.Combine(environment.ContentRootPath, "App_Data", "Uploads", draft.StoredFilePath);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, ContentTypeFor(path), enableRangeProcessing: true);
    }

    private async Task<IActionResult> ReturnUploadPreviewAsync(BatchUploadViewModel model, CancellationToken ct)
    {
        var draft = await ReadDraftAsync(model.DraftId!, ct);
        if (draft is null || !string.Equals(draft.Owner, User.Identity?.Name, StringComparison.Ordinal)) return NotFound();
        model.SourceFileName = draft.SourceFileName;
        model.File = null;
        model.ReviewFindings = BatchQualityChecker.Analyze(model.Rows);
        model.HasDuplicatePeriod = await db.BenefitBatches.AnyAsync(batch => batch.ValidityMonth == draft.Month && batch.ValidityYear == draft.Year, ct);
        ModelState.Remove(nameof(model.File));
        return View("Upload", model);
    }

    private string GetDraftsDirectory() => Path.Combine(environment.ContentRootPath, "App_Data", "UploadDrafts");
    private string GetDraftMetadataPath(string draftId) => Path.Combine(GetDraftsDirectory(), $"{draftId}.json");

    private async Task<UploadDraft?> ReadDraftAsync(string? draftId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(draftId) || !Guid.TryParseExact(draftId, "N", out _)) return null;
        var path = GetDraftMetadataPath(draftId);
        if (!System.IO.File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<UploadDraft>(await System.IO.File.ReadAllTextAsync(path, ct)); }
        catch (JsonException) { return null; }
    }

    private void DeleteDraftFiles(UploadDraft draft)
    {
        var source = Path.Combine(environment.ContentRootPath, "App_Data", "Uploads", draft.StoredFilePath);
        try { if (System.IO.File.Exists(source)) System.IO.File.Delete(source); } catch (IOException) { }
        try { System.IO.File.Delete(GetDraftMetadataPath(draft.DraftId)); } catch (IOException) { }
    }

    private void CleanupExpiredDrafts()
    {
        var directory = GetDraftsDirectory();
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var draft = JsonSerializer.Deserialize<UploadDraft>(System.IO.File.ReadAllText(path));
                if (draft is not null && DateTime.UtcNow - draft.CreatedAtUtc > TimeSpan.FromHours(24)) DeleteDraftFiles(draft);
            }
            catch (Exception ex) when (ex is IOException or JsonException) { }
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, string? name, string status = "all", CancellationToken ct = default)
    {
        var batch = await db.BenefitBatches.Include(x => x.Records).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (batch is null) return NotFound();
        var filtered = batch.Records.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(name)) filtered = filtered.Where(x => x.BeneficiaryName.Contains(name.Trim(), StringComparison.CurrentCultureIgnoreCase));
        if (status == "received") filtered = filtered.Where(x => x.IsReceived);
        else if (status == "pending") filtered = filtered.Where(x => !x.IsReceived);
        ViewBag.SearchName = name;
        ViewBag.SearchStatus = status;
        ViewBag.CanEditCards = User.IsInRole(EmployeeAccountSeeder.BatchEditRole) || User.IsInRole(EmployeeAccountSeeder.AdminRole);
        ViewBag.CanReceiveCards = User.IsInRole(EmployeeAccountSeeder.CardReceiveRole) || User.IsInRole(EmployeeAccountSeeder.AdminRole);
        ViewBag.CanApproveBatch = User.IsInRole(EmployeeAccountSeeder.BatchApproveRole) || User.IsInRole(EmployeeAccountSeeder.AdminRole);
        return View(new EmployeeBatchDetailsViewModel { Batch = batch, Records = filtered.OrderBy(x => x.Id).ToList(), Findings = BatchQualityChecker.Analyze(batch.Records), Name = name, Status = status });
    }

    [HttpGet]
    [Authorize(Roles = EmployeeAccountSeeder.CardReceiveRole + "," + EmployeeAccountSeeder.AdminRole)]
    public async Task<IActionResult> Receipt(int id, CancellationToken ct)
    {
        var record = await db.BenefitCardRecords.AsNoTracking().Include(item => item.BenefitBatch)
            .FirstOrDefaultAsync(item => item.Id == id && item.IsReceived, ct);
        if (record?.BenefitBatch is null) return NotFound();
        return View(new ReceiptViewModel
        {
            RecordId = record.Id,
            BeneficiaryName = record.BeneficiaryName,
            CardName = record.CardName,
            SerialNumber = record.SerialNumber,
            SourceRowNumber = record.SourceRowNumber,
            CardNumber = record.CardNumber,
            Month = record.BenefitBatch.ValidityMonth,
            Year = record.BenefitBatch.ValidityYear,
            ReceivedAtLocal = (record.ReceivedAtUtc ?? DateTime.UtcNow).AddHours(2),
            ReceivedBy = record.ReceivedBy ?? "غير محدد"
        });
    }

    [HttpGet]
    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var batch = await db.BenefitBatches.FindAsync([id], ct);
        if (batch is null) return NotFound();
        var path = Path.Combine(environment.ContentRootPath, "App_Data", "Uploads", batch.StoredFilePath);
        if (!System.IO.File.Exists(path)) return NotFound();
        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf", ".png" => "image/png", _ => "image/jpeg"
        };
        return PhysicalFile(path, contentType, batch.SourceFileName, enableRangeProcessing: true);
    }

    [HttpGet]
    public async Task<IActionResult> Preview(int id, CancellationToken ct)
    {
        var batch = await db.BenefitBatches.FindAsync([id], ct);
        if (batch is null) return NotFound();
        return View(batch);
    }

    [HttpGet]
    public async Task<IActionResult> PreviewFile(int id, CancellationToken ct)
    {
        var batch = await db.BenefitBatches.FindAsync([id], ct);
        if (batch is null) return NotFound();
        var path = Path.Combine(environment.ContentRootPath, "App_Data", "Uploads", batch.StoredFilePath);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, ContentTypeFor(path), enableRangeProcessing: true);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = EmployeeAccountSeeder.BatchEditRole + "," + EmployeeAccountSeeder.AdminRole)]
    public async Task<IActionResult> AddCard(AddCardViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return RedirectToAction(nameof(Details), new { id = model.BatchId });
        var batch = await db.BenefitBatches.FindAsync([model.BatchId], ct);
        if (batch is null) return NotFound();
        db.BenefitCardRecords.Add(new BenefitCardRecord
        {
            BenefitBatchId = batch.Id,
            SourceRowNumber = CleanOptional(model.SourceRowNumber),
            BeneficiaryName = model.BeneficiaryName.Trim(),
            CardName = CleanOptional(model.CardName),
            NationalNumber = CleanOptional(model.NationalNumber),
            CardNumber = CleanOptional(model.CardNumber),
            SerialNumber = CleanOptional(model.SerialNumber),
            Notes = CleanOptional(model.Notes),
            IsReceived = false
        });
        await db.SaveChangesAsync(ct);
        LogEvent("إضافة سجل", $"إضافة سجل جديد إلى الدفعة {batch.ValidityMonth}/{batch.ValidityYear}", batch.Id);
        await db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Details), new { id = batch.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = EmployeeAccountSeeder.BatchEditRole + "," + EmployeeAccountSeeder.AdminRole)]
    public async Task<IActionResult> UpdateCard(int id, AddCardViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return RedirectToAction(nameof(Details), new { id = model.BatchId });
        var record = await db.BenefitCardRecords.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (record is null) return NotFound();
        record.SourceRowNumber = CleanOptional(model.SourceRowNumber);
        record.BeneficiaryName = model.BeneficiaryName.Trim();
        record.CardName = CleanOptional(model.CardName);
        record.NationalNumber = CleanOptional(model.NationalNumber);
        record.CardNumber = CleanOptional(model.CardNumber);
        record.SerialNumber = CleanOptional(model.SerialNumber);
        record.Notes = CleanOptional(model.Notes);
        LogEvent("تعديل سجل", $"تعديل بيانات سجل الدفعة {record.BenefitBatchId}", record.BenefitBatchId, record.Id);
        await db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Details), new { id = record.BenefitBatchId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = EmployeeAccountSeeder.CardReceiveRole + "," + EmployeeAccountSeeder.AdminRole)]
    public async Task<IActionResult> MarkReceived(int id, bool returnToSearch, string? searchQuery, CancellationToken ct)
    {
        var record = await db.BenefitCardRecords.Include(x => x.BenefitBatch).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (record is null) return NotFound();
        if (record.BenefitBatch is null || !record.BenefitBatch.IsReviewed)
        {
            TempData["Error"] = "اعتمد الكشف أولًا قبل تسجيل استلام أي بطاقة.";
            return ReceiptRedirect(record.BenefitBatchId, returnToSearch, searchQuery);
        }
        if (record.IsReceived)
        {
            TempData["Notice"] = "سبق تسجيل استلام هذه البطاقة.";
            return ReceiptRedirect(record.BenefitBatchId, returnToSearch, searchQuery);
        }
        record.IsReceived = true;
        record.ReceivedAtUtc = DateTime.UtcNow;
        record.ReceivedBy = User.Identity?.Name;
        LogEvent("تسجيل استلام", $"تسجيل استلام بطاقة للمستفيد {record.BeneficiaryName}", record.BenefitBatchId, record.Id);
        await db.SaveChangesAsync(ct);
        TempData["Notice"] = "تم تسجيل استلام البطاقة.";
        return ReceiptRedirect(record.BenefitBatchId, returnToSearch, searchQuery);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = EmployeeAccountSeeder.CardReceiveRole + "," + EmployeeAccountSeeder.AdminRole)]
    public async Task<IActionResult> UndoReceipt(int id, bool returnToSearch, string? searchQuery, CancellationToken ct)
    {
        var record = await db.BenefitCardRecords.Include(x => x.BenefitBatch).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (record is null) return NotFound();
        if (!record.IsReceived)
        {
            TempData["Notice"] = "هذه البطاقة مسجلة بالفعل على أنها لم تُستلم.";
            return ReceiptRedirect(record.BenefitBatchId, returnToSearch, searchQuery);
        }

        var previousReceiver = string.IsNullOrWhiteSpace(record.ReceivedBy) ? "غير محدد" : record.ReceivedBy;
        var previousTime = record.ReceivedAtUtc?.ToString("yyyy-MM-dd HH:mm 'UTC'") ?? "غير محدد";
        record.IsReceived = false;
        record.ReceivedAtUtc = null;
        record.ReceivedBy = null;
        LogEvent("إلغاء تسجيل الاستلام", $"إلغاء استلام بطاقة المستفيد {record.BeneficiaryName}؛ كان مسجلًا بواسطة {previousReceiver} في {previousTime}.", record.BenefitBatchId, record.Id);
        await db.SaveChangesAsync(ct);
        TempData["Notice"] = "تم إلغاء تسجيل الاستلام وإعادة البطاقة إلى حالة لم يستلم.";
        return ReceiptRedirect(record.BenefitBatchId, returnToSearch, searchQuery);
    }

    private IActionResult ReceiptRedirect(int batchId, bool returnToSearch, string? searchQuery)
    {
        if (!returnToSearch) return RedirectToAction(nameof(Details), new { id = batchId });
        TempData["AfterReceiptSearch"] = true;
        TempData["ReceiptSearchQuery"] = searchQuery ?? string.Empty;
        return RedirectToAction("Index", "Search");
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Roles = EmployeeAccountSeeder.BatchApproveRole + "," + EmployeeAccountSeeder.AdminRole)]
    public async Task<IActionResult> Approve(int id, CancellationToken ct)
    {
        var batch = await db.BenefitBatches.Include(x => x.Records).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (batch is null) return NotFound();
        if (batch.Records.Count == 0)
        {
            TempData["Error"] = "أضف سجلات الكشف قبل اعتماده.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var blockingFindings = BatchQualityChecker.Analyze(batch.Records).Where(finding => finding.IsBlocking).ToArray();
        if (blockingFindings.Length > 0)
        {
            TempData["Error"] = "لا يمكن اعتماد الكشف قبل معالجة النواقص والتكرارات: " + string.Join("، ", blockingFindings.Select(finding => $"{finding.Message} ({finding.Count})"));
            return RedirectToAction(nameof(Details), new { id });
        }
        batch.IsReviewed = true;
        LogEvent("اعتماد دفعة", $"اعتماد الدفعة {batch.ValidityMonth}/{batch.ValidityYear} بعد التحقق من {batch.Records.Count} سجلًا", batch.Id);
        await db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Details), new { id });
    }

    private static string? CleanOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void LogEvent(string action, string summary, int? batchId = null, int? recordId = null) =>
        db.EmployeeAuditEvents.Add(new EmployeeAuditEvent { Actor = User.Identity?.Name ?? "موظف غير معروف", Action = action, Summary = summary, BatchId = batchId, RecordId = recordId, OccurredAtUtc = DateTime.UtcNow });

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf", ".png" => "image/png", _ => "image/jpeg"
    };

    private sealed record UploadDraft(string DraftId, int Month, int Year, string SourceFileName, string StoredFilePath, string Owner, DateTime CreatedAtUtc);

    private static async Task<(string? Extension, string ContentType)> DetectFileTypeAsync(IFormFile file, CancellationToken ct)
    {
        var header = new byte[8];
        await using var stream = file.OpenReadStream();
        var count = await stream.ReadAsync(header, ct);
        if (count >= 5 && header[0] == '%' && header[1] == 'P' && header[2] == 'D' && header[3] == 'F' && header[4] == '-') return (".pdf", "application/pdf");
        if (count >= 8 && header.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return (".png", "image/png");
        if (count >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff) return (".jpg", "image/jpeg");
        return (null, string.Empty);
    }
}
