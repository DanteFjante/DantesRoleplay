using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CodexBridge;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Play;
using DantesRoleplay.Tools.Commands;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tools.Tests;

public sealed class CaptureMemoryToolTests
{
    private const string Principal =
        "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Stop_hook_survives_failed_capture_then_replays_into_the_same_private_journal()
    {
        var root = Path.Combine(Path.GetTempPath(), "capture-memory-tool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var database = Path.Combine(root, "capture.db");
        var checkpoint = Path.Combine(root, "capture.checkpoint.json");
        const string externalThread = "thread.external.fixture";
        const string gameplaySession = "session.gameplay.fixture";
        const string turn = "turn.fixture.1";
        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite("Filename=" + database).Options;
            await using (var db = new DantesRoleplayDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                var applications = new SqliteApplicationRegistry(db);
                var revision = applications.Register(new(
                    ApplicationIdentifier.Parse("play-fixture"), "Play Fixture", "", []));
                new SqliteStateSpaceRegistry(db, applications).Create(
                    new("play-fixture-space", revision, new string('A', 64)));
            }

            var hook = Hook("Stop", externalThread, root, turn);
            var reads = new Queue<TextReader>([
                new StringReader(string.Empty), new StringReader(hook), new StringReader(string.Empty)
            ]);
            var client = new FailFirstCaptureClient(externalThread, turn);
            var tool = new CaptureMemoryTool(_ => client, () => reads.Dequeue());
            var output = new StringWriter();
            var error = new StringWriter();
            var optionValues = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["principal"] = Principal,
                ["application"] = "play-fixture",
                ["state-space"] = "play-fixture-space",
                ["gameplay-session"] = gameplaySession,
                ["project"] = "project.fixture",
                ["repository"] = root,
                ["thread"] = externalThread,
                ["checkpoint"] = checkpoint
            };
            var connect = new ToolContext([], new Dictionary<string, string>(optionValues, StringComparer.Ordinal)
            {
                ["connect"] = ""
            }, database, output, error);
            var context = new ToolContext([], optionValues, database, output, error);
            var retry = new ToolContext([], new Dictionary<string, string>(optionValues, StringComparer.Ordinal)
            {
                ["retry"] = ""
            }, database, output, error);

            Assert.Equal(0, await tool.RunAsync(connect, default));
            await Assert.ThrowsAsync<CodexBridgeException>(() => tool.RunAsync(context, default));
            var pending = await CodexCaptureCheckpointFile.LoadAsync(checkpoint);
            Assert.Equal(turn, Assert.Single(pending.Pending).TurnId);
            Assert.Empty(pending.CompletedTurnIds);
            await using (var failed = new DantesRoleplayDbContext(options))
                Assert.Equal(ConversationMemoryStatuses.RetryPending,
                    (await failed.Set<ApplicationConversationMemoryJournalRecord>().SingleAsync()).Status);

            Assert.Equal(0, await tool.RunAsync(retry, default));
            var completed = await CodexCaptureCheckpointFile.LoadAsync(checkpoint);
            Assert.Empty(completed.Pending);
            Assert.Equal(turn, Assert.Single(completed.CompletedTurnIds));
            Assert.Contains("\t2\t4", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(2, client.ReadAttempts);
            Assert.False(File.Exists(Path.Combine(root, "must-not-be-opened.jsonl")));

            await using var verify = new DantesRoleplayDbContext(options);
            var journal = await verify.Set<ApplicationConversationMemoryJournalRecord>().SingleAsync();
            Assert.Equal(gameplaySession, journal.SessionContextId);
            Assert.Equal(externalThread, journal.SourceThreadId);
            Assert.Equal(2, await verify.Set<ApplicationConversationMemoryMessageRecord>().CountAsync());
            Assert.Single(await verify.Set<ApplicationConversationMemoryDeliveryRecord>().ToArrayAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Matching_stop_hook_does_not_reconnect_a_disconnected_journal()
    {
        var root = Path.Combine(Path.GetTempPath(), "capture-memory-disconnect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var database = Path.Combine(root, "capture.db");
        var checkpoint = Path.Combine(root, "capture.checkpoint.json");
        const string externalThread = "thread.external.disconnected";
        const string gameplaySession = "session.gameplay.disconnected";
        try
        {
            var options = await CreateDatabaseAsync(database);
            var common = Options(root, checkpoint, externalThread, gameplaySession);
            var client = new FailFirstCaptureClient(externalThread, "turn.disconnected");
            var connect = new CaptureMemoryTool(_ => client, () => new StringReader(string.Empty));
            Assert.Equal(0, await connect.RunAsync(Context(database, common, connect: true), default));

            await using (var db = new DantesRoleplayDbContext(options))
                new ApplicationConversationMemoryStore(db).Disconnect(
                    new(Principal, "play-fixture", "play-fixture-space", gameplaySession));

            var hook = Hook("Stop", externalThread, root, "turn.disconnected");
            var capture = new CaptureMemoryTool(_ => client, () => new StringReader(hook));
            var error = new StringWriter();
            var result = await capture.RunAsync(Context(database, common, error: error), default);

            Assert.Equal(3, result);
            Assert.Contains("CONVERSATION_MEMORY_NOT_CONNECTED", error.ToString(), StringComparison.Ordinal);
            Assert.Equal(0, client.ReadAttempts);
            await using var verify = new DantesRoleplayDbContext(options);
            Assert.Equal(ConversationMemoryStatuses.Disconnected,
                (await verify.Set<ApplicationConversationMemoryJournalRecord>().SingleAsync()).Status);
            Assert.Empty(await verify.Set<ApplicationConversationMemoryMessageRecord>().ToArrayAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Overlapping_hook_processes_preserve_both_checkpoint_transitions()
    {
        var root = Path.Combine(Path.GetTempPath(), "capture-memory-overlap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var database = Path.Combine(root, "capture.db");
        var checkpoint = Path.Combine(root, "capture.checkpoint.json");
        const string externalThread = "thread.external.overlap";
        const string gameplaySession = "session.gameplay.overlap";
        try
        {
            await CreateDatabaseAsync(database);
            var common = Options(root, checkpoint, externalThread, gameplaySession);
            var client = new BlockingCaptureClient(externalThread, "turn.stop");
            var connect = new CaptureMemoryTool(_ => client, () => new StringReader(string.Empty));
            Assert.Equal(0, await connect.RunAsync(Context(database, common, connect: true), default));

            var stop = new CaptureMemoryTool(_ => client,
                () => new StringReader(Hook("Stop", externalThread, root, "turn.stop")));
            var prompt = new CaptureMemoryTool(_ => client,
                () => new StringReader(Hook("UserPromptSubmit", externalThread, root, "turn.prompt", "Next prompt.")));
            var stopOutput = new StringWriter();
            var stopTask = stop.RunAsync(Context(database, common, output: stopOutput), default);
            await client.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var promptTask = prompt.RunAsync(Context(database, common), default);
            await Task.Delay(100);
            Assert.False(promptTask.IsCompleted);
            client.Release.TrySetResult();

            Assert.Equal(0, await stopTask);
            Assert.Equal(0, await promptTask);
            Assert.Equal(string.Empty, stopOutput.ToString());
            var final = await CodexCaptureCheckpointFile.LoadAsync(checkpoint);
            Assert.Equal("turn.prompt", Assert.Single(final.Pending).TurnId);
            Assert.Equal("turn.stop", Assert.Single(final.CompletedTurnIds));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Hook_parser_accepts_the_current_Codex_field_and_rejects_a_conflicting_alias()
    {
        var currentJson = JsonSerializer.Serialize(new
        {
            hook_event_name = "UserPromptSubmit",
            session_id = "thread.current",
            transcript_path = (string?)null,
            cwd = "C:\\repo\\play",
            model = "gpt-5.6-sol",
            permission_mode = "default",
            turn_id = "turn.current",
            prompt = "Remember this turn."
        });
        var current = CodexCaptureHookInputParser.Parse("\uFEFF" + currentJson);

        Assert.Equal("UserPromptSubmit", current.EventName);
        var error = Assert.Throws<InvalidOperationException>(() =>
            CodexCaptureHookInputParser.Parse(JsonSerializer.Serialize(new
            {
                hook_event_name = "Stop",
                event_name = "UserPromptSubmit",
                session_id = "thread.current",
                cwd = "C:\\repo\\play",
                turn_id = "turn.current"
            })));
        Assert.Contains("conflicts", error.Message, StringComparison.Ordinal);
    }

    private static async Task<DbContextOptions<DantesRoleplayDbContext>> CreateDatabaseAsync(string database)
    {
        var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
            .UseSqlite("Filename=" + database).Options;
        await using var db = new DantesRoleplayDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var applications = new SqliteApplicationRegistry(db);
        var revision = applications.Register(new(
            ApplicationIdentifier.Parse("play-fixture"), "Play Fixture", "", []));
        new SqliteStateSpaceRegistry(db, applications).Create(
            new("play-fixture-space", revision, new string('A', 64)));
        return options;
    }

    private static Dictionary<string, string> Options(
        string root, string checkpoint, string thread, string gameplaySession) =>
        new(StringComparer.Ordinal)
        {
            ["principal"] = Principal,
            ["application"] = "play-fixture",
            ["state-space"] = "play-fixture-space",
            ["gameplay-session"] = gameplaySession,
            ["project"] = "project.fixture",
            ["repository"] = root,
            ["thread"] = thread,
            ["checkpoint"] = checkpoint
        };

    private static ToolContext Context(string database, Dictionary<string, string> common,
        bool connect = false, StringWriter? output = null, StringWriter? error = null)
    {
        var options = new Dictionary<string, string>(common, StringComparer.Ordinal);
        if (connect) options["connect"] = "";
        return new([], options, database, output ?? new StringWriter(), error ?? new StringWriter());
    }

    private static string Hook(string eventName, string thread, string root, string turn, string prompt = "")
    {
        var input = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["session_id"] = thread,
            ["transcript_path"] = Path.Combine(root, "must-not-be-opened.jsonl"),
            ["cwd"] = root,
            ["hook_event_name"] = eventName,
            ["model"] = "gpt-5.6-sol",
            ["permission_mode"] = "default",
            ["turn_id"] = turn
        };
        if (eventName == "UserPromptSubmit") input["prompt"] = prompt;
        if (eventName == "Stop")
        {
            input["stop_hook_active"] = false;
            input["last_assistant_message"] = "Answer.";
        }
        return JsonSerializer.Serialize(input);
    }

    private sealed class FailFirstCaptureClient(string threadId, string turnId) : ICodexThreadReadClient
    {
        public int ReadAttempts { get; private set; }

        public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CodexCaptureVersions.SupportedCliVersion);

        public Task<JsonElement> ReadThreadAsync(string requestedThreadId, bool includeTurns,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<JsonElement> ReadTurnAsync(string requestedThreadId, string requestedTurnId,
            CancellationToken cancellationToken = default)
        {
            ReadAttempts++;
            if (ReadAttempts == 1) throw new CodexBridgeException(
                "CODEX_PROCESS_EXITED", "Injected app-server interruption.");
            Assert.Equal(threadId, requestedThreadId);
            Assert.Equal(turnId, requestedTurnId);
            return Task.FromResult(JsonSerializer.SerializeToElement(new
            {
                thread = new
                {
                    id = threadId,
                    turns = new[]
                    {
                        new
                        {
                            id = turnId,
                            status = "completed",
                            items = new object[]
                            {
                                new { id = "item.user", type = "userMessage", content = new[] { new { type = "text", text = "Prompt." } }, visibility = "visible" },
                                new { id = "item.assistant", type = "agentMessage", text = "Answer.", phase = "final_answer", visibility = "visible" }
                            }
                        }
                    }
                }
            }));
        }
    }

    private sealed class BlockingCaptureClient(string threadId, string turnId) : ICodexThreadReadClient
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CodexCaptureVersions.SupportedCliVersion);

        public Task<JsonElement> ReadThreadAsync(string requestedThreadId, bool includeTurns,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async Task<JsonElement> ReadTurnAsync(string requestedThreadId, string requestedTurnId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(threadId, requestedThreadId);
            Assert.Equal(turnId, requestedTurnId);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return JsonSerializer.SerializeToElement(new
            {
                thread = new
                {
                    id = threadId,
                    turns = new[]
                    {
                        new
                        {
                            id = turnId,
                            status = "completed",
                            items = new object[]
                            {
                                new { id = "item.user.stop", type = "userMessage", content = new[] { new { type = "text", text = "Prompt." } }, visibility = "visible" },
                                new { id = "item.assistant.stop", type = "agentMessage", text = "Answer.", phase = "final_answer", visibility = "visible" }
                            }
                        }
                    }
                }
            });
        }
    }
}
