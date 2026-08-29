using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Ecs;
using DantesRoleplay.Operations;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling.Tests;

public sealed class TriggerSchedulingAdministrationTests : IDisposable
{
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("quest");
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 20, 0, 0, TimeSpan.Zero);
    private const string Device = "phone-device.0123456789abcdef0123456789abcdef";
    private const string Credential = "phone-credential.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private readonly SqliteFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task Exact_preview_is_required_and_commit_receipt_replays_without_a_second_write()
    {
        await using var db = fixture.CreateContext();
        var service = Service(db);
        var command = OneTimeCommand('a');

        var preview = await service.PreviewAsync(command, Context());
        Assert.Empty(db.OneTimeTriggers);
        Assert.StartsWith("would-", preview.Outcome, StringComparison.Ordinal);

        var committed = await service.CommitAsync(command, Context());
        var replay = await service.CommitAsync(command, Context());

        Assert.Equal("registered", committed.Outcome);
        Assert.Equal("replay", replay.Outcome);
        Assert.Equal(command.RequestToken, committed.OperationId);
        Assert.Single(db.OneTimeTriggers);
        Assert.Single(db.Operations.Where(value => value.Id == command.RequestToken));
        Assert.Contains("system.trigger-scheduling|", db.Operations.Single(value =>
            value.Id == command.RequestToken).Subject, StringComparison.Ordinal);

        var unpreviewed = OneTimeCommand('b');
        var failure = await Assert.ThrowsAsync<TriggerSchedulingAdministrationException>(() =>
            service.CommitAsync(unpreviewed, Context()));
        Assert.Equal("DRY_RUN_REQUIRED", failure.Code);
        Assert.Single(db.OneTimeTriggers);
    }

    [Fact]
    public async Task Concurrent_exact_commits_leave_at_most_one_definition_and_audit_receipt()
    {
        var databasePath = Path.Combine(Path.GetTempPath(),
            $"dantesroleplay-trigger-administration-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False;Default Timeout=5").Options;
            var command = OneTimeCommand('0');
            await using (var setup = new DantesRoleplayDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                await setup.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
                await Build(setup).Service.PreviewAsync(command, Context());
            }

            await using var firstDb = new DantesRoleplayDbContext(options);
            await using var secondDb = new DantesRoleplayDbContext(options);
            var results = await Task.WhenAll(
                CaptureCommitAsync(Build(firstDb, registerApplication: false).Service, command),
                CaptureCommitAsync(Build(secondDb, registerApplication: false).Service, command));

            Assert.Contains(results, result => result.Result is not null);
            Assert.All(results.Where(result => result.Result is not null), result =>
                Assert.Contains(result.Result!.Outcome, new[] { "registered", "replay" }));
            await using var verification = new DantesRoleplayDbContext(options);
            Assert.Single(await verification.OneTimeTriggers.ToListAsync());
            Assert.Single(await verification.Operations.Where(value => value.Id == command.RequestToken)
                .ToListAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var target = databasePath + suffix;
                if (File.Exists(target)) File.Delete(target);
            }
        }
    }

    [Fact]
    public async Task Audit_failure_rolls_back_the_trigger_write_in_the_same_transaction()
    {
        await using var db = fixture.CreateContext();
        var audit = new FailingOperationLog(new OperationLog(db));
        var service = Service(db, audit);
        var command = OneTimeCommand('c');
        await service.PreviewAsync(command, Context());
        audit.Fail = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CommitAsync(command, Context()));

        Assert.Empty(db.OneTimeTriggers);
        Assert.Empty(db.OneTimeTriggerCurrent);
        Assert.DoesNotContain(db.Operations, value => value.Id == command.RequestToken);
    }

    [Fact]
    public async Task Phone_credential_is_returned_once_while_queries_omit_secrets_and_raw_observation_data()
    {
        await using var db = fixture.CreateContext();
        var service = Service(db);
        var principalView = await service.QueryAsync(TriggerSchedulingAdministrationQuery.Create(
            App, "phone-principal", Device));
        var principal = principalView.PhonePrincipal!.PrincipalId;

        await PreviewCommitAsync(service, Command('d', "structure.register", """
        {
          "id":"phone.arrival-signal","version":1,
          "normalizedSchema":"{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"state\":{\"type\":\"string\"}},\"required\":[\"state\"]}",
          "description":"A privacy-minimized arrival signal.","status":"active",
          "dataClassification":"privacy-minimized-signal"
        }
        """));
        await PreviewCommitAsync(service, Command('e', "source.register", $$"""
        {
          "id":"phone.companion","version":1,"status":"enabled",
          "structures":[{"id":"phone.arrival-signal","version":1}],
          "principalIds":["{{principal}}"],"replayWindowSeconds":3600,"requestsPerMinute":10
        }
        """));
        var phone = Command('f', "phone.register", $$"""
        {
          "deviceId":"{{Device}}","sourceId":"phone.companion","sourceVersion":1,
          "structures":[{"id":"phone.arrival-signal","version":1}]
        }
        """);
        var preview = await service.PreviewAsync(phone, Context());
        var commit = await service.CommitAsync(phone, Context());
        var replay = await service.CommitAsync(phone, Context());

        Assert.Null(preview.Credential);
        Assert.Equal(Credential, commit.Credential);
        Assert.Null(replay.Credential);
        Assert.DoesNotContain(Credential, db.PhoneCompanionDevices.Single().CredentialVerifier,
            StringComparison.Ordinal);

        var scheduling = new SqliteTriggerSchedulingStore(db, new FakeTriggerClock(Now));
        await scheduling.AppendObservationAsync(
            TrustedPrincipalContext.VerifiedPrincipal(principal, "test"), App,
            ObservationSubmission.Create("observation-request.0123456789abcdef0123456789abcdef",
                ObservationSourceReference.Create("phone.companion", Device, "arrival.1"),
                ObservationStructureReference.Create("phone.arrival-signal", 1), Now,
                "{\"state\":\"secret-raw-sentinel\"}"));
        var query = await service.QueryAsync(TriggerSchedulingAdministrationQuery.Create(App, "observations"));
        var json = JsonSerializer.Serialize(query);

        Assert.DoesNotContain("secret-raw-sentinel", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CredentialVerifier", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RequestFingerprint", json, StringComparison.OrdinalIgnoreCase);
        Assert.Single(query.Observations);

        var revoke = Command('9', "phone.revoke", $$"""{"deviceId":"{{Device}}"}""");
        await service.PreviewAsync(revoke, Context());
        var revoked = await service.CommitAsync(revoke, Context());
        Assert.Equal("revoked", revoked.Outcome);
        Assert.Equal(PhoneCompanionStatus.Revoked,
            (await service.QueryAsync(TriggerSchedulingAdministrationQuery.Create(App, "devices")))
                .Devices.Single().Status);
    }

    [Fact]
    public async Task Recurring_conditional_observation_and_lifecycle_revisions_use_the_closed_command_path()
    {
        await using var db = fixture.CreateContext();
        var setup = Build(db);
        var service = setup.Service;
        var revision = setup.Applications.Get(App)!;
        setup.StateSpaces.Create(new("quest-space", revision, new string('A', 64)));
        var type = setup.Types.Define(new(App, "quest.clock",
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"hour\":{\"type\":\"integer\"}},\"required\":[\"hour\"]}"));
        await setup.Components.CreateEntityAsync("quest-space", "world", "World clock");
        await setup.Components.AddComponentAsync(new("quest-space", "world",
            new(type.QualifiedId, type.Version, type.SchemaHash), "{\"hour\":20}", 0));

        await PreviewCommitAsync(service, Command('1', "recurring.register", """
        {
          "id":"session.nightly","version":1,"lifecycle":"active","misfirePolicy":"skip",
          "pattern":{"kind":"daily","interval":1,"localTime":"23:00:00","timeZoneId":"Europe/Stockholm",
            "startDate":null,"endDate":null,"weekdays":[],"dayOfMonth":null,
            "gapPolicy":"next-valid","overlapPolicy":"earlier"},
          "notification":{"topic":"scheduled.reminder","subject":"Nightly reminder","body":"Wrap up.",
            "stateSpaceId":null,"entityIds":[]}
        }
        """));
        await PreviewCommitAsync(service, Command('2', "conditional.register", $$"""
        {
          "id":"session.clock-threshold","version":1,"lifecycle":"active","kind":"world-clock-threshold",
          "activation":"rising-edge","rearm":"manual","stateSpaceId":"quest-space",
          "dependencies":[{"entityId":"world","qualifiedTypeId":"quest.clock","typeVersion":1,"schemaHash":"{{type.SchemaHash}}"}],
          "adapter":{"id":"system.trigger.closed-scalar","version":1},
          "adapterConfiguration":{"property":"hour","operator":"gte","value":23},
          "notification":{"topic":"scheduled.reminder","subject":"Clock threshold","body":"Wrap up.",
            "stateSpaceId":null,"entityIds":[]}
        }
        """));
        await PreviewCommitAsync(service, Command('3', "structure.register", """
        {
          "id":"session.signal","version":1,
          "normalizedSchema":"{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"state\":{\"type\":\"string\"}},\"required\":[\"state\"]}",
          "description":"A reviewed session signal.","status":"active","dataClassification":"general"
        }
        """));
        var structure = (await service.QueryAsync(TriggerSchedulingAdministrationQuery.Create(
            App, "structures", "session.signal"))).Structures.Single();
        var principal = PrivateOperatorPrincipal.Create("test", "operator").PrincipalId;
        await PreviewCommitAsync(service, Command('4', "source.register", $$"""
        {
          "id":"session.signal-source","version":1,"status":"enabled",
          "structures":[{"id":"session.signal","version":1}],"principalIds":["{{principal}}"],
          "replayWindowSeconds":3600,"requestsPerMinute":10
        }
        """));
        await PreviewCommitAsync(service, Command('5', "observation-trigger.register", $$"""
        {
          "id":"session.signal-match","version":1,"lifecycle":"active",
          "sourceId":"session.signal-source","sourceVersion":1,"structureId":"session.signal",
          "structureVersion":1,"structureHash":"{{structure.SchemaHash}}",
          "adapter":{"id":"system.trigger.observation.closed-scalars","version":1},
          "adapterConfiguration":{"matches":[{"property":"state","value":"ending"}]},
          "notification":{"topic":"scheduled.reminder","subject":"Session ending","body":"Wrap up.",
            "stateSpaceId":null,"entityIds":[]}
        }
        """));
        await PreviewCommitAsync(service, Command('6', "one-time.register", """
        {
          "id":"session.revision","version":1,"dueAtUtc":"2026-08-25T23:00:00Z",
          "misfirePolicy":"fire-once","lifecycle":"active",
          "notification":{"topic":"scheduled.reminder","subject":"Revision","body":"Active.","stateSpaceId":null,"entityIds":[]}
        }
        """));
        await PreviewCommitAsync(service, Command('7', "one-time.register", """
        {
          "id":"session.revision","version":2,"dueAtUtc":"2026-08-25T23:00:00Z",
          "misfirePolicy":"fire-once","lifecycle":"cancelled",
          "notification":{"topic":"scheduled.reminder","subject":"Revision","body":"Active.","stateSpaceId":null,"entityIds":[]}
        }
        """));

        var overview = await service.QueryAsync(TriggerSchedulingAdministrationQuery.Create(App));
        Assert.Single(overview.RecurringTriggers);
        Assert.Single(overview.ConditionalTriggers);
        Assert.Single(overview.ObservationTriggers);
        Assert.Contains(overview.OneTimeTriggers, value => value.TriggerId == "session.revision" &&
            value.TriggerVersion == 2 && value.Status == TriggerScheduleStatus.Cancelled);
        Assert.Equal(2, db.OneTimeTriggers.Count(value => value.Id == "session.revision"));
    }

    [Fact]
    public async Task Query_projects_notification_outcome_after_the_existing_worker_commits()
    {
        await using var db = fixture.CreateContext();
        var setup = Build(db);
        await PreviewCommitAsync(setup.Service, Command('8', "one-time.register", """
        {
          "id":"session.due-now","version":1,"dueAtUtc":"2026-08-25T20:00:00Z",
          "misfirePolicy":"fire-once","lifecycle":"active",
          "notification":{"topic":"scheduled.reminder","subject":"Due now","body":"Stop now.",
            "stateSpaceId":null,"entityIds":[]}
        }
        """));
        var worker = new SqliteOneTimeTriggerWorker(db, setup.Clock, setup.Scheduling,
            new TriggerNotificationTransactionParticipant(db, setup.Clock));

        var batch = await worker.RunBatchAsync("administration-test-worker");
        var overview = await setup.Service.QueryAsync(TriggerSchedulingAdministrationQuery.Create(App));

        Assert.Equal(1, batch.Completed);
        Assert.NotNull(Assert.Single(overview.OneTimeTriggers).NotificationId);
        Assert.NotNull(Assert.Single(overview.Fires).NotificationId);
    }

    [Fact]
    public void Command_parser_rejects_unknown_operations_and_extra_outer_fields()
    {
        var unknown = Assert.Throws<TriggerSchedulingAdministrationException>(() =>
            TriggerSchedulingAdministrationCommand.Parse("""
            {"requestToken":"0123456789abcdef0123456789abcdef","operation":"code.run","applicationId":"quest","value":{}}
            """));
        var extra = Assert.Throws<TriggerSchedulingAdministrationException>(() =>
            TriggerSchedulingAdministrationCommand.Parse("""
            {"requestToken":"0123456789abcdef0123456789abcdef","operation":"phone.revoke","applicationId":"quest","value":{"deviceId":"phone-device.0123456789abcdef0123456789abcdef"},"capability":"trigger.admin.write"}
            """));

        Assert.Equal("TRIGGER_ADMIN_OPERATION", unknown.Code);
        Assert.Equal("TRIGGER_ADMIN_PAYLOAD", extra.Code);
    }

    private static SqliteTriggerSchedulingAdministrationService Service(
        DantesRoleplayDbContext db, IOperationLog? audit = null) => Build(db, audit).Service;

    private static AdministrationSetup Build(DantesRoleplayDbContext db, IOperationLog? audit = null,
        bool registerApplication = true)
    {
        var applications = new SqliteApplicationRegistry(db);
        if (registerApplication)
            applications.Register(new ApplicationRegistration(App, "Quest", "Trigger administration tests.", []));
        var clock = new FakeTriggerClock(Now);
        var validator = new BoundedJsonSchemaValidator();
        var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
        var types = new SqliteComponentTypeRegistry(db, validator);
        var components = new SqliteEntityComponentStore(db, types, validator);
        var scheduling = new SqliteTriggerSchedulingStore(db, clock);
        var service = new SqliteTriggerSchedulingAdministrationService(db, scheduling,
            new SqliteConditionalTriggerStore(db, stateSpaces, types, components,
                [new ClosedScalarConditionalTriggerAdapter()], clock),
            new SqliteObservationTriggerStore(db, stateSpaces, components,
                [new ClosedScalarsObservationMatchAdapter()], clock),
            new SqlitePhoneCompanionRegistry(db, clock, new FixedCredentialGenerator()),
            new SqliteTriggerScheduleStatusReader(db, clock),
            new SqliteRecurringTriggerStatusReader(db, clock),
            new SqliteConditionalTriggerStatusReader(db),
            new SqliteObservationTriggerStatusReader(db), audit ?? new OperationLog(db));
        return new(service, applications, stateSpaces, types, components, clock, scheduling);
    }

    private static TriggerSchedulingAdministrationContext Context() => new(
        "Test trigger scheduling administration.", ["procedure.system.use"],
        new("principal." + new string('a', 64), "test", "trigger.admin.write",
            PrivateOperatorAuthorizationPolicy.PrivateHostScope, "test-request", true,
            "PRIVATE_OPERATOR_ALLOWED"));

    private static TriggerSchedulingAdministrationCommand OneTimeCommand(char suffix) => Command(suffix,
        "one-time.register", """
        {
          "id":"session.soft-ending","version":1,"dueAtUtc":"2026-08-25T23:00:00Z",
          "misfirePolicy":"fire-once","lifecycle":"active",
          "notification":{"topic":"scheduled.reminder","subject":"Time to stop",
            "body":"Softly end the session.","stateSpaceId":null,"entityIds":[]}
        }
        """);

    private static TriggerSchedulingAdministrationCommand Command(char suffix, string operation, string value) =>
        TriggerSchedulingAdministrationCommand.Create(
            $"0123456789abcdef0123456789abcde{suffix}", operation, App, value);

    private static async Task PreviewCommitAsync(SqliteTriggerSchedulingAdministrationService service,
        TriggerSchedulingAdministrationCommand command)
    {
        await service.PreviewAsync(command, Context());
        await service.CommitAsync(command, Context());
    }

    private static async Task<(TriggerSchedulingAdministrationResult? Result, Exception? Exception)>
        CaptureCommitAsync(SqliteTriggerSchedulingAdministrationService service,
            TriggerSchedulingAdministrationCommand command)
    {
        try { return (await service.CommitAsync(command, Context()), null); }
        catch (Exception exception) { return (null, exception); }
    }

    private sealed class FixedCredentialGenerator : IPhoneCompanionCredentialGenerator
    { public string Generate() => Credential; }

    private sealed class FailingOperationLog(IOperationLog inner) : IOperationLog
    {
        public bool Fail { get; set; }
        public Task<Operation?> GetAsync(string id, CancellationToken cancellationToken = default) =>
            inner.GetAsync(id, cancellationToken);
        public Task<Operation> RecordAsync(string tool, string summary, bool success, string intent = "",
            string subject = "", IEnumerable<string>? proceduresCited = null, string error = "",
            bool consumesReadEvidence = false, CancellationToken cancellationToken = default,
            string mechanicId = "", int? mechanicVersion = null, long? seed = null,
            string projectionJson = "", string guardEvidenceJson = "", string id = "") =>
            Fail && !string.IsNullOrWhiteSpace(id)
                ? throw new InvalidOperationException("simulated audit failure")
                : inner.RecordAsync(tool, summary, success, intent, subject, proceduresCited, error,
                    consumesReadEvidence, cancellationToken, mechanicId, mechanicVersion, seed,
                    projectionJson, guardEvidenceJson, id);
        public Task<IReadOnlyList<Operation>> RecentAsync(int limit = 20, bool failuresOnly = false,
            string? tool = null, string? subject = null, CancellationToken cancellationToken = default) =>
            inner.RecentAsync(limit, failuresOnly, tool, subject, cancellationToken);
        public Task<IReadOnlyList<string>> RecentlyReadProceduresAsync(
            CancellationToken cancellationToken = default) => inner.RecentlyReadProceduresAsync(cancellationToken);
    }

    private sealed record AdministrationSetup(
        SqliteTriggerSchedulingAdministrationService Service,
        SqliteApplicationRegistry Applications,
        SqliteStateSpaceRegistry StateSpaces,
        SqliteComponentTypeRegistry Types,
        SqliteEntityComponentStore Components,
        FakeTriggerClock Clock,
        SqliteTriggerSchedulingStore Scheduling);
}
