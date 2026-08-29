using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.TriggerScheduling;

namespace DantesRoleplay.TriggerScheduling.Tests;

public sealed class TriggerSchedulingContractsTests
{
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("quest");
    private static readonly TrustedPrincipalContext Principal = PrivateOperatorPrincipal.Create("test", "operator");
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Object_data_is_canonical_and_fingerprinted_independently_of_property_order()
    {
        var first = ObservationDataCanonicalizer.ParseObject("{\"z\":1,\"a\":[true,null]}");
        var second = ObservationDataCanonicalizer.ParseObject("{\"a\":[true,null],\"z\":1}");

        Assert.Equal("{\"a\":[true,null],\"z\":1}", first.Json);
        Assert.Equal(first.Json, second.Json);
        Assert.Equal(first.Hash, second.Hash);
        Assert.Equal(5, first.NodeCount);
        Assert.Equal(2, first.PropertyCount);
        Assert.Equal(2, first.ArrayItemCount);
    }

    [Theory]
    [InlineData("[]", "OBSERVATION_DATA_ROOT")]
    [InlineData("{\"a\":1,\"a\":2}", "OBSERVATION_DATA_DUPLICATE_PROPERTY")]
    [InlineData("{\"a\":1e100}", "OBSERVATION_DATA_NUMBER")]
    public void Data_rejects_invalid_object_or_number_shapes(string json, string code) =>
        AssertCode(code, () => ObservationDataCanonicalizer.ParseObject(json));

    [Fact]
    public void Data_rejects_all_closed_resource_bounds()
    {
        var tooManyProperties = "{" + string.Join(',', Enumerable.Range(0, 257).Select(i => $"\"p{i}\":0")) + "}";
        var tooManyArrayItems = "{\"items\":[" + string.Join(',', Enumerable.Repeat("0", 257)) + "]}";
        var tooLongString = "{\"text\":\"" + new string('x', TriggerSchedulingLimits.MaximumStringBytes + 1) + "\"}";
        var tooDeep = new string('{', TriggerSchedulingLimits.MaximumJsonDepth) +
            "\"leaf\":" + new string('{', TriggerSchedulingLimits.MaximumJsonDepth) + "0" +
            new string('}', TriggerSchedulingLimits.MaximumJsonDepth * 2);

        AssertCode("OBSERVATION_DATA_PROPERTIES", () => ObservationDataCanonicalizer.ParseObject(tooManyProperties));
        AssertCode("OBSERVATION_DATA_ARRAY_ITEMS", () => ObservationDataCanonicalizer.ParseObject(tooManyArrayItems));
        AssertCode("OBSERVATION_DATA_STRING", () => ObservationDataCanonicalizer.ParseObject(tooLongString));
        Assert.Contains(Assert.Throws<TriggerSchedulingContractException>(
            () => ObservationDataCanonicalizer.ParseObject(tooDeep)).Code,
            new[] { "OBSERVATION_DATA_INVALID_JSON", "OBSERVATION_DATA_DEPTH" });
    }

    [Fact]
    public void Observation_submission_requires_closed_ids_and_utc_time()
    {
        var source = ObservationSourceReference.Create("phone.dante", "android-primary", "arrival.1");
        var structure = ObservationStructureReference.Create("device.geofence.transition", 1);

        AssertCode("OBSERVATION_REQUEST_ID", () => ObservationSubmission.Create(
            "wrong", source, structure, Now, "{}"));
        AssertCode("OBSERVATION_TIME_NOT_UTC", () => ObservationSubmission.Create(
            RequestId(), source, structure, Now.ToOffset(TimeSpan.FromHours(2)), "{}"));
        AssertCode("TRIGGER_SCHEDULING_ID", () => ObservationSourceReference.Create(
            "Phone.Dante", "android-primary", "arrival.1"));
    }

    [Fact]
    public void Structure_requires_current_profile_closed_object_root_and_exact_hash()
    {
        const string schema = "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"transition\":{\"type\":\"string\"}}}";
        var hash = Hash(schema);

        var structure = ObservationStructureDefinition.Create(
            Application, "device.geofence.transition", 1, SystemJsonSchemaProfile.Version2Id,
            schema, hash, "A device geofence transition.");

        Assert.Equal(hash, structure.SchemaHash);
        AssertCode("OBSERVATION_STRUCTURE_HASH", () => ObservationStructureDefinition.Create(
            Application, "device.geofence.transition", 1, SystemJsonSchemaProfile.Version2Id,
            schema, new string('A', 64), "A device geofence transition."));
        AssertCode("OBSERVATION_STRUCTURE_ROOT", () => ObservationStructureDefinition.Create(
            Application, "device.geofence.transition", 1, SystemJsonSchemaProfile.Version2Id,
            "{\"type\":\"object\"}", Hash("{\"type\":\"object\"}"), "A device geofence transition."));
    }

