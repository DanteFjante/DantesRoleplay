using DantesRoleplay.Applications;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DantesRoleplay.Play.Tests;

public sealed class ConversationMemoryMigrationTests
{
    private const string Previous = "20260912081912_DurableConditionalWorkflowObservers";

    [Fact]
    public async Task Copied_previous_schema_upgrades_without_losing_existing_state_and_runs_the_journal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "conversation-memory-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.db");
        var rehearsal = Path.Combine(directory, "rehearsal.db");
        try
        {
            await using (var db = Context(source))
            {
                await db.GetService<IMigrator>().MigrateAsync(Previous);
                await db.Database.ExecuteSqlRawAsync("CREATE TABLE p4_preservation_marker (value TEXT NOT NULL);");
                await db.Database.ExecuteSqlRawAsync("INSERT INTO p4_preservation_marker (value) VALUES ('retained');");
            }
            File.Copy(source, rehearsal);

            await using var upgraded = Context(rehearsal);
            await upgraded.Database.MigrateAsync();
            Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
            Assert.Equal("retained", await ScalarAsync(upgraded, "SELECT value FROM p4_preservation_marker"));
            var tables = await NamesAsync(upgraded,
                "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'application_conversation_memory_%' ORDER BY name");
            Assert.Equal([
                "application_conversation_memory_delivery",
                "application_conversation_memory_derived",
                "application_conversation_memory_derived_source",
                "application_conversation_memory_journal",
                "application_conversation_memory_message"
            ], tables);

            var applications = new SqliteApplicationRegistry(upgraded);
            var app = ApplicationIdentifier.Parse("memory-migration-fixture");
            var revision = applications.Register(new(app, "Memory migration fixture", "", []));
            new SqliteStateSpaceRegistry(upgraded, applications).Create(
                new("memory-migration-space", revision, new string('A', 64)));
            var binding = new ConversationMemoryBinding(
                new("principal.fixture", app.Value, "memory-migration-space", "session.fixture"),
                "codex", "project.fixture", directory, "thread.fixture");
            var store = new ApplicationConversationMemoryStore(upgraded);
            store.Connect(binding);
            var append = store.AppendTurn(new(binding, "turn.fixture",
            [
                new("user.fixture", ConversationMemoryRoles.User,
                    ConversationMemoryMessageKinds.UserPrompt, "Prompt.", DateTime.UnixEpoch),
                new("assistant.fixture", ConversationMemoryRoles.Assistant,
                    ConversationMemoryMessageKinds.AssistantFinal, "Answer.", DateTime.UnixEpoch)
            ], "migration-test", DateTime.UnixEpoch, "request.fixture"));
            Assert.Equal(2, append.Messages.Count);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static DantesRoleplayDbContext Context(string path) => new(
        new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False").Options);

    private static async Task<string> ScalarAsync(DantesRoleplayDbContext db, string sql)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            return (string)(await command.ExecuteScalarAsync())!;
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private static async Task<string[]> NamesAsync(DantesRoleplayDbContext db, string sql)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync();
            var names = new List<string>();
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));
            return names.ToArray();
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
