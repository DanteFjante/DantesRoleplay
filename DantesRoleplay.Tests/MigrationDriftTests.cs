using DantesRoleplay.DataAccess;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;

namespace DantesRoleplay.Tests;

/// <summary>
/// Guards the migrations against the model.
///
/// Every other test builds its schema with <c>EnsureCreated</c>, which reads the MODEL and ignores
/// migrations entirely. That is right for test speed and wrong for safety: it means the whole
/// suite can pass green over migrations that no longer match, and the first symptom is the
/// application refusing to start with PendingModelChangesWarning.
///
/// That is not hypothetical. Five columns were added to the model — Subject, ProceduresCited,
/// ProceduresRead, Governs, SourceHash — without regenerating the migration, and 55 passing tests
/// said nothing. A cold walk found it at startup instead.
/// </summary>
public sealed class MigrationDriftTests
{
    [Fact]
    public void The_migrations_match_the_model()
    {
        // No connection is opened — HasPendingModelChanges compares the current model against the
        // snapshot compiled into the migrations assembly.
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var db = new DantesRoleplayDbContext(options);

        Assert.False(
            db.Database.HasPendingModelChanges(),
            "The model has changed since the last migration was generated, so the application "
            + "will refuse to start. Regenerate with:\n"
            + "  dotnet ef migrations add <Name> --project DantesRoleplay.DataAccess");
    }

