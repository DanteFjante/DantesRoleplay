using System.Text.Json;
using DantesRoleplay.CodexBridge;
using DantesRoleplay.DataAccess;

namespace DantesRoleplay.Tests;

public sealed class CodexCaptureAdapterTests
{
    private const string Repository = "C:\\repo\\DantesRoleplay-gameplay-memory";

    [Fact]
    public void App_server_thread_metadata_requires_the_configured_repository()
    {
        CodexCaptureAppServerClient.ValidateThreadMetadata(
            Json("""{"thread":{"id":"thread.gameplay","cwd":"C:\\repo\\DantesRoleplay-gameplay-memory\\"}}"""),
            "thread.gameplay", Repository);

        var wrong = Assert.Throws<CodexBridgeException>(() => CodexCaptureAppServerClient.ValidateThreadMetadata(
            Json("""{"thread":{"id":"thread.gameplay","cwd":"C:\\repo\\engineering"}}"""),
            "thread.gameplay", Repository));
        Assert.Equal("CODEX_CAPTURE_THREAD_MISMATCH", wrong.Code);

        var missing = Assert.Throws<CodexBridgeException>(() => CodexCaptureAppServerClient.ValidateThreadMetadata(
            Json("""{"thread":{"id":"thread.gameplay"}}"""), "thread.gameplay", Repository));
        Assert.Equal("CODEX_CAPTURE_THREAD_MISMATCH", missing.Code);
    }

    [Fact]
    public async Task Reads_only_the_requested_completed_turn_with_real_visible_messages()
    {
        var client = new FakeReadClient(Thread("thread.gameplay", "turn.1", "completed", "User words", "Visible answer"));
        var spool = new CodexCaptureCorrelationSpool();
        var adapter = Adapter(client, spool);

        Assert.True(adapter.TryAcceptHook(Hook("UserPromptSubmit", "turn.1", "prompt metadata"), DateTimeOffset.UtcNow));
        var turn = await adapter.CaptureNextAsync();

        Assert.NotNull(turn);
        Assert.Equal("turn.1", turn!.Turn.TurnId);
        Assert.Equal(["user", "assistant"], turn.Turn.Messages.Select(message => message.Role));
        Assert.Equal(["User words", "Visible answer"], turn.Turn.Messages.Select(message => message.Content));
        Assert.Equal(1, client.ReadCalls);
        Assert.True(client.IncludeTurns);
        Assert.Equal("thread.gameplay", client.ThreadId);
    }

    [Fact]
    public async Task Excludes_reasoning_hidden_and_tool_items()
    {
        var client = new FakeReadClient(Json("""
            {"thread":{"id":"thread.gameplay","turns":[{"id":"turn.1","status":"completed","items":[
              {"id":"u1","type":"userMessage","content":[{"type":"text","text":"real user"}],"visibility":"visible"},
              {"id":"r1","type":"reasoning","text":"hidden reasoning"},
              {"id":"a1","type":"agentMessage","text":"visible assistant","visibility":"visible"},
              {"id":"a2","type":"agentMessage","text":"hidden assistant","visibility":"hidden"},
              {"id":"t1","type":"mcpToolCall","text":"tool dump","visibility":"visible"}
            ]}]}}
            """));
        var adapter = Adapter(client, new());
        adapter.TryAcceptHook(Hook("Stop", "turn.1", ""), DateTimeOffset.UtcNow);

        var turn = await adapter.CaptureNextAsync();

        Assert.Equal(["real user", "visible assistant"], turn!.Turn.Messages.Select(message => message.Content));
    }

    [Fact]
    public async Task Retains_every_visible_message_item_in_source_order_and_preserves_classification()
    {
        var client = new FakeReadClient(Json("""
            {"thread":{"id":"thread.gameplay","turns":[{"id":"turn.1","status":"completed","items":[
              {"id":"u1","type":"userMessage","content":[{"type":"text","text":"First player message"}],"visibility":"visible"},
              {"id":"a1","type":"agentMessage","text":"Working note","visibility":"visible","channel":"commentary"},
              {"id":"u2","type":"userMessage","content":[{"type":"text","text":"Second player message"}],"visibility":"visible"},
              {"id":"a2","type":"agentMessage","text":"Final reply","visibility":"visible","phase":"final_answer"}
            ]}]}}
            """));
        var adapter = Adapter(client, new());
        adapter.TryAcceptHook(Hook("Stop", "turn.1", ""), DateTimeOffset.UtcNow);

        var turn = await adapter.CaptureNextAsync();

        Assert.Equal(["u1", "a1", "u2", "a2"], turn!.Turn.Messages.Select(message => message.ExternalMessageId));
        Assert.Equal(["First player message", "Working note", "Second player message", "Final reply"],
            turn.Turn.Messages.Select(message => message.Content));
        Assert.Equal(["", "commentary", "", "final"], turn.Turn.Messages.Select(message => message.Classification));
        Assert.Equal([0, 1, 2, 3], turn.Turn.Messages.Select(message => message.Ordinal));
    }