    [Fact]
    public void Admission_is_deterministic_and_requires_enabled_exact_source_and_structure()
    {
        var source = Source();
        var structure = Structure();
        var submission = Submission(Now.AddMinutes(-1), "{\"transition\":\"entered\"}");
        var clock = new FakeTriggerClock(Now);

        var accepted = ObservationAdmissionEvaluator.Evaluate(Application, submission, source, structure, clock);
        var replay = ObservationAdmissionEvaluator.Evaluate(Application, submission, source, structure, clock);

        Assert.Equal(source.Version, accepted.SourceVersion);
        Assert.Equal(structure.SchemaHash, accepted.StructureHash);
        Assert.Equal(accepted.RequestFingerprint, replay.RequestFingerprint);

        var disabled = ObservationSourceDefinition.Create(
            Application, source.Id, source.Version, ObservationSourceStatus.Disabled,
            source.AllowedStructures, source.AllowedPrincipalIds, source.ReplayWindow, source.RequestsPerMinute);
        AssertCode("OBSERVATION_SOURCE_DISABLED", () =>
            ObservationAdmissionEvaluator.Evaluate(Application, submission, disabled, structure, clock));

        var forbidden = ObservationSourceDefinition.Create(
            Application, source.Id, source.Version, ObservationSourceStatus.Enabled,
            [ObservationStructureReference.Create("device.other.transition", 1)],
            source.AllowedPrincipalIds,
            source.ReplayWindow, source.RequestsPerMinute);
        AssertCode("OBSERVATION_STRUCTURE_FORBIDDEN", () =>
            ObservationAdmissionEvaluator.Evaluate(Application, submission, forbidden, structure, clock));
    }

    [Fact]
    public void Admission_enforces_future_and_replay_time_boundaries()
    {
        var source = Source(replayWindow: TimeSpan.FromMinutes(10));
        var structure = Structure();
        var clock = new FakeTriggerClock(Now);

        var exactFuture = Submission(Now.AddMinutes(TriggerSchedulingLimits.MaximumFutureSkewMinutes), "{}");
        Assert.NotNull(ObservationAdmissionEvaluator.Evaluate(Application, exactFuture, source, structure, clock));

        AssertCode("OBSERVATION_TIME_FUTURE", () => ObservationAdmissionEvaluator.Evaluate(
            Application, Submission(Now.AddMinutes(TriggerSchedulingLimits.MaximumFutureSkewMinutes).AddTicks(1), "{}"),
            source, structure, clock));
        AssertCode("OBSERVATION_TIME_EXPIRED", () => ObservationAdmissionEvaluator.Evaluate(
            Application, Submission(Now.AddMinutes(-10).AddTicks(-1), "{}"), source, structure, clock));
    }

    [Fact]
    public void Source_replay_window_requires_integral_seconds_inside_the_closed_bounds()
    {
        Assert.NotNull(Source(TimeSpan.FromSeconds(1)));
        Assert.NotNull(Source(TimeSpan.FromDays(TriggerSchedulingLimits.MaximumReplayWindowDays)));
        AssertCode("OBSERVATION_SOURCE_REPLAY_WINDOW", () => Source(TimeSpan.FromMilliseconds(500)));
        AssertCode("OBSERVATION_SOURCE_REPLAY_WINDOW", () => Source(TimeSpan.FromMilliseconds(1_500)));
        AssertCode("OBSERVATION_SOURCE_REPLAY_WINDOW", () => Source(
            TimeSpan.FromDays(TriggerSchedulingLimits.MaximumReplayWindowDays).Add(TimeSpan.FromSeconds(1))));
    }

    [Fact]
    public void Source_requires_distinct_opaque_principal_permissions()
    {
        var structure = ObservationStructureReference.Create("device.geofence.transition", 1);
        AssertCode("OBSERVATION_SOURCE_PRINCIPALS", () => ObservationSourceDefinition.Create(
            Application, "phone.dante", 1, ObservationSourceStatus.Enabled,
            [structure], [], TimeSpan.FromMinutes(1), 1));
        AssertCode("OBSERVATION_SOURCE_PRINCIPALS", () => ObservationSourceDefinition.Create(
            Application, "phone.dante", 1, ObservationSourceStatus.Enabled,
            [structure], ["operator"], TimeSpan.FromMinutes(1), 1));
        AssertCode("OBSERVATION_SOURCE_PRINCIPALS", () => ObservationSourceDefinition.Create(
            Application, "phone.dante", 1, ObservationSourceStatus.Enabled,
            [structure], [Principal.PrincipalId, Principal.PrincipalId], TimeSpan.FromMinutes(1), 1));
    }