    [Fact]
    public async Task No_migration_needs_an_operation_that_cannot_run_in_a_transaction()
    {
        // EF implements DropColumn on SQLite by rebuilding the table, which needs
        // "PRAGMA foreign_keys = 0" — and that cannot run inside the migration transaction, so
        // the migration stops being atomic and EF warns. Harmless on a table with no foreign
        // keys, indistinguishable from the case that is not, which is why this is a test rather
        // than a note. Use native ALTER TABLE DROP COLUMN via migrationBuilder.Sql instead.
        var file = Path.Combine(Path.GetTempPath(), $"nontx-{Guid.NewGuid():n}.db");
        var warnings = new List<string>();

        using var factory = LoggerFactory.Create(b => b
            .AddProvider(new CollectingLoggerProvider(warnings))
            .SetMinimumLevel(LogLevel.Warning));

        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite($"Data Source={file}")
                .UseLoggerFactory(factory)
                .Options;

            await using (var db = new DantesRoleplayDbContext(options))
            {
                await db.Database.MigrateAsync();
            }

            var offenders = warnings
                .Where(w => w.Contains("NonTransactionalMigrationOperationWarning", StringComparison.Ordinal))
                .ToList();

            Assert.True(offenders.Count == 0,
                "A migration contains an operation that cannot run in a transaction, so it is no "
                + "longer atomic:\n  " + string.Join("\n  ", offenders));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task An_existing_database_can_be_upgraded_to_the_current_schema()
    {
        // The case the empty-database test cannot see, and the one that actually bit: a database
        // created by an EARLIER migration must be able to reach the current schema by moving
        // forward. Regenerating the initial migration instead of adding a delta passes every
        // fresh-database check and then fails on the only database that already exists, with
        // "table already exists".
        var file = Path.Combine(Path.GetTempPath(), $"upgrade-{Guid.NewGuid():n}.db");

        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite($"Data Source={file}")
                .Options;

            var all = new List<string>();

            // Stop at the first migration — the state a database created before the audit and
            // contract fields existed would be in.
            await using (var db = new DantesRoleplayDbContext(options))
            {
                all = db.Database.GetMigrations().ToList();
                Assert.True(all.Count >= 2,
                    "Expected at least two migrations: an initial one and a forward delta. If "
                    + "there is only one, an applied migration was replaced rather than added to.");

                await db.GetService<IMigrator>().MigrateAsync(all[0]);
            }

            // Then upgrade the rest of the way, exactly as the application does at startup.
            await using (var db = new DantesRoleplayDbContext(options))
            {
                await db.Database.MigrateAsync();

                Assert.Empty(await db.Database.GetPendingMigrationsAsync());

                var connection = db.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                await using var command = connection.CreateCommand();
                command.CommandText = "select name from sqlite_master where type = 'table' and name = 'procedure_relation'";

                Assert.Null(await command.ExecuteScalarAsync());

                // Prove the added columns are really there and writable.
                var store = new ProcedureStore(db);
                var written = await store.WriteAsync(new DantesRoleplay.Procedures.WriteProcedureRequest
                {
                    Id = "procedure.test.upgraded",
                    Category = "test",
                    Name = "Upgraded",
                    Description = "Written after an upgrade.",
                    Governs = "nothing",
                    Instructions = "1. Exist.",
                    CreatedBy = "test"
                });

                Assert.Equal("nothing", written.Procedure.Governs);

                var op = await new OperationLog(db).RecordAsync(
                    "write_procedure", "ok", success: true, subject: "procedure.test.upgraded");

                Assert.Equal("procedure.test.upgraded", op.Subject);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task Migrating_an_empty_database_produces_a_usable_schema()
    {
        // The path the application actually takes at startup — Migrate, not EnsureCreated — so a
        // migration that is present but broken fails here rather than on Dante's machine.
        var file = Path.Combine(Path.GetTempPath(), $"drift-{Guid.NewGuid():n}.db");

        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite($"Data Source={file}")
                .Options;

            await using (var db = new DantesRoleplayDbContext(options))
            {
                await db.Database.MigrateAsync();

                // Touch the columns that drifted, so a migration missing one fails loudly here.
                var store = new ProcedureStore(db);
                var written = await store.WriteAsync(new DantesRoleplay.Procedures.WriteProcedureRequest
                {
                    Id = "procedure.test.migrated",
                    Category = "test",
                    Name = "Migrated",
                    Description = "Written against a migrated database.",
                    Governs = "nothing",
                    Instructions = "1. Exist.",
                    CreatedBy = "test"
                });

                Assert.Equal("nothing", written.Procedure.Governs);

                // SourceHash is no longer something a caller can supply — the store computes it —
                // so this asserts the column is present and written rather than round-tripped.
                Assert.Equal(
                    DantesRoleplay.Content.ContentHash.ForProcedure(
                        "test",
                        "Migrated",
                        "Written against a migrated database.",
                        "nothing",
                        "1. Exist.",
                        string.Empty,
                        DantesRoleplay.Procedures.ProcedureStatus.Active),
                    written.Procedure.SourceHash);

                var log = new OperationLog(db);
                var op = await log.RecordAsync(
                    "write_procedure", "ok", success: true,
                    subject: "procedure.test.migrated",
                    proceduresCited: ["procedure.contract.create"]);

                Assert.Equal("procedure.test.migrated", op.Subject);
                Assert.Equal("procedure.contract.create", op.ProceduresCited);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task Activation_evidence_migration_preserves_exact_history_and_round_trips()
    {
        var file = Path.Combine(Path.GetTempPath(), $"activation-dedup-{Guid.NewGuid():n}.db");
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite($"Data Source={file}")
            .Options;

        try
        {
            string previous;
            IReadOnlyList<string> before;
            await using (var db = new DantesRoleplayDbContext(options))
            {
                var migrations = db.Database.GetMigrations().ToArray();
                previous = migrations[^2];
                await db.GetService<IMigrator>().MigrateAsync(previous);
                await db.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO system_application (Id, DisplayName, Description, CreatedAtUtc)
                    VALUES ('storage-test', 'Storage', 'Migration fixture.', '2026-09-07T00:00:00Z');
                    INSERT INTO system_application_revision (ApplicationId, Revision, Fingerprint, CreatedAtUtc)
                    VALUES ('storage-test', 1, 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', '2026-09-07T00:00:00Z');
                    INSERT INTO operation
                        (Id, Error, Intent, ProceduresCited, ProceduresRead, Subject, Success, Summary, Timestamp, Tool)
                    VALUES
                        ('activation-storage-op-1', '', '', '', '', '', 1, '', '2026-09-07T00:00:00Z', 'commit'),
                        ('activation-storage-op-2', '', '', '', '', '', 1, '', '2026-09-07T00:00:01Z', 'commit');
                    INSERT INTO system_application_activation_revision
                        (ApplicationId, ActivationRevision, ApplicationRevision, ApplicationFingerprint,
                         PreviewFingerprint, ScannedDocumentsFingerprint, CandidateManifestFingerprint,
                         DependencyGraphFingerprint, ActivationFingerprint, DependencyCoverageVersion,
                         DependencyCoverageComplete, ActivatedByOperationId, ActivatedAtUtc)
                    VALUES
                        ('storage-test', 1, 1,
                         'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',
                         'BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB',
                         'CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC',
                         'DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD',
                         'EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE',
                         'FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF',
                         'fixture-v1', 0, 'activation-storage-op-1', '2026-09-07T00:00:00Z'),
                        ('storage-test', 2, 1,
                         'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA',
                         '1111111111111111111111111111111111111111111111111111111111111111',
                         'CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC',
                         '2222222222222222222222222222222222222222222222222222222222222222',
                         'EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE',
                         '3333333333333333333333333333333333333333333333333333333333333333',
                         'fixture-v1', 0, 'activation-storage-op-2', '2026-09-07T00:00:01Z');
                    INSERT INTO system_application_activation_document
                        (ApplicationId, ActivationRevision, Ordinal, LogicalIdentity, SourceId, Trust,
                         Precedence, RelativePath, MediaType, ContentFingerprint, Length, IsText)
                    VALUES
                        ('storage-test', 1, 0, 'file:catalog/entry.json', 'catalog', 1, 10,
                         'catalog/entry.json', 'application/json',
                         '4444444444444444444444444444444444444444444444444444444444444444', 12, 1),
                        ('storage-test', 2, 0, 'file:catalog/entry.json', 'catalog', 1, 10,
                         'catalog/entry.json', 'application/json',
                         '4444444444444444444444444444444444444444444444444444444444444444', 12, 1),
                        ('storage-test', 2, 1, 'file:catalog/other.json', 'catalog', 1, 10,
                         'catalog/other.json', 'application/json',
                         '5555555555555555555555555555555555555555555555555555555555555555', 13, 1);
                    """);
                await db.Database.OpenConnectionAsync();
                before = await ReadActivationDocumentsAsync(db.Database.GetDbConnection(), compact: false);
                await db.Database.CloseConnectionAsync();

                await db.Database.MigrateAsync();
                Assert.Equal(3, await ScalarAsync(db.Database.GetDbConnection(),
                    "SELECT count(*) FROM system_application_activation_document"));
                Assert.Equal(2, await ScalarAsync(db.Database.GetDbConnection(),
                    "SELECT count(*) FROM system_application_activation_document_identity"));
                Assert.Equal(2, await ScalarAsync(db.Database.GetDbConnection(),
                    "SELECT count(*) FROM system_application_activation_document_evidence"));
                var after = await ReadActivationDocumentsAsync(db.Database.GetDbConnection(), compact: true);
                Assert.Equal(before, after);

                await db.GetService<IMigrator>().MigrateAsync(previous);
                Assert.Equal(before,
                    await ReadActivationDocumentsAsync(db.Database.GetDbConnection(), compact: false));
                await db.Database.MigrateAsync();
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(file)) File.Delete(file);
        }
    }

    private static async Task<IReadOnlyList<string>> ReadActivationDocumentsAsync(
        DbConnection connection,
        bool compact)
    {
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = compact
            ? """
              SELECT link.ApplicationId, link.ActivationRevision, link.Ordinal, identity.LogicalIdentity,
                     evidence.SourceId, evidence.Trust, evidence.Precedence, evidence.RelativePath,
                     evidence.MediaType, evidence.ContentFingerprint, evidence.Length, evidence.IsText
              FROM system_application_activation_document AS link
              JOIN system_application_activation_document_identity AS identity
                ON identity.ApplicationId = link.ApplicationId AND identity.Id = link.IdentityId
              JOIN system_application_activation_document_evidence AS evidence
                ON evidence.IdentityId = link.IdentityId AND evidence.EvidenceVersion = link.EvidenceVersion
              ORDER BY link.ApplicationId, link.ActivationRevision, link.Ordinal
              """
            : """
              SELECT ApplicationId, ActivationRevision, Ordinal, LogicalIdentity, SourceId, Trust,
                     Precedence, RelativePath, MediaType, ContentFingerprint, Length, IsText
              FROM system_application_activation_document
              ORDER BY ApplicationId, ActivationRevision, Ordinal
              """;
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            values.Add(string.Join('\u001f', Enumerable.Range(0, reader.FieldCount)
                .Select(index => Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture))));
        return values;
    }

    private static async Task<int> ScalarAsync(DbConnection connection, string sql)
    {
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>Collects log messages so a test can assert on what EF reported.</summary>
internal sealed class CollectingLoggerProvider(List<string> sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Collector(sink);

    public void Dispose() { }

    private sealed class Collector(List<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            sink.Add($"[{eventId.Name}] {formatter(state, exception)}");
    }
}