    [Fact]
    public async Task Rejects_wrong_thread_wrong_turn_and_incomplete_turn_without_completing_checkpoint()
    {
        foreach (var response in new[]
        {
            Thread("thread.other", "turn.1", "completed", "u", "a"),
            Thread("thread.gameplay", "turn.other", "completed", "u", "a"),
            Thread("thread.gameplay", "turn.1", "inProgress", "u", "a")
        })
        {
            var spool = new CodexCaptureCorrelationSpool();
            var adapter = Adapter(new FakeReadClient(response), spool);
            adapter.TryAcceptHook(Hook("Stop", "turn.1", ""), DateTimeOffset.UtcNow);
            await Assert.ThrowsAsync<CodexBridgeException>(() => adapter.CaptureNextAsync());
            Assert.Equal(1, spool.Count); // failure stays resumable
        }
    }

    [Fact]
    public void Requires_exact_repository_and_linked_gameplay_session_and_never_uses_transcript_path()
    {
        var spool = new CodexCaptureCorrelationSpool();
        var adapter = Adapter(new FakeReadClient(Thread("thread.gameplay", "turn.1", "completed", "u", "a")), spool);

        Assert.False(adapter.TryAcceptHook(Hook("UserPromptSubmit", "turn.1", "p", session: "other"), DateTimeOffset.UtcNow));
        Assert.False(adapter.TryAcceptHook(Hook("UserPromptSubmit", "turn.1", "p", cwd: "C:\\repo\\other"), DateTimeOffset.UtcNow));
        Assert.False(adapter.TryAcceptHook(Hook("Other", "turn.1", "p"), DateTimeOffset.UtcNow));
        Assert.True(adapter.TryAcceptHook(Hook("UserPromptSubmit", "turn.1", "p", transcript: "C:\\secrets\\unrelated.jsonl"), DateTimeOffset.UtcNow));
        Assert.Equal(1, spool.Count);

        var noProject = new CodexCaptureAdapter(new FakeReadClient(Thread("thread.gameplay", "turn.2", "completed", "u", "a")),
            new(Repository, "", "session.gameplay", "thread.gameplay"), new());
        Assert.False(noProject.TryAcceptHook(Hook("Stop", "turn.2", ""), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Deduplicates_bounds_and_restores_a_bounded_checkpoint()
    {
        var spool = new CodexCaptureCorrelationSpool(maximumPending: 2, maximumCompleted: 2);
        Assert.True(spool.TryEnqueue(new("s", Repository, "t1", "", DateTimeOffset.UtcNow)));
        Assert.False(spool.TryEnqueue(new("s", Repository, "t1", "", DateTimeOffset.UtcNow)));
        Assert.True(spool.TryEnqueue(new("s", Repository, "t2", "", DateTimeOffset.UtcNow)));
        Assert.False(spool.TryEnqueue(new("s", Repository, "t3", "", DateTimeOffset.UtcNow)));
        Assert.True(spool.TryDequeue(out var first));
        spool.Complete(first!.TurnId);
        var restored = CodexCaptureCorrelationSpool.Restore(spool.Snapshot(), 2, 2);

        Assert.False(restored.TryEnqueue(new("s", Repository, "t1", "", DateTimeOffset.UtcNow)));
        Assert.True(restored.TryDequeue(out var resumed));
        Assert.Equal("t2", resumed!.TurnId);
    }

    [Fact]
    public void Checkpoint_retains_an_inflight_lease_and_new_prompt_and_rejects_overflow()
    {
        var now = DateTimeOffset.UtcNow;
        var spool = new CodexCaptureCorrelationSpool(maximumPending: 2, maximumCompleted: 2);
        Assert.True(spool.TryEnqueue(new("s", Repository, "leased", "", now)));
        Assert.True(spool.TryLease(out _));
        Assert.False(spool.TryEnqueue(new("s", Repository, "leased", "", now)));
        Assert.True(spool.TryEnqueue(new("s", Repository, "pending", "", now.AddSeconds(1))));
        Assert.False(spool.TryEnqueue(new("s", Repository, "overflow", "", now.AddSeconds(2))));

        var restored = CodexCaptureCorrelationSpool.Restore(spool.Snapshot(), 2, 2);
        var retained = new List<string>();
        while (restored.TryDequeue(out var correlation)) retained.Add(correlation!.TurnId);
        Assert.Equal(["leased", "pending"], retained.OrderBy(value => value));

        var invalid = new CodexCaptureSpoolCheckpoint(
            [new("s", Repository, "one", "", now), new("s", Repository, "two", "", now),
                new("s", Repository, "three", "", now)], []);
        var error = Assert.Throws<CodexBridgeException>(() =>
            CodexCaptureCorrelationSpool.Restore(invalid, 2, 2));
        Assert.Equal("CODEX_CAPTURE_CHECKPOINT_INVALID", error.Code);
    }

    [Fact]
    public async Task Checkpoint_file_round_trips_atomically_and_reports_corruption()
    {
        var path = Path.Combine(Path.GetTempPath(), "codex-capture-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var checkpoint = new CodexCaptureSpoolCheckpoint(
                [new("thread.gameplay", Repository, "turn.1", "", DateTimeOffset.UtcNow)],
                ["turn.completed"]);
            await CodexCaptureCheckpointFile.SaveAsync(path, checkpoint);

            var restored = await CodexCaptureCheckpointFile.LoadAsync(path);

            Assert.Equal("turn.1", Assert.Single(restored.Pending).TurnId);
            Assert.Equal("turn.completed", Assert.Single(restored.CompletedTurnIds));
            await File.WriteAllTextAsync(path, "{not-json");
            var error = await Assert.ThrowsAsync<CodexBridgeException>(() =>
                CodexCaptureCheckpointFile.LoadAsync(path));
            Assert.Equal("CODEX_CAPTURE_CHECKPOINT_INVALID", error.Code);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Rejects_an_unsupported_cli_version_before_any_thread_read()
    {
        var client = new FakeReadClient(Thread("thread.gameplay", "turn.1", "completed", "u", "a"), version: "0.153.5");
        var adapter = Adapter(client, new());
        adapter.TryAcceptHook(Hook("Stop", "turn.1", ""), DateTimeOffset.UtcNow);

        var error = await Assert.ThrowsAsync<CodexBridgeException>(() => adapter.CaptureNextAsync());

        Assert.Equal("CODEX_CAPTURE_VERSION_UNSUPPORTED", error.Code);
        Assert.Equal(0, client.ReadCalls);
    }

    [Fact]
    public async Task Does_not_complete_the_spool_until_journal_acknowledges_and_retries_after_failure()
    {
        var client = new FakeReadClient(Thread("thread.gameplay", "turn.1", "completed", "u", "a"));
        var spool = new CodexCaptureCorrelationSpool();
        var adapter = Adapter(client, spool);
        adapter.TryAcceptHook(Hook("Stop", "turn.1", ""), DateTimeOffset.UtcNow);

        var first = await adapter.CaptureNextAsync();
        Assert.Equal(1, spool.Count);
        adapter.Requeue(first!); // journal transaction failed; no durable acknowledgement occurred
        var retry = await adapter.CaptureNextAsync();
        adapter.Acknowledge(retry!);

        Assert.Equal(2, client.ReadCalls);
        Assert.Equal(0, spool.Count);
    }

    private static CodexCaptureAdapter Adapter(FakeReadClient client, CodexCaptureCorrelationSpool spool) => new(client,
        new(Repository, "project.caldris", "gameplay-context.7", "thread.gameplay"), spool);
    private static CodexCaptureHookInput Hook(string eventName, string turn, string prompt,
        string session = "thread.gameplay", string cwd = Repository, string transcript = "C:\\ignored.jsonl") =>
        new(eventName, session, cwd, transcript, turn, prompt);
    private static JsonElement Thread(string thread, string turn, string status, string user, string assistant) =>
        JsonSerializer.SerializeToElement(new
        {
            thread = new
            {
                id = thread,
                turns = new[]
                {
                    new
                    {
                        id = turn,
                        status,
                        items = new object[]
                        {
                            new { id = "user.1", type = "userMessage", content = new[] { new { type = "text", text = user } }, visibility = "visible" },
                            new { id = "assistant.1", type = "agentMessage", text = assistant, visibility = "visible" }
                        }
                    }
                }
            }
        });
    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    private sealed class FakeReadClient(JsonElement response, string version = CodexCaptureVersions.SupportedCliVersion) : ICodexThreadReadClient
    {
        public int ReadCalls { get; private set; }
        public bool IncludeTurns { get; private set; }
        public string ThreadId { get; private set; } = "";
        public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) => Task.FromResult(version);
        public Task<JsonElement> ReadThreadAsync(string threadId, bool includeTurns, CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            IncludeTurns = includeTurns;
            ThreadId = threadId;
            return Task.FromResult(response);
        }
    }
}
