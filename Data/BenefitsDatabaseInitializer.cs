using Microsoft.EntityFrameworkCore;

namespace BenefitCardsPortal.Data;

public static class BenefitsDatabaseInitializer
{
    private static readonly IReadOnlyDictionary<string, string> AddedCardColumns = new Dictionary<string, string>
    {
        ["SourceRowNumber"] = "TEXT NULL",
        ["CardName"] = "TEXT NULL",
        ["Notes"] = "TEXT NULL",
        ["BeneficiaryNameNormalized"] = "TEXT NOT NULL DEFAULT ''"
    };

    public static async Task InitializeAsync(IServiceProvider services, bool createDatabaseIfMissing, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BenefitsDbContext>();
        if (createDatabaseIfMissing) await db.Database.EnsureCreatedAsync(cancellationToken);

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('BenefitCardRecords');";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) existingColumns.Add(reader.GetString(1));
            }

            foreach (var (name, definition) in AddedCardColumns)
            {
                if (existingColumns.Contains(name)) continue;
                await using var command = connection.CreateCommand();
                command.CommandText = $"ALTER TABLE BenefitCardRecords ADD COLUMN {name} {definition};";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE BenefitCardRecords SET BeneficiaryNameNormalized = lower(replace(replace(replace(replace(replace(replace(replace(replace(replace(replace(replace(replace(BeneficiaryName, 'أ', 'ا'), 'إ', 'ا'), 'آ', 'ا'), 'ٱ', 'ا'), 'ى', 'ي'), 'ئ', 'ي'), 'ؤ', 'و'), 'ـ', ''), 'َ', ''), 'ً', ''), 'ُ', ''), 'ِ', '')) WHERE BeneficiaryNameNormalized = ''; CREATE INDEX IF NOT EXISTS IX_BenefitCardRecords_BeneficiaryNameNormalized ON BenefitCardRecords (BeneficiaryNameNormalized);";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            var userColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('AspNetUsers');";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) userColumns.Add(reader.GetString(1));
            }
            if (userColumns.Count > 0)
            {
                if (!userColumns.Contains("IsActive"))
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "ALTER TABLE AspNetUsers ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1;";
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
                if (!userColumns.Contains("MustChangePassword"))
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "ALTER TABLE AspNetUsers ADD COLUMN MustChangePassword INTEGER NOT NULL DEFAULT 0;";
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "CREATE TABLE IF NOT EXISTS EmployeeAuditEvents (Id INTEGER NOT NULL CONSTRAINT PK_EmployeeAuditEvents PRIMARY KEY AUTOINCREMENT, OccurredAtUtc TEXT NOT NULL, Actor TEXT NOT NULL, Action TEXT NOT NULL, BatchId INTEGER NULL, RecordId INTEGER NULL, Summary TEXT NOT NULL); CREATE INDEX IF NOT EXISTS IX_EmployeeAuditEvents_OccurredAtUtc ON EmployeeAuditEvents (OccurredAtUtc); CREATE TABLE IF NOT EXISTS PortalSettings (Key TEXT NOT NULL CONSTRAINT PK_PortalSettings PRIMARY KEY, Value TEXT NOT NULL);";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }
}
