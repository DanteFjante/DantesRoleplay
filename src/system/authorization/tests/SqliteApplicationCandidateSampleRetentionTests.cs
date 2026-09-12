using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public async Task Validation_samples_are_retained_canonically_and_bound_to_replay_without_claiming_execution()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Validate, StandingGrantCapability.Read]);
        var service = Service(db, setup);
        _ = await service.WriteCandidateAsync(ApplicationHost(setup, "sample-write", InteractionExecutionProfile.Atomic),
            Request(setup.Activation.Current(Application)!.ActivationFingerprint));
        var row = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().ToArrayAsync());
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId, row.Revision, row.ContentFingerprint);
        var selected = await new ApplicationCandidateSelectionReader(db, setup.Applications, setup.Activation, setup.Resolver)
            .ReadAsync(ApplicationHost(setup), candidate);
        var definition = Selection(Assert.Single(selected!.Targets));
        var sample = new ApplicationCandidateValidationSample(definition, "{\"b\":2, \"a\":1}", "{\"result\":3}");
        var request = new ApplicationCandidateValidationRequest(candidate, [sample]);

        var first = await service.ValidateAsync(request, ApplicationHost(setup, "samples", InteractionExecutionProfile.Atomic));
        var replay = await service.ValidateAsync(request, ApplicationHost(setup, "samples", InteractionExecutionProfile.Atomic));
        var conflict = await service.ValidateAsync(request with { Samples = [sample with { ExpectedDataJson = "{\"result\":4}" }] },
            ApplicationHost(setup, "samples", InteractionExecutionProfile.Atomic));

        Assert.Equal(InteractionInvocationResultTag.Committed, first.Tag);
        Assert.Equal(first.Receipt, replay.Receipt);
        Assert.Equal("APPLICATION_CANDIDATE_COMMAND_CONFLICT", conflict.Code);
        var validation = Assert.Single(await db.Set<ApplicationCandidateValidationRecord>().AsNoTracking().ToArrayAsync());
        Assert.Equal("unavailable", validation.Outcome);
        Assert.False(validation.DependenciesComplete);
        Assert.Null(validation.PreparedEvidenceReference);
        var operation = await db.Operations.AsNoTracking().SingleAsync(value => value.Id == first.Receipt!.OperationId);
        using var command = JsonDocument.Parse(operation.ProjectionJson);
        var retained = Assert.Single(command.RootElement.GetProperty("samples").EnumerateArray());
        Assert.Equal("{\"a\":1,\"b\":2}", retained.GetProperty("InputJson").GetString());
        Assert.Equal("{\"result\":3}", retained.GetProperty("ExpectedDataJson").GetString());
        Assert.Equal(first.Receipt!.RequestFingerprint, validation.CanonicalCommandFingerprint);
        Assert.Equal(InteractionInvocationResultTag.Completed,
            (await service.InspectAsync(ApplicationHost(setup, "sample-inspect"), new(row.CandidateId, row.Revision))).Tag);
    }

    [Theory]
    [InlineData("per-definition")]
    [InlineData("total")]
    [InlineData("aggregate-bytes")]
    [InlineData("wrong-target")]
    [InlineData("input-array")]
    [InlineData("input-scalar")]
    [InlineData("input-null")]
    [InlineData("expected-array")]
    [InlineData("expected-scalar")]
    [InlineData("expected-null")]
    public async Task Invalid_sample_bounds_or_targets_do_not_retain_validation_operations(string invalid)
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Validate]);
        var service = Service(db, setup);
        _ = await service.WriteCandidateAsync(ApplicationHost(setup, "bounded-sample-write", InteractionExecutionProfile.Atomic),
            Request(setup.Activation.Current(Application)!.ActivationFingerprint));
        var row = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking().ToArrayAsync());
        var candidate = new ApplicationCandidateReference(Application, row.CandidateId, row.Revision, row.ContentFingerprint);
        var selected = await new ApplicationCandidateSelectionReader(db, setup.Applications, setup.Activation, setup.Resolver)
            .ReadAsync(ApplicationHost(setup), candidate);
        var sample = new ApplicationCandidateValidationSample(Selection(Assert.Single(selected!.Targets)), "{}", "{}");
        var samples = invalid switch
        {
            "per-definition" => Enumerable.Repeat(sample, 5).ToArray(),
            "total" => Enumerable.Repeat(sample, 17).ToArray(),
            "aggregate-bytes" => Enumerable.Repeat(sample with { InputJson = JsonSerializer.Serialize(new { value = new string('x', 17000) }) }, 4).ToArray(),
            "input-array" => [sample with { InputJson = "[]" }],
            "input-scalar" => [sample with { InputJson = "1" }],
            "input-null" => [sample with { InputJson = "null" }],
            "expected-array" => [sample with { ExpectedDataJson = "[]" }],
            "expected-scalar" => [sample with { ExpectedDataJson = "1" }],
            "expected-null" => [sample with { ExpectedDataJson = "null" }],
            _ => [sample with { Definition = sample.Definition with { ContentFingerprint = new string('A', 64) } }]
        };

        var rejected = await service.ValidateAsync(new ApplicationCandidateValidationRequest(candidate, samples),
            ApplicationHost(setup, "invalid-sample", InteractionExecutionProfile.Atomic));

        Assert.NotEqual(InteractionInvocationResultTag.Committed, rejected.Tag);
        Assert.Empty(await db.Set<ApplicationCandidateValidationRecord>().ToArrayAsync());
        Assert.Empty(await db.Operations.Where(value => value.Tool == "application-candidate-validation").ToArrayAsync());
    }

    [Fact]
    public async Task Legacy_target_typed_validation_call_stays_unambiguous_and_retains_missing_sample_diagnostic()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db); await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author, StandingGrantCapability.Validate]);
        var service = Service(db, setup);
        _ = await service.WriteCandidateAsync(ApplicationHost(setup, "legacy-sample-write", InteractionExecutionProfile.Atomic),
            Request(setup.Activation.Current(Application)!.ActivationFingerprint));
        var row = Assert.Single(await db.Set<ApplicationCandidateRevisionRecord>().ToArrayAsync());

        var result = await service.ValidateAsync(ApplicationHost(setup, "legacy-sample-validation", InteractionExecutionProfile.Atomic),
            new(Application, row.CandidateId, row.Revision, row.ContentFingerprint));

        Assert.Equal(InteractionInvocationResultTag.Committed, result.Tag);
        Assert.Contains("RUNTIME_SAMPLES_UNAVAILABLE", (await db.Set<ApplicationCandidateValidationRecord>().SingleAsync()).DiagnosticsJson);
    }
}
