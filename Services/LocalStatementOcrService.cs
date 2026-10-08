using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Tesseract;

namespace BenefitCardsPortal.Services;

public sealed class LocalStatementOcrService(IWebHostEnvironment environment) : IStatementOcrService
{
    private const int RenderWidth = 1800;

    public Task<IReadOnlyList<ExtractedCardRow>> ExtractAsync(string storedFilePath, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<ExtractedCardRow>>(() => Extract(storedFilePath, cancellationToken), cancellationToken);

    private IReadOnlyList<ExtractedCardRow> Extract(string storedFilePath, CancellationToken cancellationToken)
    {
        var source = Path.Combine(environment.ContentRootPath, "App_Data", "Uploads", storedFilePath);
        var work = Path.Combine(environment.ContentRootPath, "App_Data", "OcrWork", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var images = RenderPages(source, work, cancellationToken);
            var tessData = Path.Combine(environment.ContentRootPath, "tessdata");
            using var engine = new TesseractEngine(tessData, "ara+eng", EngineMode.LstmOnly);
            var extracted = new List<ExtractedCardRow>();

            foreach (var imagePath in images)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var pix = Pix.LoadFromFile(imagePath);
                var bands = FindTableRowBands(imagePath);
                var height = GetImageHeight(imagePath);
                var columns = FindTableColumns(imagePath);
                var sparseRows = ReadRows(engine, pix, PageSegMode.SparseText, bands, height, columns);
                var merged = sparseRows.ToArray();
                if (sparseRows.Count == 0 || sparseRows.Any(row => row.BeneficiaryName == "مراجعة الاسم" || string.IsNullOrWhiteSpace(row.CardName)))
                {
                    var blockRows = ReadRows(engine, pix, PageSegMode.SingleBlock, bands, height, columns);
                    merged = MergeRows(sparseRows, blockRows).ToArray();
                }
                if (bands.Count > 0) merged = RescanMissingNumericFields(engine, imagePath, work, bands, columns, merged);
                for (var i = 0; i < merged.Length; i++)
                    if (string.IsNullOrWhiteSpace(merged[i].SourceRowNumber))
                        merged[i] = merged[i] with { SourceRowNumber = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) };
                extracted.AddRange(merged);
            }

            return extracted;
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }

