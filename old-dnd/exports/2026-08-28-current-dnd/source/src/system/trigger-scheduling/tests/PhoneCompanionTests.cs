using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.TriggerScheduling.Tests;

public sealed class PhoneCompanionTests : IDisposable
{
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("quest");
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 20, 0, 0, TimeSpan.Zero);
    private const string Device = "phone-device.0123456789abcdef0123456789abcdef";
    private const string OtherDevice = "phone-device.fedcba9876543210fedcba9876543210";
    private const string Credential = "phone-credential.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SignalId = "device.home-presence.signal";
    private const string SourceId = "phone.companion";
    private const string Schema = "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"state\":{\"type\":\"string\"}},\"required\":[\"state\"]}";
    private readonly SqliteFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task Registration_returns_the_secret_once_and_authentication_revocation_are_immediate()
    {
        await using var db = fixture.CreateContext();
        var setup = await SetupAsync(db, [Device]);
        var registry = new SqlitePhoneCompanionRegistry(db, setup.Clock,
            new SequenceCredentialGenerator(Credential));

        var registration = await registry.RegisterAsync(Request(Device));
        var authenticated = await new SqlitePhoneCompanionAuthenticator(db)
            .AuthenticateAsync(App, registration.Credential);
        var revoked = await registry.RevokeAsync(App, Device);
        var denied = await new SqlitePhoneCompanionAuthenticator(db)
            .AuthenticateAsync(App, registration.Credential);

        Assert.Equal(Credential, registration.Credential);
        Assert.Equal(PhoneCompanionStatus.Active, registration.Device.Status);
        Assert.Equal(PhoneCompanionIdentity.PrincipalId(App, Device), registration.Device.PrincipalId);
        Assert.DoesNotContain(Credential, db.PhoneCompanionDevices.Single().CredentialVerifier,
            StringComparison.Ordinal);
        Assert.Equal(PhoneCompanionIdentity.CredentialVerifier(Credential),
            db.PhoneCompanionDevices.Single().CredentialVerifier);
        Assert.True(authenticated.Allowed);
        Assert.Equal(PhoneCompanionIdentity.AuthenticationMethod,
            authenticated.Principal!.AuthenticationMethod);
        Assert.Equal(PhoneCompanionStatus.Revoked, revoked!.Status);
        Assert.Equal(2, revoked.StatusRevision);
        Assert.False(denied.Allowed);
        Assert.Equal(2, db.PhoneCompanionDeviceStatuses.Count());
    }

    [Fact]
    public async Task Phone_policy_preserves_normal_replay_window_schema_and_exact_device_binding()
    {
        await using var db = fixture.CreateContext();
        var setup = await SetupAsync(db, [Device]);
        var registry = new SqlitePhoneCompanionRegistry(db, setup.Clock,
            new SequenceCredentialGenerator(Credential));
        var registration = await registry.RegisterAsync(Request(Device));
        var principal = (await new SqlitePhoneCompanionAuthenticator(db)
            .AuthenticateAsync(App, registration.Credential)).Principal!;
        var ingestion = new SqliteObservationIngestionService(db, new BoundedJsonSchemaValidator(),
            new InMemoryTriggerObservationRateLimiter(setup.Clock), setup.Store,
            [new PhoneCompanionObservationIngestionPolicy(db)]);
        var submission = Submission('a', Device, "presence.entered", Now.AddMinutes(-5));

        var accepted = await ingestion.SubmitAsync(principal, App, submission);
        var replay = await ingestion.SubmitAsync(principal, App, submission);
        var wrongDevice = await Assert.ThrowsAsync<ObservationIngestionException>(() =>
            ingestion.SubmitAsync(principal, App,
                Submission('b', OtherDevice, "presence.other", Now.AddMinutes(-5))));
        var expired = await Assert.ThrowsAsync<TriggerSchedulingContractException>(() =>
            ingestion.SubmitAsync(principal, App,
                Submission('c', Device, "presence.old", Now.AddHours(-2))));

        Assert.Equal(TriggerSchedulingWriteDisposition.Appended, accepted.Disposition);
        Assert.Equal(TriggerSchedulingWriteDisposition.Replay, replay.Disposition);
        Assert.Equal(accepted.Value!.Id, replay.Value!.Id);
        Assert.Equal("PHONE_SUBMISSION_DENIED", wrongDevice.Code);
        Assert.Equal("OBSERVATION_TIME_EXPIRED", expired.Code);
        Assert.Single(db.TriggerObservations);
    }

    [Theory]
    [InlineData(ObservationDataClassification.General)]
    [InlineData(ObservationDataClassification.RawLocation)]
    [InlineData(ObservationDataClassification.ThirdPartyNotificationContent)]
    public async Task Registration_rejects_every_non_minimized_data_classification(
        ObservationDataClassification classification)
    {
        await using var db = fixture.CreateContext();
        var setup = await SetupAsync(db, [Device], classification);
        var registry = new SqlitePhoneCompanionRegistry(db, setup.Clock,
            new SequenceCredentialGenerator(Credential));

        var failure = await Assert.ThrowsAsync<TriggerSchedulingContractException>(() =>
            registry.RegisterAsync(Request(Device)));

        Assert.Equal("PHONE_STRUCTURE_PRIVACY_DENIED", failure.Code);
        Assert.Empty(db.PhoneCompanionDevices);
    }

    [Fact]
    public async Task Superseding_the_bound_source_invalidates_an_existing_credential_without_mutation()
    {
        await using var db = fixture.CreateContext();
        var setup = await SetupAsync(db, [Device]);
        var registry = new SqlitePhoneCompanionRegistry(db, setup.Clock,
            new SequenceCredentialGenerator(Credential));
        await registry.RegisterAsync(Request(Device));
        await setup.Store.AppendSourceAsync(Source(2, [Device]));

        var authentication = await new SqlitePhoneCompanionAuthenticator(db)
            .AuthenticateAsync(App, Credential);
        var view = await registry.GetAsync(App, Device);

        Assert.False(authentication.Allowed);
        Assert.Equal(PhoneCompanionStatus.StaleSource, view!.Status);
        Assert.Equal(1, view.StatusRevision);
    }

    [Fact]
    public async Task Credential_collisions_fail_closed_and_leave_no_partial_second_device()
    {
        await using var db = fixture.CreateContext();
        var setup = await SetupAsync(db, [Device, OtherDevice]);
        var registry = new SqlitePhoneCompanionRegistry(db, setup.Clock,
            new SequenceCredentialGenerator(Credential, Credential, Credential, Credential));
        await registry.RegisterAsync(Request(Device));

        var failure = await Assert.ThrowsAsync<TriggerSchedulingContractException>(() =>
            registry.RegisterAsync(Request(OtherDevice)));

        Assert.Equal("PHONE_CREDENTIAL_COLLISION", failure.Code);
        Assert.Single(db.PhoneCompanionDevices);
        Assert.Single(db.PhoneCompanionDeviceCurrent);
        Assert.Single(db.PhoneCompanionDeviceStatuses);
        Assert.Single(db.PhoneCompanionDeviceStructures);
    }

    [Fact]
    public async Task Migrated_database_guards_registration_status_and_current_pointer_from_tampering()
    {
        var path = Path.Combine(Path.GetTempPath(), $"phone-companion-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DantesRoleplayDbContext>()
                .UseSqlite($"Data Source={path}").Options;
            await using var db = new DantesRoleplayDbContext(options);
            await db.Database.MigrateAsync();
            var setup = await SetupAsync(db, [Device]);
            await new SqlitePhoneCompanionRegistry(db, setup.Clock,
                new SequenceCredentialGenerator(Credential)).RegisterAsync(Request(Device));
            db.ChangeTracker.Clear();

            var rewrite = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE trigger_phone_device SET SourceVersion = 2 WHERE ApplicationId = 'quest'"));
            var invalidStatus = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("""
                INSERT INTO trigger_phone_device_status
                    (ApplicationId, DeviceId, Revision, Status, RecordedAtUtc)
                VALUES ('quest', 'phone-device.0123456789abcdef0123456789abcdef', 2, 'active',
                    '2026-08-25 20:01:00');
                """));
            var deletePointer = await Assert.ThrowsAsync<SqliteException>(() =>
                db.Database.ExecuteSqlRawAsync("DELETE FROM trigger_phone_device_current WHERE ApplicationId = 'quest'"));

            Assert.Contains("TRIGGER_SCHEDULING_IMMUTABLE", rewrite.Message, StringComparison.Ordinal);
            Assert.Contains("TRIGGER_PHONE_DEVICE_STATUS_TRANSITION", invalidStatus.Message,
                StringComparison.Ordinal);
            Assert.Contains("TRIGGER_PHONE_DEVICE_CURRENT_DELETE", deletePointer.Message,
                StringComparison.Ordinal);
            Assert.Equal(PhoneCompanionStatus.Active,
                (await new SqlitePhoneCompanionRegistry(db, setup.Clock,
                    new SequenceCredentialGenerator(Credential)).GetAsync(App, Device))!.Status);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static async Task<Setup> SetupAsync(DantesRoleplayDbContext db,
        IReadOnlyList<string> devices,
        ObservationDataClassification classification = ObservationDataClassification.PrivacyMinimizedSignal)
    {
        new SqliteApplicationRegistry(db).Register(new ApplicationRegistration(App, "Quest", "Test.", []));
        var clock = new FakeTriggerClock(Now);
        var store = new SqliteTriggerSchedulingStore(db, clock);
        await store.AppendStructureAsync(ObservationStructureDefinition.Create(
            App, SignalId, 1, SystemJsonSchemaProfile.Version2Id, Schema, Hash(Schema),
            "A privacy-minimized presence transition.", dataClassification: classification));
        await store.AppendSourceAsync(Source(1, devices));
        return new(clock, store);
    }

    private static ObservationSourceDefinition Source(int version, IReadOnlyList<string> devices) =>
        ObservationSourceDefinition.Create(App, SourceId, version, ObservationSourceStatus.Enabled,
            [ObservationStructureReference.Create(SignalId, 1)],
            devices.Select(device => PhoneCompanionIdentity.PrincipalId(App, device)).ToArray(),
            TimeSpan.FromHours(1), 10);

    private static PhoneCompanionRegistrationRequest Request(string device) =>
        PhoneCompanionRegistrationRequest.Create(App, device, SourceId, 1,
            [PhoneCompanionStructurePermission.Create(SignalId, 1)]);

    private static ObservationSubmission Submission(char suffix, string device, string occurrence,
        DateTimeOffset observedAt) => ObservationSubmission.Create(
            "observation-request.0123456789abcdef0123456789abcde" + suffix,
            ObservationSourceReference.Create(SourceId, device, occurrence),
            ObservationStructureReference.Create(SignalId, 1), observedAt, "{\"state\":\"entered\"}");

    private static string Hash(string value) =>
        TriggerSchedulingFingerprint.Sha256(Encoding.UTF8.GetBytes(value));

    private sealed record Setup(FakeTriggerClock Clock, SqliteTriggerSchedulingStore Store);

    private sealed class SequenceCredentialGenerator(params string[] values)
        : IPhoneCompanionCredentialGenerator
    {
        private int index;
        public string Generate() => values[Math.Min(index++, values.Length - 1)];
    }
}