    [Fact]
    public void Fake_clock_is_utc_and_moves_only_forwards()
    {
        var clock = new FakeTriggerClock(Now);
        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(Now.AddMinutes(5), clock.UtcNow);
        AssertCode("TRIGGER_CLOCK_REWIND", () => clock.Advance(TimeSpan.FromTicks(-1)));
        AssertCode("TRIGGER_CLOCK_NOT_UTC", () => new FakeTriggerClock(Now.ToOffset(TimeSpan.FromHours(1))));
    }

    [Fact]
    public void One_time_evaluation_is_deterministic_for_pending_due_and_misfire()
    {
        var due = Now.AddHours(1);
        var fireOnce = OneTimeTriggerDefinition.Create(
            Application, "trigger.session.soft-ending", 1, due, TriggerMisfirePolicy.FireOnce);
        var skip = OneTimeTriggerDefinition.Create(
            Application, "trigger.session.soft-ending", 1, due, TriggerMisfirePolicy.Skip);

        var pending = OneTimeTriggerEvaluator.Evaluate(fireOnce, new FakeTriggerClock(Now));
        var exact = OneTimeTriggerEvaluator.Evaluate(fireOnce, new FakeTriggerClock(due));
        var fireOnceLate = OneTimeTriggerEvaluator.Evaluate(fireOnce,
            new FakeTriggerClock(due.AddHours(TriggerSchedulingLimits.MaximumFireOnceLatenessHours)));
        var fireOnceMissed = OneTimeTriggerEvaluator.Evaluate(fireOnce,
            new FakeTriggerClock(due.AddHours(TriggerSchedulingLimits.MaximumFireOnceLatenessHours).AddTicks(1)));
        var skipLate = OneTimeTriggerEvaluator.Evaluate(skip, new FakeTriggerClock(due.AddTicks(1)));

        Assert.Equal(OneTimeTriggerDisposition.Pending, pending.Disposition);
        Assert.Equal(OneTimeTriggerDisposition.Due, exact.Disposition);
        Assert.Equal(OneTimeTriggerDisposition.Due, fireOnceLate.Disposition);
        Assert.Equal(OneTimeTriggerDisposition.Missed, fireOnceMissed.Disposition);
        Assert.Equal(OneTimeTriggerDisposition.Missed, skipLate.Disposition);
        Assert.Equal(exact.FireId, fireOnceLate.FireId);

        var newVersion = OneTimeTriggerDefinition.Create(
            Application, "trigger.session.soft-ending", 2, due, TriggerMisfirePolicy.FireOnce);
        Assert.NotEqual(exact.FireId, OneTimeTriggerEvaluator.Evaluate(newVersion, new FakeTriggerClock(due)).FireId);
    }

    [Fact]
    public void One_time_contract_rejects_non_notification_target()
    {
        AssertCode("TRIGGER_TARGET_UNSUPPORTED", () => OneTimeTriggerDefinition.Create(
            Application, "trigger.session.soft-ending", 1, Now,
            TriggerMisfirePolicy.Skip, (TriggerFireTarget)999));
    }

    private static ObservationSubmission Submission(DateTimeOffset observedAt, string data) =>
        ObservationSubmission.Create(
            RequestId(),
            ObservationSourceReference.Create("phone.dante", "android-primary", "arrival.1"),
            ObservationStructureReference.Create("device.geofence.transition", 1),
            observedAt,
            data);

    private static ObservationSourceDefinition Source(TimeSpan? replayWindow = null) =>
        ObservationSourceDefinition.Create(
            Application,
            "phone.dante",
            1,
            ObservationSourceStatus.Enabled,
            [ObservationStructureReference.Create("device.geofence.transition", 1)],
            [Principal.PrincipalId],
            replayWindow ?? TimeSpan.FromHours(1),
            10);

    private static ObservationStructureDefinition Structure()
    {
        const string schema = "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"transition\":{\"type\":\"string\"}}}";
        return ObservationStructureDefinition.Create(
            Application,
            "device.geofence.transition",
            1,
            SystemJsonSchemaProfile.Version2Id,
            schema,
            Hash(schema),
            "A device geofence transition.");
    }

    private static string RequestId() => "observation-request.0123456789abcdef0123456789abcdef";

    private static string Hash(string value) =>
        TriggerSchedulingFingerprint.Sha256(Encoding.UTF8.GetBytes(value));

    private static void AssertCode(string expected, Action action)
    {
        var exception = Assert.Throws<TriggerSchedulingContractException>(action);
        Assert.Equal(expected, exception.Code);
    }
}
