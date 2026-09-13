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
    public void Item_pages_require_the_exact_turn_and_an_object_item()
    {
        var selected = CodexCaptureAppServerClient.ReadScopedItem(
            Json("""{"turnId":"turn.1","item":{"id":"user.1","type":"userMessage","content":[]}}"""), "turn.1");
        Assert.Equal("user.1", selected.GetProperty("id").GetString());
        foreach (var invalid in new[]
        {
            """{"turnId":"turn.other","item":{"id":"user.1"}}""",
            """{"item":{"id":"user.1"}}""",
            """{"turnId":"turn.1","item":null}""",
            """{"turnId":"turn.1"}"""
        })
        {
            var error = Assert.Throws<CodexBridgeException>(() =>
                CodexCaptureAppServerClient.ReadScopedItem(Json(invalid), "turn.1"));
            Assert.Equal("CODEX_CAPTURE_PROTOCOL_INVALID", error.Code);
        }
    }

    [Fact]
    public async Task Item_paging_retries_the_same_cursor_with_smaller_pages_without_retaining_tool_bodies()
    {
        var requests = new List<(string? Cursor, int Limit)>();
        var source = new[]
        {
            Json("""{"turnId":"turn.1","item":{"id":"u1","type":"userMessage","content":[{"type":"text","text":"Player choice"}]}}"""),
            Json("""{"turnId":"turn.1","item":{"id":"tool.1","type":"mcpToolCall","result":"Private tool body"}}"""),
            Json("""{"turnId":"turn.1","item":{"id":"a1","type":"agentMessage","text":"Story outcome","phase":"final_answer"}}""")
        };
        var result = await CodexCaptureAppServerClient.ReadItemPagesAsync((cursor, limit, _) =>
        {
            requests.Add((cursor, limit));
            if (limit > 1) throw new CodexBridgeException("CODEX_PROTOCOL_OVERSIZE", "Bounded page too large.");
            var index = cursor is null ? 0 : int.Parse(cursor, System.Globalization.CultureInfo.InvariantCulture);
            return Task.FromResult(JsonSerializer.SerializeToElement(new
            {
                data = new[] { source[index] },
                nextCursor = index + 1 < source.Length ? (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : null
            }));
        }, "turn.1");

        Assert.Equal(new (string?, int)[] { (null, 4), (null, 2), (null, 1), ("1", 1), ("2", 1) }, requests);
        Assert.Equal(["u1", "a1"], result.Select(item => item.GetProperty("id").GetString()));
        Assert.DoesNotContain("Private tool body", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Item_paging_fails_when_one_item_exceeds_the_byte_cap_instead_of_skipping_it()
    {
        var requests = 0;
        var error = await Assert.ThrowsAsync<CodexBridgeException>(() =>
            CodexCaptureAppServerClient.ReadItemPagesAsync((_, _, _) =>
            {
                requests++;
                throw new CodexBridgeException("CODEX_PROTOCOL_OVERSIZE", "One item is too large.");
            }, "turn.1"));

        Assert.Equal("CODEX_PROTOCOL_OVERSIZE", error.Code);
        Assert.Equal(3, requests);
    }

    [Fact]
    public async Task Item_paging_bounds_all_source_items_even_when_they_are_not_messages()
    {
        var requests = 0;
        var error = await Assert.ThrowsAsync<CodexBridgeException>(() =>
            CodexCaptureAppServerClient.ReadItemPagesAsync((_, limit, _) =>
            {
                requests++;
                return Task.FromResult(JsonSerializer.SerializeToElement(new
                {
                    data = Enumerable.Range(0, limit).Select(index => new
                    {
                        turnId = "turn.1", item = new { id = $"tool.{requests}.{index}", type = "mcpToolCall" }
                    }).ToArray(),
                    nextCursor = requests.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
            }, "turn.1"));

        Assert.Equal("CODEX_CAPTURE_HISTORY_BOUNDED", error.Code);
        Assert.Equal(32, requests);
    }

    [Theory]
    [InlineData(CodexCaptureVersions.SupportedCliVersion)]
    [InlineData(CodexCaptureVersions.SupportedDesktopVersion)]
    public async Task Accepts_only_tested_versions_and_retains_the_observed_version(string version)
    {
        var client = new FakeReadClient(Json("""
            {"thread":{"id":"thread.gameplay","turns":[{"id":"turn.1","status":"completed","items":[
              {"type":"userMessage","id":"u1","clientId":null,"content":[{"type":"text","text":"I inspect the wagon."}]},
              {"type":"agentMessage","id":"a1","text":"Preparing the next scene.","phase":"commentary","delivery":null},
              {"type":"agentMessage","id":"a2","text":"Fresh mud clings to one wheel.","phase":"final_answer","memoryCitation":null,"delivery":null,"questions":[]}
            ]}]}}
            """), version);
        var adapter = Adapter(client, new());
        adapter.TryAcceptHook(Hook("Stop", "turn.1", ""), DateTimeOffset.UtcNow);

        var delivery = await adapter.CaptureNextAsync();

        Assert.Equal(version, delivery!.SourceVersion);
        Assert.Equal(["u1", "a2"], delivery.Turn.Messages.Select(message => message.ExternalMessageId));
        Assert.Equal(["I inspect the wagon.", "Fresh mud clings to one wheel."],
            delivery.Turn.Messages.Select(message => message.Content));
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
              {"id":"a3","type":"agentMessage","text":"hidden channel","channel":"analysis"},
              {"id":"a4","type":"agentMessage","text":"operational commentary","phase":"commentary"},
              {"id":"t1","type":"mcpToolCall","text":"tool dump","visibility":"visible"}
            ]}]}}
            """));
        var adapter = Adapter(client, new());
        adapter.TryAcceptHook(Hook("Stop", "turn.1", ""), DateTimeOffset.UtcNow);

        var turn = await adapter.CaptureNextAsync();

        Assert.Equal(["real user", "visible assistant"], turn!.Turn.Messages.Select(message => message.Content));
    }

    [Fact]
    public async Task Retains_user_and_final_messages_in_source_order_and_excludes_operational_commentary()
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

        Assert.Equal(["u1", "u2", "a2"], turn!.Turn.Messages.Select(message => message.ExternalMessageId));
        Assert.Equal(["First player message", "Second player message", "Final reply"],
            turn.Turn.Messages.Select(message => message.Content));
        Assert.Equal(["", "", "final"], turn.Turn.Messages.Select(message => message.Classification));
        Assert.Equal([0, 1, 2], turn.Turn.Messages.Select(message => message.Ordinal));
    }

    [Theory]
    [InlineData("futurePhase")]
    [InlineData("123")]
    public async Task Rejects_unknown_assistant_classification_without_acknowledging_the_turn(string phase)
    {
        var response = JsonSerializer.SerializeToElement(new
        {
            thread = new
            {
                id = "thread.gameplay",
                turns = new[] { new { id = "turn.1", status = "completed", items = new[]
                {
                    new { id = "a1", type = "agentMessage", text = "Unclassified output", phase }
                } } }
            }
        });
        var spool = new CodexCaptureCorrelationSpool();
        var adapter = Adapter(new FakeReadClient(response, CodexCaptureVersions.SupportedDesktopVersion), spool);
        adapter.TryAcceptHook(Hook("Stop", "turn.1", ""), DateTimeOffset.UtcNow);

        var error = await Assert.ThrowsAsync<CodexBridgeException>(() => adapter.CaptureNextAsync());

        Assert.Equal("CODEX_CAPTURE_PROTOCOL_INVALID", error.Code);
        Assert.Equal(1, spool.Count);
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
    public async Task Windows_checkpoint_replacement_waits_for_a_reader_then_atomically_replaces_the_file()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), "codex-capture-reader-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await CodexCaptureCheckpointFile.SaveAsync(path, new([], ["turn.old"], true));
            Task replacement;
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                replacement = CodexCaptureCheckpointFile.SaveAsync(path, new([], ["turn.new"], true));
                await Task.Delay(100);
                Assert.False(replacement.IsCompleted);
                Assert.Equal("turn.old", Assert.Single((await CodexCaptureCheckpointFile.LoadAsync(path)).CompletedTurnIds));
            }
            await replacement.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("turn.new", Assert.Single((await CodexCaptureCheckpointFile.LoadAsync(path)).CompletedTurnIds));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Windows_checkpoint_replacement_fails_boundedly_without_damaging_a_persistently_locked_file()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Path.GetTempPath(), "codex-capture-busy-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await CodexCaptureCheckpointFile.SaveAsync(path, new([], ["turn.old"], true));
            using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var error = await Assert.ThrowsAsync<CodexBridgeException>(() =>
                CodexCaptureCheckpointFile.SaveAsync(path, new([], ["turn.new"], true)).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("CODEX_CAPTURE_CHECKPOINT_BUSY", error.Code);
            Assert.Equal("turn.old", Assert.Single((await CodexCaptureCheckpointFile.LoadAsync(path)).CompletedTurnIds));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData("0.153.5")]
    [InlineData("0.154.0-alpha.6.3")]
    [InlineData("0.154.0")]
    public async Task Rejects_an_unsupported_cli_version_before_any_thread_read(string version)
    {
        var client = new FakeReadClient(Thread("thread.gameplay", "turn.1", "completed", "u", "a"), version);
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
