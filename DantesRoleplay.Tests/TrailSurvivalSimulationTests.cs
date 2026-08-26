using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.ApplicationPreview;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Ecs;
using DantesRoleplay.EcsEffects;
using DantesRoleplay.LocalAI;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Tests;

public sealed class TrailSurvivalSimulationTests
{
    [Fact]
    public async Task Scenario_contract_and_create_mechanic_are_closed_and_deterministic()
    {
        var root = RepositoryRoot();
        var validator = new BoundedJsonSchemaValidator();
        foreach (var relative in new[]
        {
            "components/scenario/trail-survival.scenario",
            "components/run/trail-survival.run"
        })
        {
            var stem = Path.Combine(root, "catalog", "applications", "trail-survival",
                relative.Replace('/', Path.DirectorySeparatorChar));
            var definition = ComponentDefinitionFile.Parse(
                await File.ReadAllTextAsync(stem + ".json"),
                relative + ".json",
                await File.ReadAllTextAsync(stem + ".schema.json"));
            var compilation = validator.Compile(definition.Schema);
            Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));
            var witness = relative.Contains("scenario", StringComparison.Ordinal)
                ? ScenarioJson()
                : "{\"phase\":\"travel\",\"turn\":0,\"partyId\":\"party.main\",\"randomSeed\":1234,\"seedCursor\":0}";
            Assert.Equal(SchemaValueStatus.Valid,
                validator.Validate(compilation.ProfileId, compilation.NormalizedSchema, witness).Status);
        }

        var mechanic = ReadMechanic("run/mechanic.trail-survival.run.create");
        Assert.Equal("mechanic.trail-survival.run.create", mechanic.Id);
        var projection = new MechanicProjection
        {
            Seed = 1234,
            Input = SetupInput(),
            Roles = new()
            {
                ["scenario"] = new("scenario.test", "Test scenario", new Dictionary<string, string>
                {
                    ["trail-survival.scenario"] = ScenarioJson()
                })
            }
        };
        var engine = new JintMechanicEngine();
        var first = await engine.RunAsync(mechanic.Source, projection, ExecutionLimits.Default);
        var second = await engine.RunAsync(mechanic.Source, projection, ExecutionLimits.Default);

        Assert.True(first.Ok, first.Error);
        Assert.Equal(JsonSerializer.Serialize(first.Output), JsonSerializer.Serialize(second.Output));
        Assert.Equal(19, first.Output.Effects.Count);
        Assert.Equal(5, first.Output.Effects.Count(value => value.Type == "entity.create"));
        Assert.Equal(10, first.Output.Effects.Count(value => value.Type == "component.add"));
        Assert.Equal(4, first.Output.Effects.Count(value => value.Type == "containment.move"));
        Assert.DoesNotContain(first.Output.Effects, value => value.DefinitionId is
            "trail-survival.pending-choice" or "trail-survival.outcome");

        var malformed = await engine.RunAsync(mechanic.Source, projection with
        {
            Input = "{\"runId\":\"run.main\",\"resolvedOutcome\":\"victory\"}"
        }, ExecutionLimits.Default);
        Assert.False(malformed.Ok);
        Assert.Contains("closed setup shape", malformed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Activated_create_action_commits_once_replays_and_rolls_back_a_late_collision()
    {
        await using (var harness = await SimulationHarness.CreateAsync())
        {
            var request = harness.Request(
                "mechanic.trail-survival.run.create",
                new Dictionary<string, string> { ["scenario"] = "scenario.test" },
                SetupInput(),
                1234,
                "0123456789abcdef0123456789abcdef");
            var first = await harness.Runner.RunAsync(request);
            var replay = await harness.Runner.RunAsync(request);

            Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, first.Disposition);
            Assert.Equal(19, first.AppliedEffectCount);
            Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
            Assert.Equal(first.OperationId, replay.OperationId);
            Assert.Equal(0, replay.AppliedEffectCount);
            var run = await harness.Entities.GetComponentAsync(
                SimulationHarness.StateSpaceId, "run.main", "trail-survival.run");
            Assert.NotNull(run);
            Assert.Equal(
                "{\"phase\":\"travel\",\"turn\":0,\"partyId\":\"party.main\",\"randomSeed\":1234,\"seedCursor\":0}",
                run.ValueJson);
            Assert.Equal(4, (await harness.Edges.ListContainmentsAsync(
                SimulationHarness.StateSpaceId)).Count);
            Assert.Equal("run.main", (await harness.Edges.GetContainmentAsync(
                SimulationHarness.StateSpaceId, "party.main"))!.ContainerEntityId);
            Assert.Equal("party.main", (await harness.Edges.GetContainmentAsync(
                SimulationHarness.StateSpaceId, "member.ada"))!.ContainerEntityId);
        }

        await using (var collision = await SimulationHarness.CreateAsync("member.bo"))
        {
            var result = await collision.Runner.RunAsync(collision.Request(
                "mechanic.trail-survival.run.create",
                new Dictionary<string, string> { ["scenario"] = "scenario.test" },
                SetupInput(),
                1234,
                "1123456789abcdef0123456789abcdef"));

            Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
            Assert.Null(await collision.Entities.GetEntityAsync(
                SimulationHarness.StateSpaceId, "run.main"));
            Assert.Null(await collision.Entities.GetEntityAsync(
                SimulationHarness.StateSpaceId, "party.main"));
            Assert.Null(await collision.Entities.GetEntityAsync(
                SimulationHarness.StateSpaceId, "conveyance.main"));
            Assert.Null(await collision.Entities.GetEntityAsync(
                SimulationHarness.StateSpaceId, "member.ada"));
            Assert.NotNull(await collision.Entities.GetEntityAsync(
                SimulationHarness.StateSpaceId, "member.bo"));
            Assert.Empty(await collision.Edges.ListContainmentsAsync(
                SimulationHarness.StateSpaceId));
        }
    }

    [Fact]
    public async Task Daily_commands_derive_trade_policy_rest_and_seeded_forage_from_state()
    {
        await using var harness = await SimulationHarness.CreateAsync();
        var setup = harness.Request(
            "mechanic.trail-survival.run.create",
            new Dictionary<string, string> { ["scenario"] = "scenario.test" },
            SetupInput(),
            1234,
            "3123456789abcdef0123456789abcdef");
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(setup)).Disposition);

        var runRole = new Dictionary<string, string> { ["run"] = "run.main" };
        var trade = harness.Request(
            "mechanic.trail-survival.trade",
            runRole,
            "{\"mode\":\"buy\",\"resourceId\":\"resource.food\",\"quantity\":2}",
            ActionSeed(1234, 0),
            "4123456789abcdef0123456789abcdef");
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(trade)).Disposition);
        var afterTrade = Json(await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "party.main", "trail-survival.resources"));
        Assert.Equal(96, Resource(afterTrade, "resource.coins"));
        Assert.Equal(22, Resource(afterTrade, "resource.food"));

        var policy = harness.Request(
            "mechanic.trail-survival.policy.set",
            runRole,
            "{\"paceId\":\"pace.fast\",\"rationId\":\"ration.sparse\"}",
            ActionSeed(1234, 1),
            "5123456789abcdef0123456789abcdef");
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(policy)).Disposition);
        Assert.Equal(
            "{\"paceId\":\"pace.fast\",\"rationId\":\"ration.sparse\"}",
            (await harness.Entities.GetComponentAsync(
                SimulationHarness.StateSpaceId, "run.main", "trail-survival.policy"))!.ValueJson);

        var rest = harness.Request(
            "mechanic.trail-survival.rest",
            runRole,
            "{}",
            ActionSeed(1234, 2),
            "6123456789abcdef0123456789abcdef");
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(rest)).Disposition);
        var afterRest = Json(await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "party.main", "trail-survival.resources"));
        Assert.Equal(20, Resource(afterRest, "resource.food"));
        Assert.Equal(120, Json(await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.clock"))
            .GetProperty("elapsedMinutes").GetInt32());

        var forage = harness.Request(
            "mechanic.trail-survival.forage",
            runRole,
            "{}",
            ActionSeed(1234, 3),
            "7123456789abcdef0123456789abcdef");
        var forageResult = await harness.Runner.RunAsync(forage);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, forageResult.Disposition);
        var afterForage = Json(await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "party.main", "trail-survival.resources"));
        Assert.InRange(Resource(afterForage, "resource.food"), 21, 24);
        Assert.Equal(210, Json(await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.clock"))
            .GetProperty("elapsedMinutes").GetInt32());
        var finalRun = Json(await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.run"));
        Assert.Equal(4, finalRun.GetProperty("turn").GetInt32());
        Assert.Equal(4, finalRun.GetProperty("seedCursor").GetInt32());

        var beforeInvalid = (await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.run"))!.ValueJson;
        var wrongSeed = await harness.Runner.RunAsync(harness.Request(
            "mechanic.trail-survival.rest",
            runRole,
            "{}",
            1,
            "8123456789abcdef0123456789abcdef"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, wrongSeed.Disposition);
        Assert.Equal(beforeInvalid, (await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.run"))!.ValueJson);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed,
            (await harness.Runner.RunAsync(trade)).Disposition);
        Assert.Equal(beforeInvalid, (await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.run"))!.ValueJson);
    }

    [Fact]
    public async Task Travel_opens_one_choice_blocks_other_commands_and_reaches_victory()
    {
        await using var harness = await SimulationHarness.CreateAsync();
        await harness.CreateRunAsync("9123456789abcdef0123456789abcdef");
        var runRole = new Dictionary<string, string> { ["run"] = "run.main" };
        var firstTravel = await harness.Runner.RunAsync(harness.Request(
            "mechanic.trail-survival.travel",
            runRole,
            "{\"legId\":\"leg.first\"}",
            ActionSeed(1234, 0),
            "a123456789abcdef0123456789abcdef"));
        Assert.True(firstTravel.Successful, string.Join("; ", firstTravel.Problems.Select(
            value => value.Code + ": " + value.SafeMessage)));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, firstTravel.Disposition);
        var progress = Json(await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.route-progress"));
        Assert.Equal("landmark.mid", progress.GetProperty("currentLandmarkId").GetString());
        Assert.Equal(JsonValueKind.Null, progress.GetProperty("activeLegId").ValueKind);
        var pending = Json(await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.pending-choice"));
        Assert.Equal("event.weather", pending.GetProperty("eventId").GetString());
        Assert.Equal(1, pending.GetProperty("openedTurn").GetInt32());

        var pendingRun = (await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.run"))!.ValueJson;
        var blocked = await harness.Runner.RunAsync(harness.Request(
            "mechanic.trail-survival.rest",
            runRole,
            "{}",
            ActionSeed(1234, 1),
            "b123456789abcdef0123456789abcdef"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, blocked.Disposition);
        Assert.Equal(pendingRun, (await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.run"))!.ValueJson);
        var invalidChoice = await harness.Runner.RunAsync(harness.Request(
            "mechanic.trail-survival.event.choose",
            runRole,
            "{\"choiceId\":\"choice.not-offered\"}",
            ActionSeed(1234, 1),
            "c123456789abcdef0123456789abcdef"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, invalidChoice.Disposition);
        Assert.NotNull(await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.pending-choice"));

        var choice = await harness.Runner.RunAsync(harness.Request(
            "mechanic.trail-survival.event.choose",
            runRole,
            "{\"choiceId\":\"choice.wait\"}",
            ActionSeed(1234, 1),
            "d123456789abcdef0123456789abcdef"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, choice.Disposition);
        Assert.Null(await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.pending-choice"));
        Assert.Equal(90, Json(await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.clock"))
            .GetProperty("elapsedMinutes").GetInt32());

        var finalTravel = await harness.Runner.RunAsync(harness.Request(
            "mechanic.trail-survival.travel",
            runRole,
            "{\"legId\":\"leg.last\"}",
            ActionSeed(1234, 2),
            "e123456789abcdef0123456789abcdef"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, finalTravel.Disposition);
        var outcome = Json(await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.outcome"));
        Assert.Equal("victory", outcome.GetProperty("kind").GetString());
        Assert.Equal("outcome.destination", outcome.GetProperty("causeId").GetString());
        Assert.Equal(3, outcome.GetProperty("reachedTurn").GetInt32());
        var terminalRun = (await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.run"))!.ValueJson;
        Assert.Equal("finished", JsonDocument.Parse(terminalRun).RootElement
            .GetProperty("phase").GetString());
        var terminalBlocked = await harness.Runner.RunAsync(harness.Request(
            "mechanic.trail-survival.forage",
            runRole,
            "{}",
            ActionSeed(1234, 3),
            "f123456789abcdef0123456789abcdef"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, terminalBlocked.Disposition);
        Assert.Equal(terminalRun, (await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.run"))!.ValueJson);
    }

    [Fact]
    public async Task Event_choice_can_derive_defeat_from_conveyance_state()
    {
        await using var harness = await SimulationHarness.CreateAsync(
            scenarioJson: ScenarioJson(2));
        await harness.CreateRunAsync("0123456789abcdef0123456789abcdea");
        var runRole = new Dictionary<string, string> { ["run"] = "run.main" };
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.Request(
                "mechanic.trail-survival.travel",
                runRole,
                "{\"legId\":\"leg.first\"}",
                ActionSeed(1234, 0),
                "1123456789abcdef0123456789abcdea"))).Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.Request(
                "mechanic.trail-survival.event.choose",
                runRole,
                "{\"choiceId\":\"choice.risk\"}",
                ActionSeed(1234, 1),
                "2123456789abcdef0123456789abcdea"))).Disposition);
        var outcome = Json(await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.outcome"));
        Assert.Equal("defeat", outcome.GetProperty("kind").GetString());
        Assert.Equal("outcome.conveyance-lost", outcome.GetProperty("causeId").GetString());
        Assert.Equal("lost", Json(await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "conveyance.main", "trail-survival.conveyance"))
            .GetProperty("status").GetString());
    }

    [Fact]
    public async Task Known_seed_loop_is_byte_stable_and_every_root_has_exact_audit_evidence()
    {
        await using var first = await SimulationHarness.CreateAsync();
        await using var second = await SimulationHarness.CreateAsync();
        var firstResults = await VictoryLoopAsync(first);
        var secondResults = await VictoryLoopAsync(second);

        Assert.Equal(await first.CanonicalSnapshotAsync(), await second.CanonicalSnapshotAsync());
        Assert.Equal(
            firstResults.Select(value => (value.QualifiedMechanicId, value.Seed, value.AppliedEffectCount)),
            secondResults.Select(value => (value.QualifiedMechanicId, value.Seed, value.AppliedEffectCount)));
        foreach (var result in firstResults)
        {
            var audit = await first.OperationAsync(result.OperationId);
            Assert.NotNull(audit);
            Assert.True(audit.Success);
            Assert.Equal(ApplicationEcsExecutionIdentity.AuditTool, audit.Tool);
            Assert.Equal(result.QualifiedMechanicId, audit.MechanicId);
            Assert.Equal(1, audit.MechanicVersion);
            Assert.Equal(result.Seed, audit.Seed);
            using var projection = JsonDocument.Parse(audit.ProjectionJson);
            Assert.Equal(JsonValueKind.Object, projection.RootElement.ValueKind);
            Assert.True(projection.RootElement.GetProperty("roles").EnumerateObject().Any());
        }

        await using var divergent = await SimulationHarness.CreateAsync();
        await divergent.CreateRunAsync(
            "3123456789abcdef0123456789abcdea",
            seed: 4321);
        Assert.NotEqual(await first.CanonicalSnapshotAsync(), await divergent.CanonicalSnapshotAsync());

        var auditProblems = ApplicationEcsEffectValidation.Validate(new ApplicationEcsEffectBatch
        {
            StateSpaceId = SimulationHarness.StateSpaceId,
            Effects = [],
            MechanicId = "fixture.mechanic"
        });
        Assert.Contains(auditProblems, value => value.Code == "MECHANIC_AUDIT_INVALID");
    }

    [Fact]
    public async Task Maximum_party_setup_commits_below_the_atomic_effect_ceiling()
    {
        await using var harness = await SimulationHarness.CreateAsync();
        var result = await harness.CreateRunAsync(
            "7123456789abcdef0123456789abcdea",
            setupInput: MaximumSetupInput());

        Assert.Equal(109, result.AppliedEffectCount);
        Assert.True(result.AppliedEffectCount < ApplicationEcsEffectValidation.MaximumEffects);
        Assert.Equal(36, (await harness.Entities.ListEntitiesAsync(
            SimulationHarness.StateSpaceId, null, 100)).Entities.Count);
        Assert.Equal(34, (await harness.Edges.ListContainmentsAsync(
            SimulationHarness.StateSpaceId)).Count);
        Assert.Equal(32, Json(await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "party.main", "trail-survival.party"))
            .GetProperty("memberIds").GetArrayLength());
    }

    [Fact]
    public async Task Unaffordable_and_over_capacity_trades_preserve_canonical_state()
    {
        await using (var unaffordable = await SimulationHarness.CreateAsync())
        {
            await unaffordable.CreateRunAsync("8123456789abcdef0123456789abcdea");
            var before = await unaffordable.CanonicalSnapshotAsync();
            var result = await unaffordable.Runner.RunAsync(unaffordable.Request(
                "mechanic.trail-survival.trade",
                new Dictionary<string, string> { ["run"] = "run.main" },
                "{\"mode\":\"buy\",\"resourceId\":\"resource.parts\",\"quantity\":11}",
                ActionSeed(1234, 0),
                "9123456789abcdef0123456789abcdea"));

            Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
            Assert.Equal(before, await unaffordable.CanonicalSnapshotAsync());
        }

        var roomyWallet = ScenarioVariant(root => ResourceNode(root, "resource.coins")["quantity"] = 1000);
        await using (var overCapacity = await SimulationHarness.CreateAsync(scenarioJson: roomyWallet))
        {
            await overCapacity.CreateRunAsync("a123456789abcdef0123456789abcdea");
            var before = await overCapacity.CanonicalSnapshotAsync();
            var result = await overCapacity.Runner.RunAsync(overCapacity.Request(
                "mechanic.trail-survival.trade",
                new Dictionary<string, string> { ["run"] = "run.main" },
                "{\"mode\":\"buy\",\"resourceId\":\"resource.food\",\"quantity\":176}",
                ActionSeed(1234, 0),
                "b123456789abcdef0123456789abcdea"));

            Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
            Assert.Equal(before, await overCapacity.CanonicalSnapshotAsync());
        }
    }

    [Fact]
    public async Task Partial_leg_requires_null_continuation_and_rejects_reselection_unchanged()
    {
        var longLeg = ScenarioVariant(root =>
        {
            root["routeLegs"]!.AsArray()[0]!.AsObject()["distance"] = 12;
            root["paces"]!.AsArray()[0]!.AsObject()["eventChancePer10000"] = 0;
        });
        await using var harness = await SimulationHarness.CreateAsync(scenarioJson: longLeg);
        await harness.CreateRunAsync("c123456789abcdef0123456789abcdea");
        var runRole = new Dictionary<string, string> { ["run"] = "run.main" };

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.Request(
                "mechanic.trail-survival.travel",
                runRole,
                "{\"legId\":\"leg.first\"}",
                ActionSeed(1234, 0),
                "d123456789abcdef0123456789abcdea"))).Disposition);
        var firstProgress = Json(await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.route-progress"));
        Assert.Equal("leg.first", firstProgress.GetProperty("activeLegId").GetString());
        Assert.Equal(5, firstProgress.GetProperty("distanceIntoLeg").GetInt32());

        var beforeWrongContinuation = await harness.CanonicalSnapshotAsync();
        var wrongContinuation = await harness.Runner.RunAsync(harness.Request(
            "mechanic.trail-survival.travel",
            runRole,
            "{\"legId\":\"leg.first\"}",
            ActionSeed(1234, 1),
            "e123456789abcdef0123456789abcdea"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, wrongContinuation.Disposition);
        Assert.Equal(beforeWrongContinuation, await harness.CanonicalSnapshotAsync());

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.Request(
                "mechanic.trail-survival.travel",
                runRole,
                "{\"legId\":null}",
                ActionSeed(1234, 1),
                "f123456789abcdef0123456789abcdea"))).Disposition);
        Assert.Equal(10, Json(await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.route-progress"))
            .GetProperty("distanceIntoLeg").GetInt32());
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.Request(
                "mechanic.trail-survival.travel",
                runRole,
                "{\"legId\":null}",
                ActionSeed(1234, 2),
                "0123456789abcdef0123456789abcdeb"))).Disposition);
        var arrived = Json(await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.route-progress"));
        Assert.Equal("landmark.mid", arrived.GetProperty("currentLandmarkId").GetString());
        Assert.Equal(JsonValueKind.Null, arrived.GetProperty("activeLegId").ValueKind);
        Assert.Equal(0, arrived.GetProperty("distanceIntoLeg").GetInt32());
    }

    [Fact]
    public async Task Unavailable_event_resource_cost_preserves_the_pending_choice()
    {
        var noParts = ScenarioVariant(root => ResourceNode(root, "resource.parts")["quantity"] = 0);
        await using var harness = await SimulationHarness.CreateAsync(scenarioJson: noParts);
        await harness.CreateRunAsync("1123456789abcdef0123456789abcdeb");
        var runRole = new Dictionary<string, string> { ["run"] = "run.main" };
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.Request(
                "mechanic.trail-survival.travel",
                runRole,
                "{\"legId\":\"leg.first\"}",
                ActionSeed(1234, 0),
                "2123456789abcdef0123456789abcdeb"))).Disposition);
        var before = await harness.CanonicalSnapshotAsync();

        var choice = await harness.Runner.RunAsync(harness.Request(
            "mechanic.trail-survival.event.choose",
            runRole,
            "{\"choiceId\":\"choice.risk\"}",
            ActionSeed(1234, 1),
            "3123456789abcdef0123456789abcdeb"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, choice.Disposition);
        Assert.Equal(before, await harness.CanonicalSnapshotAsync());
        var pending = Json(await harness.Entities.GetComponentAsync(
            SimulationHarness.StateSpaceId, "run.main", "trail-survival.pending-choice"));
        Assert.Equal("event.weather", pending.GetProperty("eventId").GetString());
        Assert.Contains("choice.risk", pending.GetProperty("choiceIds")
            .EnumerateArray().Select(value => value.GetString()));
    }

    private static MechanicFile ReadMechanic(string relative)
    {
        var stem = Path.Combine(RepositoryRoot(), "catalog", "applications", "trail-survival",
            "mechanics", relative.Replace('/', Path.DirectorySeparatorChar));
        return MechanicFile.Parse(
            File.ReadAllText(stem + ".md"),
            "applications/trail-survival/mechanics/" + relative + ".md",
            File.ReadAllText(stem + ".js"));
    }

    private static string SetupInput() => JsonSerializer.Serialize(new
    {
        runId = "run.main",
        partyId = "party.main",
        partyName = "Northbound Company",
        conveyanceId = "conveyance.main",
        members = new[]
        {
            new { entityId = "member.ada", name = "Ada", roleId = "role.navigator" },
            new { entityId = "member.bo", name = "Bo", roleId = "role.medic" }
        }
    });

    private static string MaximumSetupInput() => JsonSerializer.Serialize(new
    {
        runId = "run.main",
        partyId = "party.main",
        partyName = "Maximum Company",
        conveyanceId = "conveyance.main",
        members = Enumerable.Range(1, 32).Select(index => new
        {
            entityId = $"member.{index:00}",
            name = $"Member {index}",
            roleId = "role.traveler"
        }).ToArray()
    });

    private static string ScenarioJson(int conveyanceCondition = 20) => JsonSerializer.Serialize(new
    {
        scenarioId = "scenario.test",
        scenarioVersion = 1,
        scenarioContentHash = new string('A', 64),
        rulesProfileId = "rules.test",
        routeId = "route.test",
        startLandmarkId = "landmark.start",
        finalLandmarkId = "landmark.finish",
        currencyResourceId = "resource.coins",
        foodResourceId = "resource.food",
        initialResources = new[]
        {
            new { resourceId = "resource.coins", quantity = 100 },
            new { resourceId = "resource.food", quantity = 20 },
            new { resourceId = "resource.parts", quantity = 1 }
        },
        memberMaxHealth = 100,
        conveyance = new
        {
            kindId = "conveyance.cart", condition = conveyanceCondition, maximumCondition = 20, cargoCapacity = 200
        },
        defaultPolicy = new { paceId = "pace.steady", rationId = "ration.standard" },
        paces = new[]
        {
            new { paceId = "pace.steady", distancePerTurn = 5, minutesPerTurn = 60, conveyanceWear = 1, eventChancePer10000 = 10000 },
            new { paceId = "pace.fast", distancePerTurn = 10, minutesPerTurn = 45, conveyanceWear = 2, eventChancePer10000 = 0 }
        },
        rations = new[]
        {
            new { rationId = "ration.standard", foodPerMember = 1, healthDelta = 0 },
            new { rationId = "ration.sparse", foodPerMember = 0, healthDelta = -5 }
        },
        market = new
        {
            landmarkIds = new[] { "landmark.start", "landmark.mid" },
            offers = new[]
            {
                new { resourceId = "resource.food", buyPrice = 2, sellPrice = 1, unitWeight = 1 },
                new { resourceId = "resource.parts", buyPrice = 10, sellPrice = 5, unitWeight = 5 },
                new { resourceId = "resource.coins", buyPrice = 1, sellPrice = 1, unitWeight = 0 }
            }
        },
        rest = new { minutes = 120, foodPerMember = 1, healthGain = 10 },
        forage = new { minutes = 90, minimumYield = 1, maximumYield = 4 },
        routeLegs = new[]
        {
            new { legId = "leg.first", fromLandmarkId = "landmark.start", toLandmarkId = "landmark.mid", distance = 5 },
            new { legId = "leg.last", fromLandmarkId = "landmark.mid", toLandmarkId = "landmark.finish", distance = 5 }
        },
        events = new[]
        {
            new
            {
                eventId = "event.weather",
                weight = 1,
                choices = new object[]
                {
                    new
                    {
                        choiceId = "choice.wait",
                        resourceDeltas = Array.Empty<object>(),
                        healthDelta = 0,
                        conveyanceDelta = 0,
                        elapsedMinutes = 30,
                        outcomeKind = "none",
                        outcomeCauseId = (string?)null
                    },
                    new
                    {
                        choiceId = "choice.risk",
                        resourceDeltas = new[] { new { resourceId = "resource.parts", quantity = -1 } },
                        healthDelta = -10,
                        conveyanceDelta = -2,
                        elapsedMinutes = 0,
                        outcomeKind = "none",
                        outcomeCauseId = (string?)null
                    }
                }
            }
        },
        outcomes = new
        {
            victoryCauseId = "outcome.destination",
            partyDefeatCauseId = "outcome.party-lost",
            conveyanceDefeatCauseId = "outcome.conveyance-lost"
        }
    });

    private static string ScenarioVariant(Action<JsonObject> mutate)
    {
        var root = JsonNode.Parse(ScenarioJson())!.AsObject();
        mutate(root);
        return root.ToJsonString();
    }

    private static JsonObject ResourceNode(JsonObject scenario, string resourceId) => scenario
        ["initialResources"]!.AsArray()
        .Select(value => value!.AsObject())
        .Single(value => value["resourceId"]!.GetValue<string>() == resourceId);

    private static long ActionSeed(uint randomSeed, int cursor) =>
        unchecked(randomSeed ^ ((uint)(cursor + 1) * 2654435761u));

    private static async Task<IReadOnlyList<ApplicationActionExecutionResult>> VictoryLoopAsync(
        SimulationHarness harness)
    {
        var results = new List<ApplicationActionExecutionResult>();
        var runRole = new Dictionary<string, string> { ["run"] = "run.main" };
        results.Add(await harness.CreateRunAsync("3123456789abcdef0123456789abcdef"));
        results.Add(await harness.Runner.RunAsync(harness.Request(
            "mechanic.trail-survival.travel",
            runRole,
            "{\"legId\":\"leg.first\"}",
            ActionSeed(1234, 0),
            "4123456789abcdef0123456789abcdef")));
        results.Add(await harness.Runner.RunAsync(harness.Request(
            "mechanic.trail-survival.event.choose",
            runRole,
            "{\"choiceId\":\"choice.wait\"}",
            ActionSeed(1234, 1),
            "5123456789abcdef0123456789abcdef")));
        results.Add(await harness.Runner.RunAsync(harness.Request(
            "mechanic.trail-survival.travel",
            runRole,
            "{\"legId\":\"leg.last\"}",
            ActionSeed(1234, 2),
            "6123456789abcdef0123456789abcdef")));
        Assert.All(results, value => Assert.Equal(
            ApplicationActionExecutionDisposition.Succeeded, value.Disposition));
        return results;
    }

    private static JsonElement Json(EcsComponentView? component)
    {
        Assert.NotNull(component);
        return JsonDocument.Parse(component.ValueJson).RootElement.Clone();
    }

    private static int Resource(JsonElement resources, string resourceId) => resources
        .GetProperty("entries")
        .EnumerateArray()
        .Single(value => value.GetProperty("resourceId").GetString() == resourceId)
        .GetProperty("quantity")
        .GetInt32();

    private static string RepositoryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("DANTES_ROLEPLAY_TEST_REPOSITORY_ROOT");
        foreach (var start in new[] { configured, Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
                     .Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>())
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "DantesRoleplay.slnx")))
                    return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not find the repository root.");
    }

    private sealed class SimulationHarness : IAsyncDisposable
    {
        public const string StateSpaceId = "trail-simulation";
        private static readonly ApplicationIdentifier Application =
            ApplicationIdentifier.Parse("trail-survival");
        private readonly SqliteFixture _fixture;
        private readonly DantesRoleplayDbContext _db;
        private readonly ActivatedApplicationCatalogProvider _catalogs;

        private SimulationHarness(
            SqliteFixture fixture,
            DantesRoleplayDbContext db,
            ActivatedApplicationCatalogProvider catalogs,
            SqliteEntityComponentStore entities,
            SqliteStateSpaceEdgeStore edges,
            ApplicationActionRunner runner)
        {
            _fixture = fixture;
            _db = db;
            _catalogs = catalogs;
            Entities = entities;
            Edges = edges;
            Runner = runner;
        }

        public SqliteEntityComponentStore Entities { get; }
        public SqliteStateSpaceEdgeStore Edges { get; }
        public ApplicationActionRunner Runner { get; }

        public static async Task<SimulationHarness> CreateAsync(
            string? collidingEntityId = null,
            string? scenarioJson = null)
        {
            var fixture = new SqliteFixture();
            var db = fixture.CreateContext();
            var applications = new SqliteApplicationRegistry(db);
            var revision = applications.Register(new(
                Application,
                "Trail Survival",
                "Original customizable single-player trail-survival application.",
                []));
            var sources = new SqliteSourceRegistry(db);
            sources.Register(new(
                Application,
                "trail-survival-core",
                "workspace",
                "catalog/applications/trail-survival/**/*",
                SourceTrust.Trusted,
                0,
                "trail-survival-core-catalog"));
            var roots = new WorkspaceRoot();
            var preview = new ApplicationPreviewService(
                applications,
                sources,
                new RegisteredSourceScanner(sources, roots, new LocalDocumentScanner()),
                new SourceOverlayResolver());
            var previewResult = await preview.PreviewAsync(Application);
            Assert.True(previewResult.IsValid, string.Join("; ", previewResult.Problems.Select(value => value.Code)));
            var operations = new OperationLog(db);
            var activations = new ApplicationActivationService(
                db,
                preview,
                new EmptyImpact(),
                operations);
            var activationRequest = new ApplicationActivationRequest(
                Application, previewResult.PreviewFingerprint, null);
            var activationContext = ActivationContext();
            Assert.Equal("would-activate", (await activations.PreviewAsync(
                activationRequest, activationContext)).Outcome);
            var activation = await activations.ActivateAsync(
                activationRequest,
                activationContext);
            var stateSpaces = new SqliteStateSpaceRegistry(db, applications);
            stateSpaces.Create(new(StateSpaceId, revision, activation.Activation.ActivationFingerprint));
            var schemas = new BoundedJsonSchemaValidator();
            var types = new SqliteComponentTypeRegistry(db, schemas);
            var registered = new Dictionary<string, RegisteredComponentTypeVersion>(StringComparer.Ordinal);
            var componentRoot = Path.Combine(RepositoryRoot(), "catalog", "applications",
                "trail-survival", "components");
            foreach (var metadataPath in Directory.EnumerateFiles(
                         componentRoot, "*.json", SearchOption.AllDirectories)
                     .Where(value => !value.EndsWith(".schema.json", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
            {
                var definition = ComponentDefinitionFile.Parse(
                    await File.ReadAllTextAsync(metadataPath),
                    Path.GetRelativePath(RepositoryRoot(), metadataPath),
                    await File.ReadAllTextAsync(Path.ChangeExtension(metadataPath, ".schema.json")));
                registered[definition.Id] = types.Define(new(Application, definition.Id, definition.Schema));
            }
            var entities = new SqliteEntityComponentStore(db, types, schemas);
            var edges = new SqliteStateSpaceEdgeStore(db, stateSpaces);
            await entities.CreateEntityAsync(StateSpaceId, "scenario.test", "Test scenario");
            var scenarioType = registered["trail-survival.scenario"];
            await entities.AddComponentAsync(new(
                StateSpaceId,
                "scenario.test",
                new(scenarioType.QualifiedId, scenarioType.Version, scenarioType.SchemaHash),
                scenarioJson ?? ScenarioJson(),
                0));
            if (collidingEntityId is not null)
                await entities.CreateEntityAsync(StateSpaceId, collidingEntityId, "Collision witness");

            var materializer = new ActivatedApplicationCatalogMaterializer(
                applications, activations, sources, roots);
            var catalogs = new ActivatedApplicationCatalogProvider(
                new ConfiguredPublicApplicationCatalogPolicy([Application.Value]),
                materializer,
                new CatalogCursorCodec(Encoding.UTF8.GetBytes(
                    "trail-survival-simulation-cursor-key")));
            var evaluator = new ApplicationMechanicEvaluator(
                catalogs,
                new ApplicationMechanicProjectionResolver(db, stateSpaces),
                new JintMechanicEngine());
            var applier = new ApplicationEcsEffectApplier(db, entities, stateSpaces, operations, edges);
            var runner = new ApplicationActionRunner(
                catalogs, activations, stateSpaces, types, entities, edges, evaluator, applier, operations);
            return new(fixture, db, catalogs, entities, edges, runner);
        }

        public ApplicationActionExecutionRequest Request(
            string localMechanicId,
            IReadOnlyDictionary<string, string> roles,
            string input,
            long seed,
            string operationId)
        {
            Assert.True(_catalogs.TryGet(Application, out var catalog));
            var qualified = Application.Value + "." + localMechanicId;
            var record = catalog.Inspect(new(Application, Application.Value, qualified));
            var subject = qualified + "\n" + input + "\n" + seed + "\n" +
                string.Join(';', roles.OrderBy(value => value.Key));
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject)));
            return new(
                StateSpaceId,
                Application,
                qualified,
                record.Summary.ContentFingerprint,
                roles,
                input,
                seed,
                new(operationId, fingerprint));
        }

        public async Task<ApplicationActionExecutionResult> CreateRunAsync(
            string operationId,
            long seed = 1234,
            string? setupInput = null)
        {
            var result = await Runner.RunAsync(Request(
                "mechanic.trail-survival.run.create",
                new Dictionary<string, string> { ["scenario"] = "scenario.test" },
                setupInput ?? SetupInput(),
                seed,
                operationId));
            Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, result.Disposition);
            return result;
        }

        public Task<Operation?> OperationAsync(string operationId) =>
            new OperationLog(_db).GetAsync(operationId);

        public async Task<string> CanonicalSnapshotAsync()
        {
            var entityPage = await Entities.ListEntitiesAsync(StateSpaceId, null, 100);
            var entities = new List<object>();
            foreach (var entity in entityPage.Entities.OrderBy(value => value.EntityId, StringComparer.Ordinal))
            {
                var componentPage = await Entities.ListComponentsAsync(
                    StateSpaceId, entity.EntityId, null, 100);
                entities.Add(new
                {
                    entity.EntityId,
                    entity.Name,
                    entity.Revision,
                    Components = componentPage.Components
                        .OrderBy(value => value.Type.QualifiedTypeId, StringComparer.Ordinal)
                        .Select(value => new
                        {
                            value.Type.QualifiedTypeId,
                            value.Type.TypeVersion,
                            value.Type.SchemaHash,
                            value.ValueJson,
                            value.Revision
                        }).ToArray()
                });
            }
            var containments = (await Edges.ListContainmentsAsync(StateSpaceId))
                .OrderBy(value => value.ContainedEntityId, StringComparer.Ordinal)
                .Select(value => new
                {
                    value.ContainedEntityId,
                    value.ContainerEntityId,
                    value.Slot,
                    value.Revision
                }).ToArray();
            return JsonSerializer.Serialize(new { Entities = entities, Containments = containments });
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            _fixture.Dispose();
        }

        private static ApplicationActivationContext ActivationContext() => new(
            "2123456789abcdef0123456789abcdef",
            "Activate exact Trail Survival simulation source in disposable test state.",
            ["procedure.system.use"],
            new AuthorizationAuditEvidence(
                "principal." + new string('a', 64),
                "test",
                "modify",
                "system.private-host",
                "trail-survival-simulation",
                true,
                "PRIVATE_OPERATOR_ALLOWED"));

        private sealed class WorkspaceRoot : IAllowedSourceRootResolver
        {
            public bool TryResolve(string allowedRootId, out string canonicalPath)
            {
                canonicalPath = allowedRootId == "workspace" ? RepositoryRoot() : "";
                return canonicalPath.Length > 0;
            }
        }

        private sealed class EmptyImpact : IProjectionImpactService
        {
            public ProjectionImpactReport Analyze(
                ApplicationIdentifier applicationId,
                string? rootId = null,
                bool transitive = true) => new(
                    applicationId,
                    new string('F', 64),
                    null,
                    transitive,
                    [],
                    [],
                    []);
        }
    }
}
