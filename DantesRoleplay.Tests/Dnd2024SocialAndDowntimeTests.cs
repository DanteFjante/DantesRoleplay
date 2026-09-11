using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
using DantesRoleplay.Events;
using DantesRoleplay.LocalAI;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using DantesRoleplay.Projections;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Sources;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class Dnd2024SocialAndDowntimeTests : Dnd2024TestBase
{
    [Fact]
    public async Task Social_attitudes_record_explicit_reasons_filter_by_audience_and_compensate()
    {
        await using var harness = await DndHarness.CreateAsync();
        await AddSocialFixturesAsync(harness);
        var roles = SocialRoles("actor.social.target");
        var initialInput = SocialInput(
            "social.attitude.primary", null, "dnd2024.vocabulary.attitude.friendly", "party", 0,
            [("social.reason.party", "The party returned the sealed letter.", "party"),
             ("social.reason.private", "The DM selected a private corroborating reason.", "gm")],
            ["aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"],
            ("social.consequence.primary", "The steward records the completed audience.", "party"));
        var request = harness.ActionForRoles(
            "dnd2024.mechanic.social.attitude.transition", roles, initialInput, 0,
            "60000000000000000000000000000001");

        var recorded = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);

        AssertSucceeded(recorded);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.Single(await harness.RuleEventsAsync(recorded.OperationId));
        Assert.NotNull(await harness.Entities.GetEntityAsync(
            DndHarness.StateSpaceId, "social.consequence.primary"));
        using (var state = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "social.attitude.primary",
                   "dnd2024.exploration.social-attitude"))!.ValueJson))
        {
            Assert.Equal(1, state.RootElement.GetProperty("revision").GetInt32());
            Assert.Equal("dnd2024.vocabulary.attitude.friendly",
                state.RootElement.GetProperty("attitude").GetProperty("entityId").GetString());
        }
        AssertSucceeded(await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.social.attitude.transition", SocialRoles("subject.low"),
            SocialInput("social.attitude.private", null,
                "dnd2024.vocabulary.attitude.hostile", "gm", 0,
                [("social.reason.record-private", "The DM selected a private attitude record.", "gm")]),
            0, "60000000000000000000000000000005")));

        var sourceRole = new Dictionary<string, string> { ["source"] = "subject.high" };
        var noAudience = await Assert.ThrowsAsync<ApplicationReadModelException>(() =>
            harness.ReadModels.ReadAsync(new(
                DndHarness.StateSpaceId, ApplicationIdentifier.Parse("dnd2024"),
                "dnd2024.query.social-context", sourceRole)));
        Assert.Equal("READ_MODEL_EVALUATION_FAILED", noAudience.Code);
        var player = await harness.ReadModels.ReadAsync(new(
            DndHarness.StateSpaceId, ApplicationIdentifier.Parse("dnd2024"),
            "dnd2024.query.social-context", sourceRole, MechanicAudienceContext.Player));
        var dm = await harness.ReadModels.ReadAsync(new(
            DndHarness.StateSpaceId, ApplicationIdentifier.Parse("dnd2024"),
            "dnd2024.query.social-context", sourceRole, MechanicAudienceContext.GameMaster));
        using (var playerData = JsonDocument.Parse(player.DataJson))
        {
            Assert.Equal("player", playerData.RootElement.GetProperty("perspective").GetString());
            var attitude = Assert.Single(playerData.RootElement.GetProperty("attitudes").EnumerateArray());
            var reason = Assert.Single(attitude.GetProperty("reasons").EnumerateArray());
            Assert.Equal("social.reason.party", reason.GetProperty("factId").GetString());
            Assert.DoesNotContain("private", player.DataJson, StringComparison.OrdinalIgnoreCase);
        }
        using (var dmData = JsonDocument.Parse(dm.DataJson))
        {
            Assert.Equal("dm", dmData.RootElement.GetProperty("perspective").GetString());
            var attitudes = dmData.RootElement.GetProperty("attitudes").EnumerateArray().ToArray();
            Assert.Equal(2, attitudes.Length);
            Assert.Equal(2, attitudes.Single(value =>
                    value.GetProperty("relationshipId").GetString() == "social.attitude.primary")
                .GetProperty("reasons").GetArrayLength());
        }

        roles["relationship"] = "social.attitude.primary";
        var changed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.social.attitude.transition", roles,
            SocialInput("social.attitude.primary", "dnd2024.vocabulary.attitude.friendly",
                "dnd2024.vocabulary.attitude.indifferent", "party", 1,
                [("social.reason.changed", "The DM selected a changed attitude after negotiation.", "party")]),
            0, "60000000000000000000000000000002"));
        var noOp = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.social.attitude.transition", roles,
            SocialInput("social.attitude.primary", "dnd2024.vocabulary.attitude.indifferent",
                "dnd2024.vocabulary.attitude.indifferent", "party", 2, []),
            0, "60000000000000000000000000000003"));
        var compensation = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.social.attitude.transition", roles,
            SocialInput("social.attitude.primary", "dnd2024.vocabulary.attitude.indifferent",
                "dnd2024.vocabulary.attitude.friendly", "party", 2,
                [("social.reason.compensation", "The DM explicitly corrected the selected attitude.", "party")]),
            0, "60000000000000000000000000000004"));

        AssertSucceeded(changed);
        AssertSucceeded(noOp);
        AssertSucceeded(compensation);
        Assert.Empty(await harness.EventsAsync(noOp.OperationId));
        using var compensated = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "social.attitude.primary",
            "dnd2024.exploration.social-attitude"))!.ValueJson);
        Assert.Equal(3, compensated.RootElement.GetProperty("revision").GetInt32());
        Assert.Equal("dnd2024.vocabulary.attitude.friendly",
            compensated.RootElement.GetProperty("attitude").GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task Active_catalog_keeps_existing_read_views_and_exposes_slice_16_status_and_slice_17_board_views()
    {
        await using var harness = await DndHarness.CreateAsync();
        var existing = new[]
        {
            "dnd2024.query.campaign-resume",
            "dnd2024.query.current-scene",
            "dnd2024.query.actor-context",
            "dnd2024.query.unresolved-decisions",
            "dnd2024.query.recent-consequences",
            "dnd2024.query.character-sheet-v2",
            "dnd2024.query.character-dossier-v1",
            "dnd2024.query.encounter-board"
        };
        var status = new[]
        {
            "dnd2024.query.travel-status",
            "dnd2024.query.rest-status",
            "dnd2024.query.hazard-status",
            "dnd2024.query.object-durability",
            "dnd2024.query.social-context",
            "dnd2024.query.downtime-status"
        };

        Assert.All(existing.Concat(status), queryId => Assert.True(
            harness.HasActiveQuery(queryId), $"Active catalog is missing {queryId}."));
    }

    [Fact]
    public async Task Social_attitudes_reject_stale_unknown_and_creative_judgment_inputs()
    {
        await using var harness = await DndHarness.CreateAsync();
        await AddSocialFixturesAsync(harness);
        var roles = SocialRoles("actor.social.target");
        AssertSucceeded(await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.social.attitude.transition", roles,
            SocialInput("social.attitude.guarded", null,
                "dnd2024.vocabulary.attitude.indifferent", "party", 0,
                [("social.reason.guarded", "The DM selected the initial attitude.", "party")]),
            0, "61000000000000000000000000000001")));
        roles["relationship"] = "social.attitude.guarded";

        var stale = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.social.attitude.transition", roles,
            SocialInput("social.attitude.guarded", "dnd2024.vocabulary.attitude.indifferent",
                "dnd2024.vocabulary.attitude.hostile", "party", 99,
                [("social.reason.stale", "This stale selection must not be committed.", "party")]),
            0, "61000000000000000000000000000002"));
        var creative = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.social.attitude.transition", roles,
            SocialInput("social.attitude.guarded", "dnd2024.vocabulary.attitude.indifferent",
                "dnd2024.vocabulary.attitude.hostile", "party", 1,
                [("social.reason.creative", "This invalid request must not be committed.", "party")],
                creativeDecision: "The NPC agrees and delivers a speech."),
            0, "61000000000000000000000000000003"));
        var unknown = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.social.attitude.transition", SocialRoles("actor.social.missing"),
            SocialInput("social.attitude.missing", null,
                "dnd2024.vocabulary.attitude.friendly", "party", 0,
                [("social.reason.missing", "This target does not exist.", "party")]),
            0, "61000000000000000000000000000004"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, stale.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, creative.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, unknown.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, "social.reason.stale"));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, "social.reason.creative"));
        using var unchanged = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "social.attitude.guarded",
            "dnd2024.exploration.social-attitude"))!.ValueJson);
        Assert.Equal(1, unchanged.RootElement.GetProperty("revision").GetInt32());
        Assert.Equal("dnd2024.vocabulary.attitude.indifferent",
            unchanged.RootElement.GetProperty("attitude").GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task Social_attitude_transaction_failure_rolls_back_facts_state_and_event()
    {
        await using var harness = await DndHarness.CreateAsync(failTransactionAfterEffects: true);
        await AddSocialFixturesAsync(harness);
        var failed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.social.attitude.transition", SocialRoles("actor.social.target"),
            SocialInput("social.attitude.rollback", null,
                "dnd2024.vocabulary.attitude.friendly", "party", 0,
                [("social.reason.rollback", "This transaction is forced to roll back.", "party")],
                consequence: ("social.consequence.rollback", "This consequence must roll back.", "party")),
            0, "62000000000000000000000000000001"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, failed.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, "social.attitude.rollback"));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, "social.reason.rollback"));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, "social.consequence.rollback"));
        Assert.Empty(await harness.EventsAsync(failed.OperationId));
    }

    [Fact]
    public async Task Downtime_consumes_once_advances_clock_completes_output_and_replays()
    {
        await using var harness = await DndHarness.CreateAsync();
        await AddDowntimeFixturesAsync(harness);
        var beginRoles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["participant"] = "subject.high", ["world"] = "world.downtime.fixture",
            ["definition"] = "downtime.definition.crafting.fixture"
        };
        var beginInput = "{\"activityId\":\"downtime.activity.fixture\",\"expectedDefinitionRevision\":1,"
                         + "\"expectedDefinitionFingerprint\":\"" + Fingerprint + "\","
                         + "\"prerequisiteKeys\":[\"tool.smith\"],\"reservations\":[{"
                         + "\"itemId\":\"item.downtime.material.fixture\","
                         + "\"definitionId\":\"item.definition.downtime.material\","
                         + "\"quantity\":2,\"purpose\":\"material\"}]}";
        var beginRequest = harness.ActionForRoles("dnd2024.mechanic.downtime.begin",
            beginRoles, beginInput, 0, "63000000000000000000000000000001");
        var begun = await harness.Runner.RunAsync(beginRequest);
        var beginReplay = await harness.Runner.RunAsync(beginRequest);
        AssertSucceeded(begun);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, beginReplay.Disposition);
        using (var quantity = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "item.downtime.material.fixture",
                   "dnd2024.item.quantity"))!.ValueJson))
            Assert.Equal(1, quantity.RootElement.GetProperty("current").GetInt32());

        var progressRoles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["participant"] = "subject.high", ["world"] = "world.downtime.fixture"
        };
        var progressRequest = harness.ActionForRoles("dnd2024.mechanic.downtime.progress",
            progressRoles, "{\"minutes\":60,\"expectedClockRevision\":7}", 0,
            "63000000000000000000000000000002");
        var progressed = await harness.Runner.RunAsync(progressRequest);
        var progressReplay = await harness.Runner.RunAsync(progressRequest);
        AssertSucceeded(progressed);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, progressReplay.Disposition);
        using (var clock = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "world.downtime.fixture",
                   "game.core.world.clock"))!.ValueJson))
        {
            Assert.Equal(160, clock.RootElement.GetProperty("currentMinute").GetInt32());
            Assert.Equal(8, clock.RootElement.GetProperty("revision").GetInt32());
        }

        var completeRoles = new Dictionary<string, string>(beginRoles, StringComparer.Ordinal)
        {
            ["outputDefinition"] = "item.definition.downtime.output"
        };
        var completed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.downtime.complete", completeRoles,
            "{\"expectedDefinitionRevision\":1,\"expectedDefinitionFingerprint\":\""
            + Fingerprint + "\",\"output\":{\"itemId\":\"item.downtime.output.fixture\","
            + "\"name\":\"Finished Fixture\",\"slot\":\"inventory.crafted\"}}", 0,
            "63000000000000000000000000000003"));
        AssertSucceeded(completed);
        Assert.NotNull(await harness.Entities.GetEntityAsync(
            DndHarness.StateSpaceId, "item.downtime.output.fixture"));
        using (var activity = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "subject.high", "dnd2024.downtime.activity"))!.ValueJson))
            Assert.Equal("completed", activity.RootElement.GetProperty("status").GetString());

        var read = await harness.ReadModels.ReadAsync(new(
            DndHarness.StateSpaceId, ApplicationIdentifier.Parse("dnd2024"),
            "dnd2024.query.downtime-status", beginRoles));
        using var projected = JsonDocument.Parse(read.DataJson);
        Assert.Equal("completed", projected.RootElement.GetProperty("status").GetString());
        Assert.Equal(160, projected.RootElement.GetProperty("clock").GetProperty("minute").GetInt32());
        Assert.Empty(projected.RootElement.GetProperty("nextActions").EnumerateArray());
        Assert.Contains(harness.Search("what downtime is due or ready").Records,
            value => value.Record.QualifiedId == "dnd2024.mechanic.downtime.status.project");
    }

    [Fact]
    public async Task Downtime_transaction_failure_rolls_back_reservations_state_and_events()
    {
        await using var harness = await DndHarness.CreateAsync(failTransactionAfterEffects: true);
        await AddDowntimeFixturesAsync(harness);
        var roles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["participant"] = "subject.high", ["world"] = "world.downtime.fixture",
            ["definition"] = "downtime.definition.crafting.fixture"
        };
        var failed = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.downtime.begin", roles,
            "{\"activityId\":\"downtime.activity.rollback\",\"expectedDefinitionRevision\":1,"
            + "\"expectedDefinitionFingerprint\":\"" + Fingerprint + "\","
            + "\"prerequisiteKeys\":[\"tool.smith\"],\"reservations\":[{"
            + "\"itemId\":\"item.downtime.material.fixture\","
            + "\"definitionId\":\"item.definition.downtime.material\","
            + "\"quantity\":2,\"purpose\":\"material\"}]}", 0,
            "64000000000000000000000000000001"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, failed.Disposition);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.downtime.activity"));
        using var quantity = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "item.downtime.material.fixture",
            "dnd2024.item.quantity"))!.ValueJson);
        Assert.Equal(3, quantity.RootElement.GetProperty("current").GetInt32());
        Assert.Empty(await harness.EventsAsync(failed.OperationId));
    }

    [Fact]
    public async Task Downtime_cancellation_refunds_only_by_the_authored_policy()
    {
        await using var harness = await DndHarness.CreateAsync();
        await AddDowntimeFixturesAsync(harness);
        var roles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["participant"] = "subject.low", ["world"] = "world.downtime.fixture",
            ["definition"] = "downtime.definition.crafting.fixture"
        };
        AssertSucceeded(await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.downtime.begin", roles,
            "{\"activityId\":\"downtime.activity.cancel\",\"expectedDefinitionRevision\":1,"
            + "\"expectedDefinitionFingerprint\":\"" + Fingerprint + "\","
            + "\"prerequisiteKeys\":[\"tool.smith\"],\"reservations\":[{"
            + "\"itemId\":\"item.downtime.cancel-material.fixture\","
            + "\"definitionId\":\"item.definition.downtime.material\","
            + "\"quantity\":2,\"purpose\":\"material\"}]}", 0,
            "65000000000000000000000000000001")));
        Assert.Null(await harness.Entities.GetEntityAsync(
            DndHarness.StateSpaceId, "item.downtime.cancel-material.fixture"));

        var cancelled = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.downtime.cancel", roles,
            "{\"expectedDefinitionRevision\":1,\"expectedDefinitionFingerprint\":\""
            + Fingerprint + "\",\"refunds\":[{"
            + "\"reservationItemId\":\"item.downtime.cancel-material.fixture\","
            + "\"refundItemId\":\"item.downtime.refund.fixture\"}]}", 0,
            "65000000000000000000000000000002"));
        AssertSucceeded(cancelled);
        using (var refund = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "item.downtime.refund.fixture",
                   "dnd2024.item.quantity"))!.ValueJson))
            Assert.Equal(2, refund.RootElement.GetProperty("current").GetInt32());
        using (var activity = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                   DndHarness.StateSpaceId, "subject.low", "dnd2024.downtime.activity"))!.ValueJson))
            Assert.Equal("cancelled", activity.RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("recovery")]
    [InlineData("service")]
    [InlineData("training")]
    [InlineData("lifestyle")]
    public async Task Non_crafting_downtime_families_complete_mechanically_without_creative_output(string kind)
    {
        await using var harness = await DndHarness.CreateAsync();
        await AddDowntimeFixturesAsync(harness);
        var definitionId = "downtime.definition." + kind + ".fixture";
        var roles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["participant"] = "subject.high", ["world"] = "world.downtime.fixture",
            ["definition"] = definitionId
        };
        AssertSucceeded(await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.downtime.begin", roles,
            "{\"activityId\":\"downtime.activity." + kind
            + "\",\"expectedDefinitionRevision\":1,\"expectedDefinitionFingerprint\":\""
            + Fingerprint + "\",\"prerequisiteKeys\":[],\"reservations\":[]}", 0,
            "66000000000000000000000000000001")));
        AssertSucceeded(await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.downtime.progress",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["participant"] = "subject.high", ["world"] = "world.downtime.fixture"
            }, "{\"minutes\":5,\"expectedClockRevision\":7}", 0,
            "66000000000000000000000000000002")));
        AssertSucceeded(await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.downtime.complete", roles,
            "{\"expectedDefinitionRevision\":1,\"expectedDefinitionFingerprint\":\""
            + Fingerprint + "\"}", 0, "66000000000000000000000000000003")));
        using var activity = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.downtime.activity"))!.ValueJson);
        Assert.Equal(kind, activity.RootElement.GetProperty("kind").GetString());
        Assert.Equal("completed", activity.RootElement.GetProperty("status").GetString());
        Assert.False(activity.RootElement.TryGetProperty("outputItemId", out _));
    }
}