    private static List<string> RenderPages(string source, string work, CancellationToken ct)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension != ".pdf")
        {
            var output = Path.Combine(work, "image.png");
            NormalizeImage(source, output);
            return [output];
        }

        using var document = DocLib.Instance.GetDocReader(source, new PageDimensions(RenderWidth, 2400));
        if (document.GetPageCount() > 60) throw new InvalidDataException("الكشف يتجاوز الحد المسموح وهو 60 صفحة.");
        var pages = new List<string>();
        for (var i = 0; i < document.GetPageCount(); i++)
        {
            ct.ThrowIfCancellationRequested();
            using var page = document.GetPageReader(i);
            var width = page.GetPageWidth();
            var height = page.GetPageHeight();
            var pixels = page.GetImage();
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(pixels, 0, data.Scan0, Math.Min(pixels.Length, Math.Abs(data.Stride) * height)); }
            finally { bitmap.UnlockBits(data); }
            var renderedPath = Path.Combine(work, $"page-{i + 1}.png");
            bitmap.Save(renderedPath, System.Drawing.Imaging.ImageFormat.Png);
            var normalizedPath = Path.Combine(work, $"normalized-{i + 1}.png");
            NormalizeImage(renderedPath, normalizedPath);
            pages.Add(normalizedPath);
        }
        return pages;
    }

    private static void NormalizeImage(string source, string destination)
    {
        using var input = new Bitmap(source);
        // Ignore isolated scanner streaks by finding the strongest contiguous block of active rows.
        var activeRows = new List<int>();
        var step = Math.Max(2, Math.Min(input.Width, input.Height) / 900);
        for (var y = 0; y < input.Height; y += step)
        {
            var dark = 0;
            for (var x = 0; x < input.Width; x += step)
            {
                var color = input.GetPixel(x, y);
                if (color.R < 228 || color.G < 228 || color.B < 228) dark++;
            }
            if (dark >= Math.Max(8, input.Width / step / 80)) activeRows.Add(y);
        }
        if (activeRows.Count == 0) throw new InvalidDataException("تعذر العثور على محتوى في صفحة الكشف.");
        var runs = new List<List<int>>();
        foreach (var row in activeRows)
        {
            if (runs.Count == 0 || row - runs[^1][^1] > step * 12) runs.Add([row]);
            else runs[^1].Add(row);
        }
        var strongestRun = runs.OrderByDescending(run => run[^1] - run[0]).First();
        var top = strongestRun[0]; var bottom = Math.Min(input.Height - 1, strongestRun[^1] + step);
        var columnHits = new int[input.Width];
        for (var y = top; y <= bottom; y += step)
        for (var x = 0; x < input.Width; x += step)
        {
            var color = input.GetPixel(x, y);
            if (color.R < 228 || color.G < 228 || color.B < 228) columnHits[x]++;
        }
        var usedColumns = Enumerable.Range(0, (input.Width + step - 1) / step)
            .Select(i => Math.Min(input.Width - 1, i * step))
            .Where(x => columnHits[x] >= Math.Max(2, (bottom - top) / step / 120)).ToArray();
        if (usedColumns.Length == 0) throw new InvalidDataException("تعذر تحديد حدود الكشف.");
        var left = usedColumns.Min(); var right = usedColumns.Max();
        var padding = Math.Max(12, input.Width / 100);
        var bounds = Rectangle.FromLTRB(Math.Max(0, left - padding), Math.Max(0, top - padding), Math.Min(input.Width, right + padding), Math.Min(input.Height, bottom + padding));
        var targetHeight = Math.Max(1, (int)Math.Round(bounds.Height * (double)RenderWidth / bounds.Width));
        using var normalized = new Bitmap(RenderWidth, targetHeight, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(normalized))
        {
            graphics.Clear(Color.White);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(input, new Rectangle(0, 0, RenderWidth, targetHeight), bounds, GraphicsUnit.Pixel);
        }
        normalized.Save(destination, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static int GetImageHeight(string path) { using var image = Image.FromFile(path); return image.Height; }

    private static IReadOnlyList<ColumnBand> FindTableColumns(string imagePath)
    {
        using var image = new Bitmap(imagePath);
        var top = Math.Max(0, (int)(image.Height * .04));
        var bottom = Math.Min(image.Height, (int)(image.Height * .97));
        var rowStep = Math.Max(1, image.Height / 1200);
        var sampledRows = (bottom - top + rowStep - 1) / rowStep;
        var requiredDarkPixels = sampledRows * .20;
        var activeColumns = new List<int>();
        for (var x = (int)(image.Width * .01); x < image.Width * .99; x++)
        {
            var dark = 0;
            for (var y = top; y < bottom; y += rowStep)
            {
                var color = image.GetPixel(x, y);
                if (color.R < 205 && color.G < 205 && color.B < 205) dark++;
            }
            if (dark >= requiredDarkPixels) activeColumns.Add(x);
        }

        var runs = new List<List<int>>();
        foreach (var x in activeColumns)
        {
            if (runs.Count == 0 || x - runs[^1][^1] > 3) runs.Add([x]);
            else runs[^1].Add(x);
        }
        var rules = runs.Where(run => run.Count <= 12).Select(run => (int)Math.Round(run.Average())).ToArray();
        // Six ministry fields exist in every variant; the newer template adds
        // a notes column on the far left. Each pair of rules closes one field.
        if (rules.Length is < 7 or > 10) return FallbackColumns(image.Width, image.Height);

        return rules.Zip(rules.Skip(1), (left, right) => new ColumnBand(left, right)).ToArray();
    }

    private static IReadOnlyList<ColumnBand> FallbackColumns(int width, int height)
    {
        var fractions = height < width * .72
            ? new[] { 0d, .17, .32, .55, .79, .93, 1d }
            : new[] { 0d, .10, .29, .41, .60, .85, .96, 1d };
        return fractions.Zip(fractions.Skip(1), (left, right) => new ColumnBand((int)(left * width), (int)(right * width))).ToArray();
    }

    private static IReadOnlyList<ExtractedCardRow> ReadRows(TesseractEngine engine, Pix pix, PageSegMode mode, IReadOnlyList<RowBand> bands, int height, IReadOnlyList<ColumnBand> columns)
    {
        const int width = RenderWidth;
        using var page = engine.Process(pix, mode);
        using var iterator = page.GetIterator();
        var words = new List<OcrWord>();
        iterator.Begin();
        do
        {
            var text = iterator.GetText(PageIteratorLevel.Word)?.Trim();
            if (string.IsNullOrWhiteSpace(text) || !iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var box)) continue;
            words.Add(new OcrWord(text, box.X1, box.Y1, box.X2, box.Y2, iterator.GetConfidence(PageIteratorLevel.Word)));
        } while (iterator.Next(PageIteratorLevel.Word));

        List<List<OcrWord>> groups;
        if (bands.Count >= 3)
        {
            groups = bands.Select(band => words.Where(word => word.CenterY >= band.Top && word.CenterY < band.Bottom).ToList()).ToList();
        }
        else
        {
            groups = [];
            var tolerance = Math.Max(26, height * .014);
            foreach (var word in words.OrderBy(w => w.CenterY))
            {
                var group = groups.LastOrDefault(g => Math.Abs(g.Average(x => x.CenterY) - word.CenterY) <= tolerance);
                if (group is null) groups.Add([word]); else group.Add(word);
            }
        }

        var rows = new List<ExtractedCardRow>();
        var compactLayout = height < width * .72;
        foreach (var group in groups)
        {
            if (group.Count == 0 && bands.Count < 3) continue;
            var y = group.Count == 0 ? 0.5 : group.Average(w => w.CenterY) / height;
            var firstDataRowY = compactLayout ? .12 : .26;
            var lastDataRowY = compactLayout ? .97 : .94;
            if (bands.Count < 3 && (y < firstDataRowY || y > lastDataRowY || group.Count < 2)) continue;
            if ((compactLayout || bands.Count >= 3) && !group.Any(word => HasDigit(word.Text))) continue;

            var rtlColumns = columns.Count - 1;
            string InColumn(int index, bool rtl = false) =>
                index < 0 || index >= columns.Count ? string.Empty : string.Join(" ", group
                    .Where(w => w.CenterX >= columns[index].Left && w.CenterX < columns[index].Right)
                    .OrderBy(w => rtl ? -w.CenterX : w.CenterX).Select(w => w.Text)).Trim();

            // The ministry table is laid out right-to-left: row number, serial,
            // beneficiary name, card name, card fragment, national fragment,
            // and sometimes a notes column at the far left.
            var sourceRowNumber = DigitsAndSeparators(InColumn(rtlColumns));
            var serial = DigitsAndSeparators(InColumn(rtlColumns - 1));
            var name = InColumn(rtlColumns - 2, rtl: true);
            var cardName = InColumn(rtlColumns - 3);
            var card = DigitsAndSeparators(InColumn(rtlColumns - 4));
            var national = DigitsAndSeparators(InColumn(rtlColumns - 5));
            var notes = columns.Count >= 7 ? InColumn(0, rtl: true) : string.Empty;
            if (string.IsNullOrWhiteSpace(name)) name = "مراجعة الاسم";
            var confidence = group.Count == 0 ? 0 : group.Average(w => Math.Clamp(w.Confidence, 0, 100));
            rows.Add(new ExtractedCardRow(EmptyToNull(sourceRowNumber), EmptyToNull(serial), name,
                EmptyToNull(cardName), EmptyToNull(card), EmptyToNull(national), EmptyToNull(notes), confidence));
        }
        return rows;
    }

    private static IReadOnlyList<ExtractedCardRow> MergeRows(IReadOnlyList<ExtractedCardRow> sparse, IReadOnlyList<ExtractedCardRow> block)
    {
        var count = Math.Max(sparse.Count, block.Count);
        var rows = new List<ExtractedCardRow>(count);
        for (var i = 0; i < count; i++)
        {
            var sparseRow = i < sparse.Count ? sparse[i] : null;
            var blockRow = i < block.Count ? block[i] : null;
            var name = sparseRow?.BeneficiaryName;
            if (string.IsNullOrWhiteSpace(name) || name == "مراجعة الاسم") name = blockRow?.BeneficiaryName;
            rows.Add(new ExtractedCardRow(
                sparseRow?.SourceRowNumber ?? blockRow?.SourceRowNumber,
                sparseRow?.SerialNumber ?? blockRow?.SerialNumber,
                string.IsNullOrWhiteSpace(name) ? "مراجعة الاسم" : name,
                sparseRow?.CardName ?? blockRow?.CardName,
                sparseRow?.CardNumber ?? blockRow?.CardNumber,
                sparseRow?.NationalNumber ?? blockRow?.NationalNumber,
                sparseRow?.Notes ?? blockRow?.Notes,
                Math.Max(sparseRow?.Confidence ?? 0, blockRow?.Confidence ?? 0)));
        }
        return rows;
    }

    private static IReadOnlyList<string?> ReadNumericColumn(TesseractEngine engine, string imagePath, string work, IReadOnlyList<RowBand> bands, ColumnBand column, string label)
    {
        using var source = new Bitmap(imagePath);
        const int scale = 2;
        var sourceBounds = Rectangle.FromLTRB(column.Left, 0, column.Right, source.Height);
        using var crop = new Bitmap(sourceBounds.Width * scale, sourceBounds.Height * scale, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(crop))
        {
            graphics.Clear(Color.White);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, new Rectangle(0, 0, crop.Width, crop.Height), sourceBounds, GraphicsUnit.Pixel);
        }
        var path = Path.Combine(work, $"{Path.GetFileNameWithoutExtension(imagePath)}-{label}.png");
        crop.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        using var pix = Pix.LoadFromFile(path);
        using var page = engine.Process(pix, PageSegMode.SparseText);
        using var iterator = page.GetIterator();
        var words = new List<(string Text, double CenterY)>();
        iterator.Begin();
        do
        {
            var text = iterator.GetText(PageIteratorLevel.Word)?.Trim();
            if (string.IsNullOrWhiteSpace(text) || !HasDigit(text) || !iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var box)) continue;
            words.Add((text, (box.Y1 + box.Y2) / (2.0 * scale)));
        } while (iterator.Next(PageIteratorLevel.Word));

        return bands.Select(band => EmptyToNull(DigitsAndSeparators(string.Join(" ",
            words.Where(word => word.CenterY >= band.Top && word.CenterY < band.Bottom).Select(word => word.Text))))).ToArray();
    }

    private static string? At(IReadOnlyList<string?> values, int index) => index < values.Count ? values[index] : null;

    private static ExtractedCardRow[] RescanMissingNumericFields(TesseractEngine engine, string imagePath, string work,
        IReadOnlyList<RowBand> bands, IReadOnlyList<ColumnBand> columns, ExtractedCardRow[] rows)
    {
        var columnsToRead = new List<(int Index, string Label, Func<ExtractedCardRow, string?> Get, Action<int, string> Set)>
        {
            (columns.Count - 2, "serial-number", row => row.SerialNumber, (i, value) => rows[i] = rows[i] with { SerialNumber = value }),
            (columns.Count - 5, "card-number", row => row.CardNumber, (i, value) => rows[i] = rows[i] with { CardNumber = value }),
            (columns.Count - 6, "national-number", row => row.NationalNumber, (i, value) => rows[i] = rows[i] with { NationalNumber = value })
        };
        foreach (var (index, label, getValue, setValue) in columnsToRead)
        {
            var presentValues = rows.Select(getValue).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
            var dominantLength = presentValues.GroupBy(value => value!.Length).OrderByDescending(group => group.Count()).FirstOrDefault()?.Key ?? 0;
            var inconsistentLengths = presentValues.Length > 1 && presentValues.Count(value => value!.Length == dominantLength) < presentValues.Length * .9;
            if (label != "serial-number" && presentValues.Length == rows.Length && !inconsistentLengths) continue;
            var values = ReadNumericColumn(engine, imagePath, work, bands, columns[index], label);
            for (var i = 0; i < rows.Length; i++)
                if ((label == "serial-number" || string.IsNullOrWhiteSpace(getValue(rows[i])) || (inconsistentLengths && getValue(rows[i])?.Length != dominantLength)) &&
                    !string.IsNullOrWhiteSpace(At(values, i)))
                    setValue(i, At(values, i)!);
        }
        return rows;
    }

    private static IReadOnlyList<RowBand> FindTableRowBands(string imagePath)
    {
        using var image = new Bitmap(imagePath);
        var stride = Math.Max(1, image.Width / 450);
        var sampleCount = ((int)(image.Width * .94) - (int)(image.Width * .03) + stride - 1) / stride;
        var activeRows = new List<int>();
        for (var y = 0; y < image.Height; y++)
        {
            var dark = 0;
            for (var x = (int)(image.Width * .03); x < image.Width * .97; x += stride)
            {
                var color = image.GetPixel(x, y);
                if (color.R < 225 && color.G < 225 && color.B < 225) dark++;
            }
            if (dark >= sampleCount * .62) activeRows.Add(y);
        }

        var lineRuns = new List<List<int>>();
        foreach (var row in activeRows)
        {
            if (lineRuns.Count == 0 || row - lineRuns[^1][^1] > 3) lineRuns.Add([row]);
            else lineRuns[^1].Add(row);
        }
        var rules = lineRuns.Where(run => run.Count >= 3).Select(run => (int)Math.Round(run.Average())).ToArray();
        if (rules.Length < 4) return [];

        var gaps = rules.Zip(rules.Skip(1), (top, bottom) => bottom - top).Where(gap => gap is >= 38 and <= 100).Order().ToArray();
        if (gaps.Length < 3) return [];
        var medianGap = gaps[gaps.Length / 2];
        var tolerance = Math.Max(8, medianGap * .2);
        var chains = new List<List<int>>();
        foreach (var rule in rules)
        {
            if (chains.Count == 0) { chains.Add([rule]); continue; }
            var gap = rule - chains[^1][^1];
            var shortFirstRow = chains[^1].Count == 1 && gap is >= 30 and < 38;
            if (gap < 30 || gap > 100 || (!shortFirstRow && Math.Abs(gap - medianGap) > tolerance)) chains.Add([rule]);
            else chains[^1].Add(rule);
        }
        var best = chains.OrderByDescending(chain => chain.Count).FirstOrDefault();
        if (best is null || best.Count < 4) return [];
        return best.Zip(best.Skip(1), (top, bottom) => new RowBand(top, bottom)).ToArray();
    }

    private sealed record RowBand(int Top, int Bottom);
    private sealed record ColumnBand(int Left, int Right);

    private static bool HasDigit(string text) => text.Any(char.IsDigit);
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string DigitsAndSeparators(string value) => new(value.Where(c => char.IsDigit(c) || c is '-' or '/').Select(NormalizeDigit).ToArray());
    private static char NormalizeDigit(char value)
    {
        if (!char.IsDigit(value)) return value;
        var digit = char.GetNumericValue(value);
        return digit is >= 0 and <= 9 ? (char)('0' + (int)digit) : value;
    }
    private sealed record OcrWord(string Text, int Left, int Top, int Right, int Bottom, float Confidence)
    {
        public double CenterX => (Left + Right) / 2.0;
        public double CenterY => (Top + Bottom) / 2.0;
    }
}
