using System.Globalization;
using System.IO.Compression;
using BenefitCardsPortal.Data;
using BenefitCardsPortal.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BenefitCardsPortal.Services;

public sealed class BackupService(BenefitsDbContext db, IWebHostEnvironment environment) : IBackupService
{
    private const string EnabledKey = "BackupEnabled";
    private const string TimeKey = "BackupDailyTime";
    private const string RetentionKey = "BackupRetentionDays";
    private const string FilePatternPrefix = "benefitcards-backup-";
    private static readonly TimeZoneInfo LibyaTimeZone = ResolveLibyaTimeZone();
    private static readonly SemaphoreSlim BackupGate = new(1, 1);
    private string BackupDirectory => Path.Combine(environment.ContentRootPath, "App_Data", "Backups");

    public async Task<BackupPolicy> GetPolicyAsync(CancellationToken cancellationToken = default)
    {
        var entries = await db.PortalSettings.AsNoTracking().Where(entry => entry.Key == EnabledKey || entry.Key == TimeKey || entry.Key == RetentionKey)
            .ToDictionaryAsync(entry => entry.Key, entry => entry.Value, cancellationToken);
        return new BackupPolicy(
            !entries.TryGetValue(EnabledKey, out var enabled) || !bool.TryParse(enabled, out var parsedEnabled) || parsedEnabled,
            entries.TryGetValue(TimeKey, out var time) && TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? time : "02:00",
            entries.TryGetValue(RetentionKey, out var days) && int.TryParse(days, out var parsedDays) ? Math.Clamp(parsedDays, 1, 3650) : 30);
    }

    public async Task SavePolicyAsync(BackupPolicy policy, CancellationToken cancellationToken = default)
    {
        await SaveValueAsync(EnabledKey, policy.Enabled.ToString(), cancellationToken);
        await SaveValueAsync(TimeKey, policy.DailyTime, cancellationToken);
        await SaveValueAsync(RetentionKey, policy.RetentionDays.ToString(CultureInfo.InvariantCulture), cancellationToken);
    }

    public async Task RunScheduledBackupIfDueAsync(CancellationToken cancellationToken = default)
    {
        var policy = await GetPolicyAsync(cancellationToken);
        if (!policy.Enabled || !TimeOnly.TryParseExact(policy.DailyTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var scheduledTime)) return;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, LibyaTimeZone);
        if (TimeOnly.FromDateTime(localNow) < scheduledTime) return;
        Directory.CreateDirectory(BackupDirectory);
        var dailyPrefix = $"{FilePatternPrefix}{localNow:yyyyMMdd}-";
        if (Directory.EnumerateFiles(BackupDirectory, dailyPrefix + "*.zip").Any()) return;
        await CreateBackupAsync(cancellationToken);
    }

    public async Task<string> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(BackupDirectory);
        await BackupGate.WaitAsync(cancellationToken);
        var stamp = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, LibyaTimeZone).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var archivePath = Path.Combine(BackupDirectory, $"{FilePatternPrefix}{stamp}.zip");
        var databasePath = Path.Combine(BackupDirectory, $".database-{Guid.NewGuid():N}.tmp");
        try
        {
            var connectionString = db.Database.GetConnectionString() ?? throw new InvalidOperationException("تعذر قراءة اتصال قاعدة البيانات.");
            var builder = new SqliteConnectionStringBuilder(connectionString);
            var sourcePath = Path.IsPathRooted(builder.DataSource) ? builder.DataSource : Path.GetFullPath(builder.DataSource, environment.ContentRootPath);
            var sourceConnectionString = new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Shared }.ToString();
            await using (var source = new SqliteConnection(sourceConnectionString))
            {
                await source.OpenAsync(cancellationToken);
                await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
                await destination.OpenAsync(cancellationToken);
                source.BackupDatabase(destination);
            }

            var statementFiles = await GetStatementFilesFromSnapshotAsync(databasePath, cancellationToken);
            await using (var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
            {
                archive.CreateEntryFromFile(databasePath, "benefitcards.db", CompressionLevel.Fastest);
                foreach (var storedName in statementFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var safeName = Path.GetFileName(storedName);
                    if (string.IsNullOrWhiteSpace(safeName)) continue;
                    var statementPath = Path.Combine(environment.ContentRootPath, "App_Data", "Uploads", safeName);
                    if (!File.Exists(statementPath)) throw new FileNotFoundException("تعذر العثور على كشف مرتبط بسجل محفوظ؛ أُلغيت النسخة لتجنب إنشاء أرشيف ناقص.", statementPath);
                    archive.CreateEntryFromFile(statementPath, $"Uploads/{safeName}", CompressionLevel.Fastest);
                }
                var manifest = archive.CreateEntry("backup-info.txt", CompressionLevel.Fastest);
                await using var manifestWriter = new StreamWriter(manifest.Open());
                await manifestWriter.WriteAsync($"Backup time (UTC): {DateTime.UtcNow:O}\nStatements included: {statementFiles.Count}\n");
            }
            var policy = await GetPolicyAsync(cancellationToken);
            RemoveExpiredBackups(policy.RetentionDays, DateTime.UtcNow);
            return Path.GetFileName(archivePath);
        }
        catch
        {
            try { if (File.Exists(archivePath)) File.Delete(archivePath); } catch (IOException) { }
            throw;
        }
        finally
        {
            try { if (File.Exists(databasePath)) File.Delete(databasePath); } catch (IOException) { }
            BackupGate.Release();
        }
    }

    public IReadOnlyList<BackupFileViewModel> ListBackups()
    {
        if (!Directory.Exists(BackupDirectory)) return Array.Empty<BackupFileViewModel>();
        return Directory.EnumerateFiles(BackupDirectory, $"{FilePatternPrefix}*.zip")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTimeUtc)
            .Take(50)
            .Select(file => new BackupFileViewModel(file.Name, file.CreationTimeUtc.AddHours(2), file.Length))
            .ToList();
    }

    public string? FindBackupPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.StartsWith(FilePatternPrefix, StringComparison.Ordinal) || !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        var fullPath = Path.GetFullPath(Path.Combine(BackupDirectory, fileName));
        return fullPath.StartsWith(Path.GetFullPath(BackupDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath) ? fullPath : null;
    }

    private async Task SaveValueAsync(string key, string value, CancellationToken cancellationToken)
    {
        var entry = await db.PortalSettings.FindAsync([key], cancellationToken);
        if (entry is null) db.PortalSettings.Add(new PortalSetting { Key = key, Value = value });
        else entry.Value = value;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<List<string>> GetStatementFilesFromSnapshotAsync(string snapshotPath, CancellationToken cancellationToken)
    {
        await using var snapshot = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = snapshotPath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        await snapshot.OpenAsync(cancellationToken);
        await using var command = snapshot.CreateCommand();
        command.CommandText = "SELECT DISTINCT StoredFilePath FROM BenefitBatches WHERE StoredFilePath IS NOT NULL AND StoredFilePath <> '';";
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) names.Add(reader.GetString(0));
        return names;
    }

    private void RemoveExpiredBackups(int retentionDays, DateTime nowUtc)
    {
        var cutoff = nowUtc.AddDays(-retentionDays);
        foreach (var path in Directory.EnumerateFiles(BackupDirectory, $"{FilePatternPrefix}*.zip"))
            if (File.GetCreationTimeUtc(path) < cutoff)
                try { File.Delete(path); } catch (IOException) { }
    }

    private static TimeZoneInfo ResolveLibyaTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Tripoli"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Libya Standard Time"); }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.FindSystemTimeZoneById("Libya Standard Time"); }
    }
}
