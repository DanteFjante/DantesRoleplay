using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;

namespace DantesRoleplay.TriggerScheduling.Tests;

public sealed class ObservationIngestionTests : IDisposable
{
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("quest");
    private static readonly TrustedPrincipalContext Principal = PrivateOperatorPrincipal.Create("test", "operator");
    private static readonly TrustedPrincipalContext Other = PrivateOperatorPrincipal.Create("test", "other");
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 20, 0, 0, TimeSpan.Zero);
    private readonly SqliteFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task Ingestion_validates_exact_schema_and_principal_before_durable_append_or_replay()
    {
        await using var db = fixture.CreateContext();
        new SqliteApplicationRegistry(db).Register(new ApplicationRegistration(Application, "Quest", "Test.", []));
        var clock = new FakeTriggerClock(Now);
        var store = new SqliteTriggerSchedulingStore(db, clock);
        const string schema = "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"transition\":{\"type\":\"string\"}},\"required\":[\"transition\"]}";
        await store.AppendStructureAsync(ObservationStructureDefinition.Create(
            Application, "device.geofence.transition", 1, SystemJsonSchemaProfile.Version2Id,
            schema, Hash(schema), "A transition."));
        await store.AppendSourceAsync(ObservationSourceDefinition.Create(
            Application, "phone.dante", 1, ObservationSourceStatus.Enabled,
            [ObservationStructureReference.Create("device.geofence.transition", 1)],
            [Principal.PrincipalId], TimeSpan.FromHours(1), 10));
        var service = new SqliteObservationIngestionService(
            db, new BoundedJsonSchemaValidator(), new InMemoryTriggerObservationRateLimiter(clock), store);
        var accepted = Submission('a', "arrival.a", "{\"transition\":\"entered\"}");

        var appended = await service.SubmitAsync(Principal, Application, accepted);
        var replay = await service.SubmitAsync(Principal, Application, accepted);
        var invalid = await Assert.ThrowsAsync<ObservationIngestionException>(() =>
            service.SubmitAsync(Principal, Application, Submission('b', "arrival.b", "{\"unexpected\":true}")));
        var denied = await Assert.ThrowsAsync<ObservationIngestionException>(() =>
            service.SubmitAsync(Other, Application, Submission('c', "arrival.c", "{\"transition\":\"left\"}")));

        Assert.Equal(TriggerSchedulingWriteDisposition.Appended, appended.Disposition);
        Assert.Equal(TriggerSchedulingWriteDisposition.Replay, replay.Disposition);
        Assert.Equal(appended.Value!.Id, replay.Value!.Id);
        Assert.Equal(Principal.PrincipalId, appended.Value.PrincipalId);
        Assert.Equal("OBSERVATION_SCHEMA_INVALID", invalid.Code);
        Assert.Equal("OBSERVATION_PRINCIPAL_FORBIDDEN", denied.Code);
        Assert.Single(db.TriggerObservations);
    }

    [Fact]
    public async Task Rate_limiter_enforces_source_principal_and_concurrency_windows_without_queueing()
    {
        var clock = new FakeTriggerClock(Now);
        var limiter = new InMemoryTriggerObservationRateLimiter(clock);

        await Dispose(await limiter.TryAcquireAsync(Principal.PrincipalId, Application, "source.low", 2));
        await Dispose(await limiter.TryAcquireAsync(Principal.PrincipalId, Application, "source.low", 2));
        Assert.Null(await limiter.TryAcquireAsync(Principal.PrincipalId, Application, "source.low", 2));

        clock.Advance(TimeSpan.FromMinutes(1));
        var first = await limiter.TryAcquireAsync(Principal.PrincipalId, Application, "source.concurrent", 10);
        var second = await limiter.TryAcquireAsync(Principal.PrincipalId, Application, "source.concurrent", 10);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Null(await limiter.TryAcquireAsync(Principal.PrincipalId, Application, "source.concurrent", 10));
        await first!.DisposeAsync();
        await Dispose(await limiter.TryAcquireAsync(Principal.PrincipalId, Application, "source.concurrent", 10));
        await second!.DisposeAsync();

        clock.Advance(TimeSpan.FromMinutes(1));
        for (var index = 0; index < 10; index++)
            await Dispose(await limiter.TryAcquireAsync(Principal.PrincipalId, Application,
                index % 2 == 0 ? "source.alpha" : "source.beta", 10));
        Assert.Null(await limiter.TryAcquireAsync(Principal.PrincipalId, Application, "source.gamma", 10));
        clock.Advance(TimeSpan.FromMinutes(1));
        await Dispose(await limiter.TryAcquireAsync(Principal.PrincipalId, Application, "source.gamma", 10));
    }

    [Fact]
    public async Task Rate_limiter_capacity_recovers_after_expired_windows()
    {
        var clock = new FakeTriggerClock(Now);
        var limiter = new InMemoryTriggerObservationRateLimiter(clock);
        for (var index = 0; index < 2048; index++)
            await Dispose(await limiter.TryAcquireAsync($"principal-{index}", Application, "source", 1));

        Assert.Null(await limiter.TryAcquireAsync("principal-overflow", Application, "source", 1));

        clock.Advance(TimeSpan.FromMinutes(1));
        await Dispose(await limiter.TryAcquireAsync("principal-recovered", Application, "source", 1));
    }

    private static ObservationSubmission Submission(char suffix, string occurrence, string data) =>
        ObservationSubmission.Create(
            "observation-request.0123456789abcdef0123456789abcde" + suffix,
            ObservationSourceReference.Create("phone.dante", "android-primary", occurrence),
            ObservationStructureReference.Create("device.geofence.transition", 1),
            Now.AddMinutes(-1), data);

    private static string Hash(string value) =>
        TriggerSchedulingFingerprint.Sha256(Encoding.UTF8.GetBytes(value));

    private static async Task Dispose(ITriggerObservationRateLease? lease)
    {
        Assert.NotNull(lease);
        await lease!.DisposeAsync();
    }
}
