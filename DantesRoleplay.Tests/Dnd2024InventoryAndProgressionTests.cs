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

public sealed class Dnd2024InventoryAndProgressionTests : Dnd2024TestBase
{
    [Theory]
    [InlineData("tiny")]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    [InlineData("huge")]
    [InlineData("gargantuan")]
    public async Task Creature_size_accepts_every_closed_category(string size)
    {
        await using var harness = await DndHarness.CreateAsync();
        var result = await harness.EvaluateRolesAsync("dnd2024.mechanic.creature-size.record",
            new Dictionary<string, string> { ["creature"] = "subject.low" },
            "{\"size\":\"" + size + "\"}", 0);
        Assert.True(result.Ok, result.Run?.Error);
        Assert.Single(result.Run!.Output.Effects);
    }

    [Fact]
    public async Task Language_and_tool_recorders_canonicalize_correct_and_reject_unknown_members()
    {
        await using var harness = await DndHarness.CreateAsync();
        var language = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.language-proficiencies.record", "subject.high",
            "{\"mode\":\"record\",\"languages\":[\"elvish\",\"common\"]}", 0,
            "7123456789abcdef0123456789abcded"));
        var tool = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.tool-proficiencies.record", "subject.high",
            "{\"mode\":\"record\",\"tools\":[\"thieves-tools\",\"lyre\"]}", 0,
            "8123456789abcdef0123456789abcded"));
        var corrected = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.language-proficiencies.record", "subject.high",
            "{\"mode\":\"correct\",\"languages\":[]}", 0,
            "9123456789abcdef0123456789abcded"));
        var invalid = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.tool-proficiencies.record", "subject.high",
            "{\"mode\":\"correct\",\"tools\":[\"laser-cutter\"]}", 0,
            "a123456789abcdef0123456789abcded"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, language.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, tool.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, corrected.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, invalid.Disposition);
        var languages = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.languages");
        var tools = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.proficiencies");
        Assert.Equal(2, languages!.Revision);
        Assert.StartsWith("{\"languages\":{}", languages.ValueJson, StringComparison.Ordinal);
        Assert.Equal(1, tools!.Revision);
        using var toolState = JsonDocument.Parse(tools.ValueJson);
        Assert.Equal(new[] { "lyre", "thieves-tools" }, ReadToolIds(toolState.RootElement).ToArray());
    }

    [Fact]
    public async Task Item_runtime_component_schemas_require_a_closed_definition_link_and_positive_quantity()
    {
        var root = RepositoryRoot();
        var componentRoot = Path.Combine(root, "catalog", "applications", "dnd2024", "components");
        var validator = new BoundedJsonSchemaValidator();
        var link = validator.Compile(await File.ReadAllTextAsync(Path.Combine(
            componentRoot, "dnd2024.core.definition-link.schema.json")));
        var quantity = validator.Compile(await File.ReadAllTextAsync(Path.Combine(
            componentRoot, "dnd2024.item.quantity.schema.json")));

        Assert.True(link.IsAccepted, string.Join("; ", link.Diagnostics));
        Assert.True(quantity.IsAccepted, string.Join("; ", quantity.Diagnostics));
        Assert.Equal(SchemaValueStatus.Valid, validator.Validate(link.ProfileId,
            link.NormalizedSchema, "{\"definition\":{\"entityId\":\"dnd2024.item.arrow.v1\"}}").Status);
        Assert.Equal(SchemaValueStatus.Valid, validator.Validate(link.ProfileId,
            link.NormalizedSchema, "{\"definition\":{\"entityId\":\"dnd2024.item.arrow.v1\"},\"definitionRevision\":1}").Status);
        Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(link.ProfileId,
            link.NormalizedSchema, "{\"definitionId\":\"dnd2024.item.arrow.v1\"}").Status);
        Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(link.ProfileId,
            link.NormalizedSchema, "{\"definition\":{\"entityId\":\"dnd2024.item.arrow.v1\"},\"extra\":true}").Status);
        Assert.Equal(SchemaValueStatus.Valid, validator.Validate(quantity.ProfileId,
            quantity.NormalizedSchema, "{\"current\":1}").Status);
        Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(quantity.ProfileId,
            quantity.NormalizedSchema, "{\"current\":0}").Status);
        Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(quantity.ProfileId,
            quantity.NormalizedSchema, "{\"count\":1,\"stackKey\":\"dnd2024.item.arrow.v1\"}").Status);
    }

    [Fact]
    public async Task Item_instance_record_create_read_and_move_use_definition_identity_and_containment()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string definitionId = "dnd2024.item.robe.v1";
        await harness.AddItemDefinitionAsync(definitionId, "Robe definition", SeparateItemDefinition());
        var recordRoles = new Dictionary<string, string>
        {
            ["item"] = "subject.low", ["definition"] = definitionId
        };
        var recorded = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item-instance.record", recordRoles, "{}", 0,
            "b123456789abcdef0123456789abcded"));
        var createRoles = new Dictionary<string, string>
        {
            ["definition"] = definitionId, ["destination"] = "subject.high"
        };
        var created = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item-instance.create-and-place", createRoles,
            "{\"itemId\":\"item.campaign.robe\",\"name\":\"Traveler's Robe\",\"slot\":\"carried\"}",
            0, "c123456789abcdef0123456789abcded"));
        var read = await harness.EvaluateRolesAsync("dnd2024.mechanic.item-instance.read",
            new Dictionary<string, string> { ["item"] = "item.campaign.robe" }, "{}", 0);
        var moved = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item-instance.move", new Dictionary<string, string>
            {
                ["item"] = "item.campaign.robe", ["destination"] = "subject.low"
            }, "{\"slot\":\"gift\"}", 0, "d123456789abcdef0123456789abcded"));

        Assert.True(recorded.Disposition == ApplicationActionExecutionDisposition.Succeeded,
            string.Join("; ", recorded.Problems.Select(value => value.Code + ": " + value.SafeMessage)));
        Assert.True(created.Disposition == ApplicationActionExecutionDisposition.Succeeded,
            string.Join("; ", created.Problems.Select(value => value.Code + ": " + value.SafeMessage)));
        Assert.True(read.Ok, read.Run?.Error);
        Assert.Contains("\"containerId\":\"subject.high\"", read.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.True(moved.Disposition == ApplicationActionExecutionDisposition.Succeeded,
            string.Join("; ", moved.Problems.Select(value => value.Code + ": " + value.SafeMessage)));
    }

    [Fact]
    public async Task Fungible_stack_lifecycle_conserves_count_and_deletes_zero()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string definitionId = "dnd2024.item.arrow.v1";
        await harness.AddItemDefinitionAsync(definitionId, "Arrow definition", FungibleItemDefinition());
        await harness.AddPhysicalItemAsync("item.stack.recorded", "Recorded Arrows", definitionId,
            "subject.low", includeQuantity: false);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.item-stack.record", new Dictionary<string, string>
                {
                    ["item"] = "item.stack.recorded", ["definition"] = definitionId
                }, "{\"count\":2}", 0, "d123456789abcdef0123456789abcdee"))).Disposition);
        var definitionAndDestination = new Dictionary<string, string>
        {
            ["definition"] = definitionId, ["destination"] = "subject.high"
        };
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.item-stack.create-and-place", definitionAndDestination,
                "{\"count\":10,\"itemId\":\"item.stack.arrows\",\"name\":\"Arrows\",\"slot\":\"quiver\"}",
                0, "e123456789abcdef0123456789abcded"))).Disposition);
        const string childDefinition = "dnd2024.item.token.v1";
        await harness.AddItemDefinitionAsync(childDefinition, "Token definition", SeparateItemDefinition());
        await harness.AddPhysicalItemAsync("item.stack.child", "Token", childDefinition,
            "item.stack.arrows", "inside");
        var blockedByContents = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item-stack.consume", new Dictionary<string, string>
            {
                ["item"] = "item.stack.arrows", ["definition"] = definitionId
            }, "{\"count\":1}", 0, "e223456789abcdef0123456789abcded"));
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, blockedByContents.Disposition);
        Assert.Contains("\"current\":10", (await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "item.stack.arrows", "dnd2024.item.quantity"))!.ValueJson,
            StringComparison.Ordinal);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.item-instance.move", new Dictionary<string, string>
                {
                    ["item"] = "item.stack.child", ["destination"] = "subject.high"
                }, "{\"slot\":\"carried\"}", 0, "e323456789abcdef0123456789abcded"))).Disposition);
        var split = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item-stack.split", new Dictionary<string, string>
            {
                ["source"] = "item.stack.arrows", ["definition"] = definitionId
            }, "{\"count\":3,\"itemId\":\"item.stack.arrows-split\",\"name\":\"Three Arrows\"}",
            0, "f123456789abcdef0123456789abcded"));
        var merged = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item-stack.merge", new Dictionary<string, string>
            {
                ["source"] = "item.stack.arrows-split", ["target"] = "item.stack.arrows",
                ["definition"] = definitionId
            }, "{}", 0, "0123456789abcdef0123456789abcdee"));
        var partial = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item-stack.consume", new Dictionary<string, string>
            {
                ["item"] = "item.stack.arrows", ["definition"] = definitionId
            }, "{\"count\":4}", 0, "1123456789abcdef0123456789abcdee"));
        var final = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item-stack.consume", new Dictionary<string, string>
            {
                ["item"] = "item.stack.arrows", ["definition"] = definitionId
            }, "{\"count\":6}", 0, "2123456789abcdef0123456789abcdee"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, split.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, merged.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, partial.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, final.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, "item.stack.arrows"));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, "item.stack.arrows-split"));
    }

    [Fact]
    public async Task Equipment_and_transfer_require_definition_eligibility_direct_custody_and_unequipped_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string definitionId = "dnd2024.item.spear.v1";
        await harness.AddItemDefinitionAsync(definitionId, "Spear definition",
            SeparateItemDefinition("[\"held\"]"));
        await harness.AddPhysicalItemAsync("item.spear", "Spear", definitionId, "subject.high");
        var roles = new Dictionary<string, string>
        {
            ["item"] = "item.spear", ["holder"] = "subject.high"
        };
        var equipped = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item.equip", roles,
            "{\"slotIds\":[\"dnd2024.equipment-slot.main-hand\"]}", 0,
            "3123456789abcdef0123456789abcdee"));
        var blocked = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item.transfer", new Dictionary<string, string>
            {
                ["item"] = "item.spear", ["source"] = "subject.high", ["destination"] = "subject.low"
            }, "{\"slot\":\"carried\"}", 0, "4123456789abcdef0123456789abcdee"));
        var read = await harness.EvaluateRolesAsync("dnd2024.mechanic.item.equipment.read",
            new Dictionary<string, string> { ["item"] = "item.spear" }, "{}", 0);
        var unequipped = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item.unequip", roles, "{}", 0,
            "5123456789abcdef0123456789abcdee"));
        var transferred = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item.transfer", new Dictionary<string, string>
            {
                ["item"] = "item.spear", ["source"] = "subject.high", ["destination"] = "subject.low"
            }, "{\"slot\":\"carried\"}", 0, "6123456789abcdef0123456789abcdee"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, equipped.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, blocked.Disposition);
        Assert.Contains("\"entityId\":\"dnd2024.equipment-slot.main-hand\"",
            read.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, unequipped.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, transferred.Disposition);
    }

    [Fact]
    public async Task Item_transfer_enforces_direct_container_weight_capacity_without_partial_move()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string itemDefinition = "dnd2024.item.stone.v1";
        const string bagDefinition = "dnd2024.item.bag.v1";
        await harness.AddItemDefinitionAsync(itemDefinition, "Stone definition", SeparateItemDefinition());
        await harness.AddItemDefinitionAsync(bagDefinition, "Bag definition", ContainerItemDefinition(1));
        await harness.AddPhysicalItemAsync("item.bag", "Bag", bagDefinition, "subject.low");
        await harness.AddPhysicalItemAsync("item.stone.one", "Stone One", itemDefinition, "subject.high");
        await harness.AddPhysicalItemAsync("item.stone.two", "Stone Two", itemDefinition, "subject.high");
        var rolesOne = new Dictionary<string, string>
        {
            ["item"] = "item.stone.one", ["source"] = "subject.high", ["destination"] = "item.bag"
        };
        var rolesTwo = new Dictionary<string, string>(rolesOne) { ["item"] = "item.stone.two" };

        var first = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item.transfer", rolesOne, "{\"slot\":\"inside\"}", 0,
            "9123456789abcdef0123456789abcdee"));
        var second = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item.transfer", rolesTwo, "{\"slot\":\"inside\"}", 0,
            "a123456789abcdef0123456789abcdee"));
        var secondRead = await harness.EvaluateRolesAsync("dnd2024.mechanic.item-instance.read",
            new Dictionary<string, string> { ["item"] = "item.stone.two" }, "{}", 0);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, first.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, second.Disposition);
        Assert.Contains("\"containerId\":\"subject.high\"", secondRead.Run!.Output.Data,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Item_activity_is_descriptor_driven_atomic_and_duplicate_safe()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string sourceDefinition = "dnd2024.item.package.v1";
        const string grantDefinition = "dnd2024.item.rope.v1";
        await harness.AddItemDefinitionAsync(grantDefinition, "Rope definition", SeparateItemDefinition());
        await harness.AddItemDefinitionAsync(sourceDefinition, "Package definition", FungibleItemDefinition(),
            "{\"activities\":[{\"id\":\"open\",\"kind\":\"consume-and-grant-item\",\"consumeQuantity\":1,\"grant\":{\"definitionId\":\"dnd2024.item.rope.v1\",\"name\":\"Rope\",\"slot\":\"unpacked\"}}]}");
        await harness.AddPhysicalItemAsync("item.package-stack", "Packages", sourceDefinition,
            "subject.high", quantity: 2);
        var roles = new Dictionary<string, string>
        {
            ["item"] = "item.package-stack", ["definition"] = sourceDefinition,
            ["grantDefinition"] = grantDefinition
        };
        const string input = "{\"activityId\":\"open\",\"grantItemId\":\"item.granted-rope\"}";
        var used = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item-activity.use", roles, input, 0,
            "7123456789abcdef0123456789abcdee"));
        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "item.package-stack", "dnd2024.item.quantity");
        var duplicate = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item-activity.use", roles, input, 0,
            "8123456789abcdef0123456789abcdee"));
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "item.package-stack", "dnd2024.item.quantity");

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, used.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, duplicate.Disposition);
        Assert.Equal(before!.ValueJson, after!.ValueJson);
        Assert.Contains("\"current\":1", after.ValueJson, StringComparison.Ordinal);
        Assert.NotNull(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, "item.granted-rope"));
    }

    [Fact]
    public async Task Inventory_mutations_replay_without_second_quantity_containment_component_event_or_invalidation_change()
    {
        await using var harness = await DndHarness.CreateAsync(includeObjectChangeParticipant: true);
        const string arrows = "dnd2024.item.replay-arrow.v1";
        const string spear = "dnd2024.item.replay-spear.v1";
        await harness.AddItemDefinitionAsync(arrows, "Replay arrows", FungibleItemDefinition());
        await harness.AddItemDefinitionAsync(spear, "Replay spear", SeparateItemDefinition("[\"held\"]"));
        await harness.AddPhysicalItemAsync("item.replay.spear", "Replay Spear", spear, "subject.high");

        async Task<string> InventoryStateAsync()
        {
            var arrowsState = await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
                "item.replay.arrows", "dnd2024.item.quantity");
            var spearEquipment = await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
                "item.replay.spear", "dnd2024.item.equipment");
            var spearContainment = await harness.Edges.GetContainmentAsync(DndHarness.StateSpaceId,
                "item.replay.spear");
            return string.Join("|", arrowsState?.ValueJson ?? "absent", arrowsState?.Revision ?? -1,
                spearEquipment?.ValueJson ?? "absent", spearEquipment?.Revision ?? -1,
                spearContainment?.ContainerEntityId ?? "absent", spearContainment?.Slot ?? "absent",
                spearContainment?.Revision ?? -1);
        }

        async Task ReplayAsync(ApplicationActionExecutionRequest request)
        {
            var first = await harness.Runner.RunAsync(request);
            Assert.True(first.Disposition == ApplicationActionExecutionDisposition.Succeeded,
                string.Join("; ", first.Problems.Select(value => value.Code + ": " + value.SafeMessage)));
            var state = await InventoryStateAsync();
            var events = (await harness.EventsAsync(first.OperationId)).Count;
            var changes = await harness.ChangeDeliveryCountAsync();
            var replay = await harness.Runner.RunAsync(request);
            Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
            Assert.Equal(first.OperationId, replay.OperationId);
            Assert.Equal(state, await InventoryStateAsync());
            Assert.Equal(events, (await harness.EventsAsync(first.OperationId)).Count);
            Assert.Equal(changes, await harness.ChangeDeliveryCountAsync());
        }

        await ReplayAsync(harness.ActionForRoles("dnd2024.mechanic.item-stack.create-and-place",
            new Dictionary<string, string> { ["definition"] = arrows, ["destination"] = "subject.high" },
            "{\"count\":3,\"itemId\":\"item.replay.arrows\",\"name\":\"Replay Arrows\",\"slot\":\"quiver\"}",
            0, "a0000000000000000000000000000001"));
        Assert.Contains("\"current\":3", (await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
            "item.replay.arrows", "dnd2024.item.quantity"))!.ValueJson, StringComparison.Ordinal);

        await ReplayAsync(harness.ActionForRoles("dnd2024.mechanic.item-instance.move",
            new Dictionary<string, string> { ["item"] = "item.replay.spear", ["destination"] = "subject.low" },
            "{\"slot\":\"gift\"}", 0, "a0000000000000000000000000000002"));
        Assert.Equal("subject.low", (await harness.Edges.GetContainmentAsync(DndHarness.StateSpaceId,
            "item.replay.spear"))!.ContainerEntityId);

        await ReplayAsync(harness.ActionForRoles("dnd2024.mechanic.item.transfer",
            new Dictionary<string, string>
            {
                ["item"] = "item.replay.spear", ["source"] = "subject.low", ["destination"] = "subject.high"
            }, "{\"slot\":\"carried\"}", 0, "a0000000000000000000000000000003"));
        Assert.Equal("subject.high", (await harness.Edges.GetContainmentAsync(DndHarness.StateSpaceId,
            "item.replay.spear"))!.ContainerEntityId);

        var equipmentRoles = new Dictionary<string, string>
        {
            ["item"] = "item.replay.spear", ["holder"] = "subject.high"
        };
        await ReplayAsync(harness.ActionForRoles("dnd2024.mechanic.item.equip", equipmentRoles,
            "{\"slotIds\":[\"dnd2024.equipment-slot.main-hand\"]}", 0,
            "a0000000000000000000000000000004"));
        Assert.NotNull(await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
            "item.replay.spear", "dnd2024.item.equipment"));

        await ReplayAsync(harness.ActionForRoles("dnd2024.mechanic.item.unequip", equipmentRoles,
            "{}", 0, "a0000000000000000000000000000005"));
        Assert.Null(await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
            "item.replay.spear", "dnd2024.item.equipment"));

        await ReplayAsync(harness.ActionForRoles("dnd2024.mechanic.item-stack.consume",
            new Dictionary<string, string> { ["item"] = "item.replay.arrows", ["definition"] = arrows },
            "{\"count\":1}", 0, "a0000000000000000000000000000006"));
        Assert.Contains("\"current\":2", (await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
            "item.replay.arrows", "dnd2024.item.quantity"))!.ValueJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inventory_mutations_late_rollback_preserves_quantity_containment_components_events_and_invalidations()
    {
        await using var harness = await DndHarness.CreateAsync(failTransactionAfterEffects: true,
            includeObjectChangeParticipant: true);
        const string arrows = "dnd2024.item.rollback-arrow.v1";
        const string spear = "dnd2024.item.rollback-spear.v1";
        await harness.AddItemDefinitionAsync(arrows, "Rollback arrows", FungibleItemDefinition());
        await harness.AddItemDefinitionAsync(spear, "Rollback spear", SeparateItemDefinition("[\"held\"]"));
        await harness.AddPhysicalItemAsync("item.rollback.arrows", "Rollback Arrows", arrows,
            "subject.high", quantity: 3);
        await harness.AddPhysicalItemAsync("item.rollback.spear", "Rollback Spear", spear, "subject.high");

        async Task<string> StateAsync()
        {
            var quantity = await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
                "item.rollback.arrows", "dnd2024.item.quantity");
            var equipment = await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
                "item.rollback.spear", "dnd2024.item.equipment");
            var containment = await harness.Edges.GetContainmentAsync(DndHarness.StateSpaceId,
                "item.rollback.spear");
            var added = await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, "item.rollback.added");
            return string.Join("|", quantity!.ValueJson, quantity.Revision, equipment?.ValueJson ?? "absent",
                equipment?.Revision ?? -1, containment!.ContainerEntityId, containment.Slot,
                containment.Revision, added?.Revision ?? -1);
        }

        async Task RejectLateAsync(ApplicationActionExecutionRequest request)
        {
            var before = await StateAsync();
            var eventCount = (await harness.EventsAsync(request.ExecutionIdentity.OperationId)).Count;
            var changeCount = await harness.ChangeDeliveryCountAsync();
            var result = await harness.Runner.RunAsync(request);
            Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
            Assert.Equal(before, await StateAsync());
            Assert.Equal(eventCount, (await harness.EventsAsync(request.ExecutionIdentity.OperationId)).Count);
            Assert.Equal(changeCount, await harness.ChangeDeliveryCountAsync());
        }

        await RejectLateAsync(harness.ActionForRoles("dnd2024.mechanic.item-stack.create-and-place",
            new Dictionary<string, string> { ["definition"] = arrows, ["destination"] = "subject.high" },
            "{\"count\":1,\"itemId\":\"item.rollback.added\",\"name\":\"Never Added\",\"slot\":\"quiver\"}",
            0, "b0000000000000000000000000000001"));
        await RejectLateAsync(harness.ActionForRoles("dnd2024.mechanic.item-instance.move",
            new Dictionary<string, string> { ["item"] = "item.rollback.spear", ["destination"] = "subject.low" },
            "{\"slot\":\"gift\"}", 0, "b0000000000000000000000000000002"));
        await RejectLateAsync(harness.ActionForRoles("dnd2024.mechanic.item.transfer",
            new Dictionary<string, string>
            {
                ["item"] = "item.rollback.spear", ["source"] = "subject.high", ["destination"] = "subject.low"
            }, "{\"slot\":\"gift\"}", 0, "b0000000000000000000000000000003"));
        var equipmentRoles = new Dictionary<string, string>
        {
            ["item"] = "item.rollback.spear", ["holder"] = "subject.high"
        };
        await RejectLateAsync(harness.ActionForRoles("dnd2024.mechanic.item.equip", equipmentRoles,
            "{\"slotIds\":[\"dnd2024.equipment-slot.main-hand\"]}", 0,
            "b0000000000000000000000000000004"));
        await harness.AddApplicationComponentAsync("item.rollback.spear", "dnd2024.item.equipment",
            "{\"equippedBy\":{\"entityId\":\"subject.high\"},\"slots\":[{\"entityId\":\"dnd2024.equipment-slot.main-hand\"}]}" );
        await RejectLateAsync(harness.ActionForRoles("dnd2024.mechanic.item.unequip", equipmentRoles,
            "{}", 0, "b0000000000000000000000000000005"));
        await RejectLateAsync(harness.ActionForRoles("dnd2024.mechanic.item-stack.consume",
            new Dictionary<string, string> { ["item"] = "item.rollback.arrows", ["definition"] = arrows },
            "{\"count\":1}", 0, "b0000000000000000000000000000006"));
    }

    [Fact]
    public async Task Inventory_burden_and_carrying_capacity_compose_exact_bounded_views()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string robe = "dnd2024.item.robe-reader.v1";
        const string arrows = "dnd2024.item.arrow-reader.v1";
        await harness.AddItemDefinitionAsync(robe, "Robe definition", SeparateItemDefinition());
        await harness.AddItemDefinitionAsync(arrows, "Arrow definition", FungibleItemDefinition());
        await harness.AddPhysicalItemAsync("item.reader.robe", "Robe", robe, "subject.high");
        await harness.AddPhysicalItemAsync("item.reader.arrows", "Arrows", arrows, "subject.high",
            quantity: 20);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.creature-size.record",
                new Dictionary<string, string> { ["creature"] = "subject.high" },
                "{\"size\":\"medium\"}", 0, "b123456789abcdef0123456789abcdee"))).Disposition);

        var inventory = await harness.EvaluateRolesAsync("dnd2024.mechanic.inventory.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);
        var burden = await harness.EvaluateRolesAsync("dnd2024.mechanic.item-burden.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);
        var carrying = await harness.EvaluateRolesAsync("dnd2024.mechanic.carrying-capacity.read",
            new Dictionary<string, string> { ["creature"] = "subject.high" }, "{}", 0,
            MechanicAudienceContext.GameMaster);
        var playerCarrying = await harness.EvaluateRolesAsync("dnd2024.mechanic.carrying-capacity.read",
            new Dictionary<string, string> { ["creature"] = "subject.high" }, "{}", 0,
            MechanicAudienceContext.Player);

        Assert.True(inventory.Ok,
            string.Join("; ", inventory.Problems.Append(inventory.Run?.Error ?? string.Empty)));
        Assert.Contains("\"mayOmitDeeperContents\":true", inventory.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.True(burden.Ok, burden.Run?.Error);
        Assert.Contains("\"mass\":{\"dimension\":\"mass\",\"value\":{\"numerator\":45359237,\"denominator\":50000000}",
            burden.Run!.Output.Data, StringComparison.Ordinal);
        Assert.True(carrying.Ok,
            string.Join("; ", carrying.Problems.Append(carrying.Run?.Error ?? string.Empty)));
        Assert.True(playerCarrying.Ok,
            string.Join("; ", playerCarrying.Problems.Append(playerCarrying.Run?.Error ?? string.Empty)));
        Assert.Equal(carrying.Run!.Output.Data, playerCarrying.Run!.Output.Data);
        Assert.Equal(carrying.Run.Output.Narration, playerCarrying.Run.Output.Narration);
        Assert.Contains("\"carryingCapacity\":{\"dimension\":\"mass\",\"value\":{\"numerator\":408233133,\"denominator\":2000000}",
            carrying.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(carrying.Run.Output.Effects);
        var capacityProjection = carrying.Projection!;
        var capacityObject = Assert.Single(capacityProjection.Objects).Value;
        Assert.Equal("dnd2024.object.carrying-capacity-creature", capacityObject.QualifiedId);
        Assert.Equal("subject.high", capacityObject.Roles["creature"].Id);
        Assert.Equal(30, capacityObject.Value.GetProperty("abilityScores").GetProperty("scores")
            .GetProperty("dnd2024.vocabulary.ability.strength").GetInt32());
        Assert.Equal("dnd2024.vocabulary.size.medium",
            capacityObject.Value.GetProperty("sizeRef").GetProperty("entityId").GetString());
        Assert.InRange(Encoding.UTF8.GetByteCount(capacityObject.Value.GetRawText()), 1, 4096);
        Assert.Empty(capacityProjection.Roles["creature"].Components);
        Assert.Null(capacityProjection.Roles["creature"].Contains);
        Assert.Empty(capacityProjection.References);
        var sourceRevisions = capacityProjection.ComponentRevisions["subject.high"];
        Assert.True(sourceRevisions["dnd2024.creature.ability-scores"] > 0);
        Assert.True(sourceRevisions["dnd2024.creature.body"] > 0);
        Assert.Single(capacityProjection.Children["burden"]);
    }

    [Fact]
    public async Task Carrying_capacity_object_requires_complete_strength_and_size_sources()
    {
        await using var harness = await DndHarness.CreateAsync();
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded,
            (await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.creature-size.record",
                new Dictionary<string, string> { ["creature"] = "subject.high" },
                "{\"size\":\"medium\"}", 0,
                "b223456789abcdef0123456789abcdee"))).Disposition);
        var body = (await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
            "subject.high", "dnd2024.creature.body"))!;
        Assert.True(await harness.Entities.RemoveComponentAsync(DndHarness.StateSpaceId,
            "subject.high", body.Type, body.Revision));

        var result = await harness.EvaluateRolesAsync("dnd2024.mechanic.carrying-capacity.read",
            new Dictionary<string, string> { ["creature"] = "subject.high" }, "{}", 0,
            MechanicAudienceContext.GameMaster);

        Assert.False(result.Ok);
        Assert.Equal(["OBJECT_SNAPSHOT_UNAVAILABLE"], result.Problems);
        Assert.Null(result.Run);
    }

    [Fact]
    public async Task Currency_reader_derives_mixed_physical_coin_value_without_wallet_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string cp = "dnd2024.equipment.currency.copper-piece";
        const string gp = "dnd2024.equipment.currency.gold-piece";
        await harness.AddCanonicalCurrencyDefinitionFixtureAsync("copper-piece");
        await harness.AddCanonicalCurrencyDefinitionFixtureAsync("gold-piece");
        await harness.AddPhysicalItemAsync("item.coins.cp", "Copper Pieces", cp, "subject.high",
            quantity: 10);
        await harness.AddPhysicalItemAsync("item.coins.gp", "Gold Pieces", gp, "subject.high",
            quantity: 2);

        var result = await harness.EvaluateRolesAsync("dnd2024.mechanic.currency-value.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);

        Assert.True(result.Ok, result.Run?.Error);
        Assert.Contains("\"coinCount\":12", result.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Contains("\"copperValue\":210", result.Run.Output.Data, StringComparison.Ordinal);
        Assert.Empty(result.Run.Output.Effects);
    }

    [Fact]
    public async Task Activated_static_currency_cohort_is_schema_valid_and_consumed_by_existing_readers()
    {
        var root = RepositoryRoot();
        var contentRoot = Path.Combine(root, "catalog", "applications", "dnd2024", "content",
            "entities", "equipment", "base");
        var paths = Directory.GetFiles(contentRoot, "equipment.currency.*.json")
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(5, paths.Length);

        await using var harness = await DndHarness.CreateAsync();
        var definitions = new Dictionary<string, EntityFile>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            Assert.Contains(relative, harness.ActiveSourcePaths);
            var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
            Assert.Contains(entity.Components, component =>
                component.DefinitionId == "dnd2024.core.source");
            Assert.Contains(entity.Components, component =>
                component.DefinitionId == "dnd2024.core.version");
            Assert.Contains(entity.Components, component =>
                component.DefinitionId == "dnd2024.item.physical");
            definitions.Add(entity.Id, entity);
            await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, entity.Id, entity.Name);
            foreach (var component in entity.Components)
                await harness.AddApplicationComponentAsync(
                    entity.Id, component.DefinitionId, component.Data);
        }

        var cp = definitions["dnd2024.equipment.currency.copper-piece"];
        var gp = definitions["dnd2024.equipment.currency.gold-piece"];
        await harness.AddPhysicalItemAsync("item.static-coins.cp", "Copper Pieces", cp.Id,
            "subject.high", quantity: 10);
        await harness.AddPhysicalItemAsync("item.static-coins.gp", "Gold Pieces", gp.Id,
            "subject.high", quantity: 2);

        var currency = await harness.EvaluateRolesAsync("dnd2024.mechanic.currency-value.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);
        var burden = await harness.EvaluateRolesAsync("dnd2024.mechanic.item-burden.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);

        Assert.True(currency.Ok, currency.Run?.Error);
        Assert.Contains("\"coinCount\":12", currency.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Contains("\"copperValue\":210", currency.Run.Output.Data, StringComparison.Ordinal);
        Assert.True(burden.Ok, burden.Run?.Error);
        Assert.Contains("\"value\":{\"numerator\":136077711,\"denominator\":1250000000}",
            burden.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(currency.Run.Output.Effects);
        Assert.Empty(burden.Run.Output.Effects);
    }

    [Fact]
    public async Task Activated_split_adventuring_gear_is_schema_valid_and_enforces_backpack_capacity()
    {
        var root = RepositoryRoot();
        var contentRoot = Path.Combine(root, "catalog", "applications", "dnd2024", "content",
            "entities", "equipment", "base");
        var paths = new[] { "equipment.gear.backpack.json", "equipment.gear.waterskin.json" }
            .Select(file => Path.Combine(contentRoot, file)).ToArray();

        await using var harness = await DndHarness.CreateAsync();
        var definitions = new Dictionary<string, EntityFile>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            Assert.Contains(relative, harness.ActiveSourcePaths);
            var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
            Assert.Contains(entity.Components,
                component => component.DefinitionId == "dnd2024.item.physical");
            using var source = JsonDocument.Parse(entity.Components.Single(component =>
                component.DefinitionId == "dnd2024.core.source").Data);
            Assert.Equal("dnd2024.source.srd-5.2.1", source.RootElement.GetProperty("citations")[0]
                .GetProperty("sourceRef").GetProperty("entityId").GetString());
            definitions.Add(entity.Id, entity);
            await harness.AddCatalogEntityAsync(entity);
        }

        await harness.AddPhysicalItemAsync("item.static.backpack", "Backpack",
            definitions["dnd2024.equipment.gear.backpack"].Id, "subject.low");
        for (var index = 0; index < 7; index++)
        {
            await harness.AddPhysicalItemAsync($"item.static.waterskin.{index}", $"Waterskin {index}",
                definitions["dnd2024.equipment.gear.waterskin"].Id, "subject.high");
        }

        for (var index = 0; index < 7; index++)
        {
            var moved = await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.item.transfer", new Dictionary<string, string>
                {
                    ["item"] = $"item.static.waterskin.{index}",
                    ["source"] = "subject.high",
                    ["destination"] = "item.static.backpack"
                }, "{\"slot\":\"inside\"}", 0, (index + 1).ToString("x32")));
            var expected = index < 6
                ? ApplicationActionExecutionDisposition.Succeeded
                : ApplicationActionExecutionDisposition.Failed;
            Assert.True(moved.Disposition == expected,
                $"Waterskin {index} expected {expected} but was {moved.Disposition}: "
                + string.Join("; ", moved.Problems.Select(problem =>
                    problem.Code + ": " + problem.SafeMessage)));
        }

        var refused = await harness.EvaluateRolesAsync("dnd2024.mechanic.item-instance.read",
            new Dictionary<string, string> { ["item"] = "item.static.waterskin.6" }, "{}", 0);
        var burden = await harness.EvaluateRolesAsync("dnd2024.mechanic.item-burden.read",
            new Dictionary<string, string> { ["root"] = "subject.low" }, "{}", 0);
        Assert.True(refused.Ok, refused.Run?.Error);
        Assert.Contains("\"containerId\":\"subject.high\"", refused.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.True(burden.Ok, burden.Run?.Error);
        Assert.Contains("\"mass\":{\"dimension\":\"mass\",\"value\":{\"numerator\":317514659,\"denominator\":20000000}",
            burden.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(refused.Run.Output.Effects);
        Assert.Empty(burden.Run.Output.Effects);
    }

    [Fact]
    public async Task Optional_legacy_rope_is_consumed_only_when_extension_profile_is_selected()
    {
        const string relativePath =
            "catalog/extensions/dnd2024/legacy-equipment/content/entities/adventuring-gear/dnd2024.extension.legacy-equipment.item.hempen-rope-50-foot.v1.json";
        await using var coreOnly = await DndHarness.CreateAsync();
        Assert.DoesNotContain(relativePath, coreOnly.ActiveSourcePaths);

        await using var extended = await DndHarness.CreateAsync(includeLegacyEquipmentExtension: true);
        Assert.Contains(relativePath, extended.ActiveSourcePaths);
        var path = Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relativePath);
        var component = Assert.Single(entity.Components);
        Assert.Equal("dnd2024.item-definition", component.DefinitionId);
        await extended.AddItemDefinitionAsync(entity.Id, entity.Name, component.Data);
        await extended.AddPhysicalItemAsync("item.compatibility.rope", "Hempen Rope", entity.Id,
            "subject.high");

        var burden = await extended.EvaluateRolesAsync("dnd2024.mechanic.item-burden.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);

        Assert.True(burden.Ok, burden.Run?.Error);
        Assert.Contains("\"mass\":{\"dimension\":\"mass\",\"value\":{\"numerator\":45359237,\"denominator\":20000000}",
            burden.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(burden.Run.Output.Effects);
    }

    [Fact]
    public async Task Activated_catalog_item_facets_drive_equipment_and_burden_readers()
    {
        var root = RepositoryRoot();
        const string relative =
            "catalog/applications/dnd2024/content/entities/equipment/weapon/equipment.weapon.club.json";
        await using var harness = await DndHarness.CreateAsync();
        Assert.Contains(relative, harness.ActiveSourcePaths);
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
        Assert.Equal("dnd2024.equipment.weapon.club", entity.Id);
        Assert.Contains(entity.Components, value => value.DefinitionId == "dnd2024.item.physical");
        Assert.Contains(entity.Components, value => value.DefinitionId == "dnd2024.item.weapon");
        Assert.Contains(entity.Components, value => value.DefinitionId == "dnd2024.item.equippable");
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, entity.Id, entity.Name);
        foreach (var component in entity.Components)
            await harness.AddApplicationComponentAsync(entity.Id, component.DefinitionId, component.Data);
        await harness.AddPhysicalItemAsync("item.catalog.club", "Club", entity.Id, "subject.high");

        var equipped = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.item.equip", new Dictionary<string, string>
            {
                ["item"] = "item.catalog.club",
                ["holder"] = "subject.high"
            }, "{\"slotIds\":[\"dnd2024.equipment-slot.main-hand\"]}", 0,
            "e123456789abcdef0123456789abcdee"));
        var equipment = await harness.EvaluateRolesAsync("dnd2024.mechanic.item.equipment.read",
            new Dictionary<string, string> { ["item"] = "item.catalog.club" }, "{}", 0);
        var burden = await harness.EvaluateRolesAsync("dnd2024.mechanic.item-burden.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, equipped.Disposition);
        Assert.True(equipment.Ok, equipment.Run?.Error);
        Assert.Contains("\"definitionId\":\"dnd2024.equipment.weapon.club\"",
            equipment.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Contains("\"entityId\":\"dnd2024.equipment-slot.main-hand\"",
            equipment.Run.Output.Data, StringComparison.Ordinal);
        Assert.True(burden.Ok, burden.Run?.Error);
        Assert.Contains("\"mass\":{\"dimension\":\"mass\",\"value\":{\"numerator\":45359237,\"denominator\":50000000}",
            burden.Run!.Output.Data, StringComparison.Ordinal);
        Assert.Empty(equipment.Run.Output.Effects);
        Assert.Empty(burden.Run.Output.Effects);
    }

    [Fact]
    public async Task Derived_inventory_readers_fail_closed_on_visible_item_missing_required_quantity()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string definitionId = "dnd2024.item.invalid-stack.v1";
        await harness.AddItemDefinitionAsync(definitionId, "Invalid stack definition",
            FungibleItemDefinition());
        await harness.AddPhysicalItemAsync("item.invalid-stack", "Invalid Stack", definitionId,
            "subject.high");
        var quantity = (await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
            "item.invalid-stack", "dnd2024.item.quantity"))!;
        Assert.True(await harness.Entities.RemoveComponentAsync(DndHarness.StateSpaceId,
            "item.invalid-stack", quantity.Type, quantity.Revision));

        var inventory = await harness.EvaluateRolesAsync("dnd2024.mechanic.inventory.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);
        var burden = await harness.EvaluateRolesAsync("dnd2024.mechanic.item-burden.read",
            new Dictionary<string, string> { ["root"] = "subject.high" }, "{}", 0);

        Assert.False(inventory.Ok);
        Assert.False(burden.Ok);
        Assert.Empty(inventory.Run!.Output.Effects);
        Assert.Empty(burden.Run!.Output.Effects);
    }

    [Fact]
    public async Task Character_experience_records_corrects_replays_and_derives_only_next_level_threshold()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddProficiencyStateAsync("subject.high", 1, []);
        var record = harness.ActionFor("dnd2024.mechanic.character-experience.write", "subject.high",
            "{\"mode\":\"record\",\"total\":250}", 0,
            "c123456789abcdef0123456789abcdee");
        var recorded = await harness.Runner.RunAsync(record);
        var replay = await harness.Runner.RunAsync(record);
        var corrected = await harness.Runner.RunAsync(harness.ActionFor(
            "dnd2024.mechanic.character-experience.write", "subject.high",
            "{\"mode\":\"correct\",\"total\":300}", 0,
            "d123456789abcdef0123456789abcdee"));
        var read = await harness.EvaluateAsync("subject.high", "{}", 0,
            "dnd2024.mechanic.character-experience.read");
        var unknown = await harness.EvaluateAsync("subject.low", "{}", 0,
            "dnd2024.mechanic.character-experience.read");

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, corrected.Disposition);
        Assert.Contains("\"status\":\"eligible-for-next-level\"", read.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.Contains("\"nextThreshold\":300", read.Run.Output.Data, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"unknown\"", unknown.Run!.Output.Data,
            StringComparison.Ordinal);
        var experience = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.character.experience");
        Assert.Equal("{\"total\":300}", experience!.ValueJson);
        Assert.Empty(read.Run.Output.Effects);
    }

    [Fact]
    public async Task Activated_fighter_progression_is_closed_schema_valid_and_consumed_by_existing_reader()
    {
        var root = RepositoryRoot();
        var contentRoot = Path.Combine(root, "catalog", "applications", "dnd2024", "content",
            "entities", "character-progression");
        var paths = Directory.GetFiles(contentRoot, "dnd2024.content.*.json")
            .Where(path => Path.GetFileName(path) == "dnd2024.content.class.fighter.v1.json"
                || Path.GetFileName(path).StartsWith("dnd2024.content.feature.fighter.",
                    StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(6, paths.Length);

        var validator = new BoundedJsonSchemaValidator();
        var contentSchema = await File.ReadAllTextAsync(Path.Combine(root, "catalog", "applications",
            "dnd2024", "components", "dnd2024.character.content-definition.schema.json"));
        var contentCompilation = validator.Compile(contentSchema);
        Assert.True(contentCompilation.IsAccepted, string.Join("; ", contentCompilation.Diagnostics));
        var progressionSchema = await File.ReadAllTextAsync(Path.Combine(root, "catalog", "applications",
            "dnd2024", "components", "dnd2024.class-progression.schema.json"));
        var progressionCompilation = validator.Compile(progressionSchema);
        Assert.True(progressionCompilation.IsAccepted,
            string.Join("; ", progressionCompilation.Diagnostics));

        var expectedFeatures = new HashSet<string>(StringComparer.Ordinal)
        {
            "dnd2024.content.feature.fighter.action-surge.v1",
            "dnd2024.content.feature.fighter.fighting-style.v1",
            "dnd2024.content.feature.fighter.second-wind.v1",
            "dnd2024.content.feature.fighter.tactical-mind.v1",
            "dnd2024.content.feature.fighter.weapon-mastery.v1"
        };
        await using var harness = await DndHarness.CreateAsync();
        EntityFile? fighter = null;
        var actualFeatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            Assert.Contains(relative, harness.ActiveSourcePaths);
            var entity = EntityFile.Parse(await File.ReadAllTextAsync(path), relative);
            var content = Assert.Single(entity.Components, value =>
                value.DefinitionId == "dnd2024.character.content-definition");
            var contentValidation = validator.Validate(contentCompilation.ProfileId,
                contentCompilation.NormalizedSchema, content.Data);
            Assert.Equal(SchemaValueStatus.Valid, contentValidation.Status);
            using var contentJson = JsonDocument.Parse(content.Data);
            var kind = contentJson.RootElement.GetProperty("kind").GetString();
            Assert.Equal("active", contentJson.RootElement.GetProperty("status").GetString());

            await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, entity.Id, entity.Name);
            await harness.AddApplicationComponentAsync(entity.Id, content.DefinitionId, content.Data);
            var progression = entity.Components.SingleOrDefault(value =>
                value.DefinitionId == "dnd2024.class-progression");
            if (kind == "class")
            {
                fighter = entity;
                Assert.NotNull(progression);
                var progressionValidation = validator.Validate(progressionCompilation.ProfileId,
                    progressionCompilation.NormalizedSchema, progression!.Data);
                Assert.Equal(SchemaValueStatus.Valid, progressionValidation.Status);
                await harness.AddApplicationComponentAsync(entity.Id, progression.DefinitionId,
                    progression.Data);
            }
            else
            {
                Assert.Equal("feature", kind);
                Assert.Null(progression);
                actualFeatures.Add(entity.Id);
            }
        }

        Assert.NotNull(fighter);
        Assert.True(expectedFeatures.SetEquals(actualFeatures));
        var roles = new Dictionary<string, string> { ["class"] = fighter!.Id };
        var level1 = await harness.EvaluateRolesAsync("dnd2024.mechanic.class-progression.read",
            roles, "{\"classLevel\":1}", 0);
        var level2 = await harness.EvaluateRolesAsync("dnd2024.mechanic.class-progression.read",
            roles, "{\"classLevel\":2}", long.MaxValue);
        var level3 = await harness.EvaluateRolesAsync("dnd2024.mechanic.class-progression.read",
            roles, "{\"classLevel\":3}", 0);

        Assert.True(level1.Ok, level1.Run?.Error);
        Assert.True(level2.Ok, level2.Run?.Error);
        Assert.True(level3.Ok, level3.Run?.Error);
        using var level1Json = JsonDocument.Parse(level1.Run!.Output.Data);
        using var level2Json = JsonDocument.Parse(level2.Run!.Output.Data);
        using var level3Json = JsonDocument.Parse(level3.Run!.Output.Data);
        var level1Entitlements = level1Json.RootElement.GetProperty("featureEntitlements")
            .EnumerateArray().ToArray();
        var level2Entitlements = level2Json.RootElement.GetProperty("featureEntitlements")
            .EnumerateArray().ToArray();
        Assert.Equal(new[]
        {
            "dnd2024.content.feature.fighter.fighting-style.v1",
            "dnd2024.content.feature.fighter.second-wind.v1",
            "dnd2024.content.feature.fighter.weapon-mastery.v1"
        }, level1Entitlements.Select(value => value.GetProperty("definitionId").GetString()));
        Assert.Equal(new[]
        {
            "dnd2024.content.feature.fighter.action-surge.v1",
            "dnd2024.content.feature.fighter.tactical-mind.v1"
        }, level2Entitlements.Select(value => value.GetProperty("definitionId").GetString()));
        Assert.All(level1Entitlements.Concat(level2Entitlements), value =>
            Assert.Equal("unimplemented", value.GetProperty("behaviorStatus").GetString()));
        Assert.Equal("unsupported-level", level3Json.RootElement.GetProperty("status").GetString());
        Assert.Empty(level1.Run.Output.Effects);
        Assert.Empty(level1.Run.Output.Events);
        Assert.Empty(level1.Run.Output.Notifications);
        Assert.Empty(level2.Run.Output.Effects);
        Assert.Empty(level3.Run.Output.Effects);
    }

    [Fact]
    public async Task Class_progression_reports_canonical_unimplemented_entitlements_and_source_mismatch()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string classId = "dnd2024.content.class.fighter.v1";
        const string locator = "Classes > Fighter PDF page 60";
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, classId, "Fighter");
        await harness.AddApplicationComponentAsync(classId, "dnd2024.character.content-definition",
            "{\"kind\":\"class\",\"contentKey\":\"fighter\",\"contentVersion\":1,\"status\":\"active\",\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"" + locator + "\"}}");
        var progression =
            "{\"hitDieSides\":10,\"fixedHitPointGainBeforeConstitution\":6,\"levels\":[{\"classLevel\":1,\"featureDefinitionIds\":[],\"choiceSetDefinitionIds\":[]},{\"classLevel\":2,\"featureDefinitionIds\":[\"dnd2024.content.feature.action-surge.v1\"],\"choiceSetDefinitionIds\":[]}],\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"" + locator + "\"}}";
        await harness.AddApplicationComponentAsync(classId, "dnd2024.class-progression", progression);
        var roles = new Dictionary<string, string> { ["class"] = classId };

        var supported = await harness.EvaluateRolesAsync("dnd2024.mechanic.class-progression.read",
            roles, "{\"classLevel\":2}", 0);
        var unsupported = await harness.EvaluateRolesAsync("dnd2024.mechanic.class-progression.read",
            roles, "{\"classLevel\":3}", 0);
        await harness.ReplaceApplicationComponentRawAsync(classId, "dnd2024.class-progression",
            progression.Replace(locator, "Classes > Fighter PDF page 61", StringComparison.Ordinal));
        var mismatch = await harness.EvaluateRolesAsync("dnd2024.mechanic.class-progression.read",
            roles, "{\"classLevel\":2}", 0);

        Assert.Contains("\"status\":\"supported\"", supported.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.Contains("\"behaviorStatus\":\"unimplemented\"", supported.Run.Output.Data,
            StringComparison.Ordinal);
        Assert.Contains("\"status\":\"unsupported-level\"", unsupported.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.Contains("\"problem\":\"source-mismatch\"", mismatch.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.Empty(supported.Run.Output.Effects);
    }

    public static TheoryData<string, int, string[], string[], string[], string[], string[], bool>
        BasicClassCreationCases => new()
        {
            { "barbarian", 12, ["str", "con"], ["perception", "survival"], ["simple", "martial"], [], ["light", "medium", "shield"], false },
            { "bard", 8, ["dex", "cha"], ["arcana", "perception", "persuasion"], ["simple"], [], ["light"], true },
            { "cleric", 8, ["wis", "cha"], ["insight", "religion"], ["simple"], [], ["light", "medium", "shield"], true },
            { "druid", 8, ["int", "wis"], ["nature", "perception"], ["simple"], [], ["light", "shield"], true },
            { "fighter", 10, ["str", "con"], ["perception", "survival"], ["simple", "martial"], [], ["light", "medium", "heavy", "shield"], false },
            { "monk", 8, ["str", "dex"], ["acrobatics", "insight"], ["simple"], ["light"], [], false },
            { "paladin", 10, ["wis", "cha"], ["persuasion", "religion"], ["simple", "martial"], [], ["light", "medium", "heavy", "shield"], true },
            { "ranger", 10, ["str", "dex"], ["nature", "perception", "stealth"], ["simple", "martial"], [], ["light", "medium", "shield"], true },
            { "rogue", 8, ["dex", "int"], ["acrobatics", "investigation", "sleight-of-hand", "stealth"], ["simple"], ["finesse", "light"], ["light"], false },
            { "sorcerer", 6, ["con", "cha"], ["arcana", "persuasion"], ["simple"], [], [], true },
            { "warlock", 8, ["wis", "cha"], ["arcana", "investigation"], ["simple"], [], ["light"], true },
            { "wizard", 6, ["int", "wis"], ["arcana", "investigation"], ["simple"], [], [], true }
        };

    public static TheoryData<string, string, string, int, int, int, int, int>
        BasicClassSpellcastingCases => new()
        {
            { "bard", "full", "cha", 2, 4, 0, 2, 1 },
            { "cleric", "full", "wis", 3, 4, 0, 2, 1 },
            { "druid", "full", "wis", 2, 4, 0, 2, 1 },
            { "paladin", "half", "cha", 0, 2, 0, 2, 1 },
            { "ranger", "half", "wis", 0, 2, 0, 2, 1 },
            { "sorcerer", "full", "cha", 4, 2, 0, 2, 1 },
            { "warlock", "pact", "cha", 2, 2, 0, 1, 1 },
            { "wizard", "full", "int", 3, 4, 6, 2, 1 }
        };

    public static TheoryData<string, string, string[]> BasicClassPrimaryAbilityCases => new()
    {
        { "barbarian", "all", ["str"] },
        { "bard", "all", ["cha"] },
        { "cleric", "all", ["wis"] },
        { "druid", "all", ["wis"] },
        { "fighter", "one-of", ["str", "dex"] },
        { "monk", "all", ["dex", "wis"] },
        { "paladin", "all", ["str", "cha"] },
        { "ranger", "all", ["dex", "wis"] },
        { "rogue", "all", ["dex"] },
        { "sorcerer", "all", ["cha"] },
        { "warlock", "all", ["cha"] },
        { "wizard", "all", ["int"] }
    };

    public static TheoryData<string, int> BasicClassStartingCashCases => new()
    {
        { "barbarian", 75 },
        { "bard", 90 },
        { "cleric", 110 },
        { "druid", 50 },
        { "fighter", 155 },
        { "monk", 50 },
        { "paladin", 150 },
        { "ranger", 150 },
        { "rogue", 100 },
        { "sorcerer", 50 },
        { "warlock", 100 },
        { "wizard", 55 }
    };

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"entitlements\":{}}")]
    [InlineData("{\"entitlements\":[{\"featureRef\":{\"entityId\":\"dnd2024.feat.alert\"},\"grantedByRef\":{\"entityId\":\"dnd2024.content.background.criminal.v1\"},\"grantKind\":\"origin-feat\",\"configurationKey\":\"default\",\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Feats > Alert\"},\"behaviorStatus\":\"implemented\"}]}")]
    [InlineData("{\"entitlements\":[{\"featureRef\":{\"entityId\":\"dnd2024.feat.alert\"},\"grantedByRef\":{\"entityId\":\"dnd2024.content.background.criminal.v1\"},\"grantKind\":\"origin-feat\",\"configurationKey\":\"default\",\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Feats > Alert\"}},{\"featureRef\":{\"entityId\":\"dnd2024.feat.alert\"},\"grantedByRef\":{\"entityId\":\"dnd2024.content.background.criminal.v1\"},\"grantKind\":\"origin-feat\",\"configurationKey\":\"default\",\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Feats > Alert\"}}]}")]
    [InlineData("{\"entitlements\":[{\"featureRef\":{\"entityId\":\"dnd2024.content.feature.fighter.second-wind.v1\"},\"grantedByRef\":{\"entityId\":\"dnd2024.content.class.fighter.v1\"},\"grantKind\":\"origin-feat\",\"classLevel\":1,\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Classes > Fighter\"}}]}")]
    [InlineData("{\"entitlements\":[{\"featureRef\":{\"entityId\":\"dnd2024.content.feature.fighter.second-wind.v1\"},\"grantedByRef\":{\"entityId\":\"dnd2024.content.class.fighter.v1\"},\"grantKind\":\"class-feature\",\"classLevel\":1,\"sourceRef\":{\"sourceId\":\"drifted source\",\"locator\":\"Classes > Fighter\"}}]}")]
    public async Task Character_feature_entitlement_schema_rejects_malformed_duplicate_extra_or_drifted_state(
        string valueJson)
    {
        var schema = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "catalog",
            "applications", "dnd2024", "components",
            "dnd2024.character.feature-entitlements.schema.json"));
        var validator = new BoundedJsonSchemaValidator();
        var compilation = validator.Compile(schema);

        Assert.True(compilation.IsAccepted, string.Join("; ", compilation.Diagnostics));
        Assert.Equal(SchemaValueStatus.Invalid, validator.Validate(
            compilation.ProfileId, compilation.NormalizedSchema, valueJson).Status);
    }

    [Fact]
    public async Task Armor_training_owner_records_reads_corrects_and_replays_canonical_membership()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string actorId = "actor.armor-training.owner";
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, actorId, "Armor Owner");
        var roles = new Dictionary<string, string> { ["subject"] = actorId };
        var absent = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.armor-training.read", roles, "{}", 0);
        var record = harness.ActionForRoles("dnd2024.mechanic.armor-training.write", roles,
            "{\"mode\":\"record\",\"categories\":[\"shield\",\"light\",\"medium\"]}", 0,
            "cc3e1000000000000000000000000001");
        var recorded = await harness.Runner.RunAsync(record);
        var replay = await harness.Runner.RunAsync(record);
        var read = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.armor-training.read", roles, "{}", 0);
        var corrected = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.armor-training.write", roles,
            "{\"mode\":\"correct\",\"categories\":[]}", 0,
            "cc3e1000000000000000000000000002"));

        Assert.True(absent.Ok, absent.Run?.Error);
        Assert.Contains("\"problem\":\"absent\"", absent.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.Empty(absent.Run.Output.Effects);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, recorded.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, corrected.Disposition);
        Assert.True(read.Ok, read.Run?.Error);
        using var readData = JsonDocument.Parse(read.Run!.Output.Data);
        Assert.Equal(new[] { "light", "medium", "shield" }, readData.RootElement
            .GetProperty("categories").EnumerateArray().Select(value => value.GetString()));
        using var state = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature.proficiencies"))!.ValueJson);
        Assert.Empty(ReadArmorTrainingIds(state.RootElement));
        Assert.Contains("armor-training", state.RootElement.GetProperty("recordedFamilies")
            .EnumerateArray().Select(value => value.GetString()));
    }

    [Theory]
    [InlineData("{\"mode\":\"record\",\"categories\":[\"light\",\"light\"]}")]
    [InlineData("{\"mode\":\"record\",\"categories\":[\"cloth\"]}")]
    [InlineData("{\"mode\":\"correct\",\"categories\":[\"light\"]}")]
    [InlineData("{\"mode\":\"record\",\"categories\":[\"light\"],\"sourceRef\":{}}")]
    public async Task Armor_training_owner_rejects_invalid_or_wrong_state_input_unchanged(string input)
    {
        await using var harness = await DndHarness.CreateAsync();
        const string actorId = "actor.armor-training.invalid";
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, actorId, "Invalid Armor");
        var roles = new Dictionary<string, string> { ["subject"] = actorId };

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.armor-training.write", roles, input, 0,
            "cc3e1000000000000000000000000003"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature.proficiencies"));
    }

    [Fact]
    public async Task Armor_training_owner_rejects_record_over_existing_and_invalid_prior_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        const string actorId = "actor.armor-training.prior";
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, actorId, "Prior Armor");
        await harness.AddApplicationComponentAsync(actorId, "dnd2024.creature.proficiencies",
            "{\"categories\":[\"light\"],\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Rules Glossary > Armor Training\"}}");
        var roles = new Dictionary<string, string> { ["subject"] = actorId };
        var duplicateRecord = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.armor-training.write", roles,
            "{\"mode\":\"record\",\"categories\":[\"light\"]}", 0,
            "cc3e1000000000000000000000000004"));
        await harness.ReplaceApplicationComponentRawAsync(actorId, "dnd2024.creature.proficiencies",
            "{\"entries\":{\"dnd2024.equipment.armor-category.light\":{\"rankRef\":{\"entityId\":\"dnd2024.vocabulary.proficiency-rank.expertise\"},\"sourceRefs\":[{\"entityId\":\"dnd2024.source.srd-5.2.1\"}]}},\"recordedFamilies\":[\"armor-training\"]}");
        var invalidRead = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.armor-training.read", roles, "{}", 0);
        var correction = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.armor-training.write", roles,
            "{\"mode\":\"correct\",\"categories\":[\"light\"]}", 0,
            "cc3e1000000000000000000000000005"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, duplicateRecord.Disposition);
        Assert.True(invalidRead.Ok, invalidRead.Run?.Error);
        Assert.Contains("\"problem\":\"invalid\"", invalidRead.Run!.Output.Data,
            StringComparison.Ordinal);
        Assert.Empty(invalidRead.Run.Output.Effects);
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, correction.Disposition);
    }

    [Fact]
    public async Task Basic_character_creation_commits_core_state_participation_pending_ledger_and_replays()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.basic.aric";
        const string worldId = "world.character-creation.fixture";
        var roles = BasicCreationRoles(worldId, "dnd2024.content.species.human.v1");
        const string input =
            "{\"characterId\":\"actor.basic.aric\",\"name\":\"Aric\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"}}";

        var evaluated = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.character.basic.create", roles, input, 0);
        var request = harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles, input, 0,
            "a123456789abcdef0123456789abcdf0");
        var created = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);

        Assert.True(evaluated.Ok, evaluated.Run?.Error ?? string.Join("; ", evaluated.Problems));
        Assert.Equal(21, evaluated.Run!.Output.Effects.Count);
        Assert.Empty(evaluated.Run.Output.Events);
        Assert.Empty(evaluated.Run.Output.Notifications);
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, created.Disposition);
        Assert.Equal(21, created.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.NotNull(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));

        using var abilities = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature.ability-scores"))!.ValueJson);
        var abilityScores = abilities.RootElement.GetProperty("scores");
        Assert.Equal(17, abilityScores.GetProperty("dnd2024.vocabulary.ability.strength").GetInt32());
        Assert.Equal(14, abilityScores.GetProperty("dnd2024.vocabulary.ability.dexterity").GetInt32());
        Assert.Equal(14, abilityScores.GetProperty("dnd2024.vocabulary.ability.constitution").GetInt32());
        using var hitPoints = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature.hit-points"))!.ValueJson);
        Assert.Equal(12, hitPoints.RootElement.GetProperty("current").GetInt32());
        Assert.Equal(12, hitPoints.RootElement.GetProperty("maximum").GetInt32());
        var armorClass = await harness.EvaluateAsync(actorId, "{}", 0,
            "dnd2024.mechanic.armor-class.read");
        Assert.True(armorClass.Ok, armorClass.Run?.Error);
        using var armorClassData = JsonDocument.Parse(armorClass.Run!.Output.Data);
        Assert.Equal(12, armorClassData.RootElement.GetProperty("armorClass").GetInt32());
        var level = await harness.EvaluateAsync(actorId, "{}", 0,
            "dnd2024.mechanic.character-level.read");
        Assert.True(level.Ok, level.Run?.Error);
        Assert.Contains("\"totalLevel\":1", level.Run!.Output.Data, StringComparison.Ordinal);
        using var proficiencies = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature.proficiencies"))!.ValueJson);
        Assert.Equal(new[] { "light", "medium", "heavy", "shield" },
            ReadArmorTrainingIds(proficiencies.RootElement));
        Assert.Equal(new[] { "athletics", "intimidation", "perception", "survival" },
            ReadSkillIds(proficiencies.RootElement).ToArray());
        Assert.Equal(new[] { "str", "con" }, ReadSavingThrowIds(proficiencies.RootElement).ToArray());
        Assert.Equal(new[] { "simple", "martial" },
            ReadWeaponCategoryIds(proficiencies.RootElement).ToArray());
        Assert.Empty(ReadWeaponPropertyIds(proficiencies.RootElement));
        using var languages = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature.languages"))!.ValueJson);
        Assert.Equal(new[] { "common" }, ReadLanguageIds(languages.RootElement).ToArray());
        using var featureEntitlements = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.character.feature-entitlements"))!.ValueJson);
        var entitlements = featureEntitlements.RootElement.GetProperty("entitlements")
            .EnumerateArray().ToArray();
        Assert.Equal(4, entitlements.Length);
        Assert.Equal(new[]
        {
            "dnd2024.content.feature.fighter.fighting-style.v1",
            "dnd2024.content.feature.fighter.second-wind.v1",
            "dnd2024.content.feature.fighter.weapon-mastery.v1"
        }, entitlements.Where(value => value.GetProperty("grantKind").GetString() == "class-feature")
            .Select(value => value.GetProperty("featureRef").GetProperty("entityId").GetString()));
        var originEntitlement = Assert.Single(entitlements, value =>
            value.GetProperty("grantKind").GetString() == "origin-feat");
        Assert.Equal("dnd2024.feat.savage-attacker",
            originEntitlement.GetProperty("featureRef").GetProperty("entityId").GetString());
        Assert.Equal("dnd2024.content.background.soldier.v1",
            originEntitlement.GetProperty("grantedByRef").GetProperty("entityId").GetString());
        Assert.Equal("default", originEntitlement.GetProperty("configurationKey").GetString());
        Assert.False(originEntitlement.TryGetProperty("behaviorStatus", out _));

        using var record = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.character-creation-record"))!.ValueJson);
        Assert.Equal("basic-playable", record.RootElement.GetProperty("status").GetString());
        Assert.Equal("soldier-fighter-level-1-v1",
            record.RootElement.GetProperty("templateKey").GetString());
        Assert.Equal(13, record.RootElement.GetProperty("appliedComponentIds").GetArrayLength());
        Assert.Contains("dnd2024.character.origin-selections",
            record.RootElement.GetProperty("appliedComponentIds").EnumerateArray()
                .Select(value => value.GetString()));
        using var origin = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.character.origin-selections"))!.ValueJson);
        Assert.Equal("dnd2024.content.species.human.v1",
            origin.RootElement.GetProperty("speciesRef").GetProperty("entityId").GetString());
        Assert.Equal("dnd2024.content.background.soldier.v1",
            origin.RootElement.GetProperty("backgroundRef").GetProperty("entityId").GetString());
        Assert.DoesNotContain("dnd2024.combat.turn-budget",
            record.RootElement.GetProperty("appliedComponentIds").EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Contains("dnd2024.creature.proficiencies",
            record.RootElement.GetProperty("appliedComponentIds").EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Contains("dnd2024.character.feature-entitlements",
            record.RootElement.GetProperty("appliedComponentIds").EnumerateArray()
                .Select(value => value.GetString()));
        var pending = record.RootElement.GetProperty("unresolvedEntitlements")
            .EnumerateArray().ToArray();
        Assert.Equal(11, pending.Length);
        Assert.DoesNotContain(pending, value => value.GetProperty("entitlementKey").GetString()!
            .StartsWith("armor-training:", StringComparison.Ordinal));
        Assert.Contains(pending, value => value.GetProperty("ownerDefinitionId").GetString() ==
            "dnd2024.content.species.human.v1" &&
            value.GetProperty("entitlementKey").GetString() == "trait:resourceful");
        Assert.Contains(pending, value => value.GetProperty("ownerDefinitionId").GetString() ==
            "dnd2024.content.feature.fighter.second-wind.v1" &&
            value.GetProperty("reason").GetString() == "behavior-unimplemented");
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.character.heroic-inspiration"));
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.equipment-state"));
        Assert.DoesNotContain("tool", proficiencies.RootElement.GetProperty("recordedFamilies")
            .EnumerateArray().Select(value => value.GetString()));

        var participationId = worldId + ".participation." + actorId;
        using var participation = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, participationId,
            "game.core.campaign.character-participation"))!.ValueJson);
        Assert.Equal("active", participation.RootElement.GetProperty("status").GetString());
        Assert.Equal("{}", (await harness.Edges.GetRelationshipAsync(DndHarness.StateSpaceId,
            worldId, participationId,
            "dnd2024.campaign.has-character-participation"))!.DataJson);
        Assert.Equal("{}", (await harness.Edges.GetRelationshipAsync(DndHarness.StateSpaceId,
            participationId, actorId,
            "dnd2024.campaign.character-participation.for-actor"))!.DataJson);

        Assert.NotNull(await harness.ReadEntityFreshAsync(actorId));
        Assert.NotNull(await harness.ReadRelationshipFreshAsync(worldId, participationId,
            "dnd2024.campaign.has-character-participation"));
        var sheet = await harness.EvaluateRolesAsync("dnd2024.mechanic.character-sheet.read",
            new Dictionary<string, string> { ["subject"] = actorId }, "{}", 0);
        var projectedSheet = await harness.EvaluateRolesAsync(
            "dnd2024.mechanic.character-sheet.project",
            new Dictionary<string, string> { ["subject"] = actorId }, "{}", 0);
        var registeredSheet = await harness.ReadModels.ReadAsync(new(
            DndHarness.StateSpaceId,
            ApplicationIdentifier.Parse("dnd2024"),
            "dnd2024.query.character-sheet",
            new Dictionary<string, string> { ["subject"] = actorId }));
        var registeredSheetV2 = await harness.ReadModels.ReadAsync(new(
            DndHarness.StateSpaceId,
            ApplicationIdentifier.Parse("dnd2024"),
            "dnd2024.query.character-sheet-v2",
            new Dictionary<string, string> { ["subject"] = actorId }));
        // Dossier reads require canonical metadata; legacy feat fixtures do not provide it.
        var incompleteDossier = await harness.EvaluateRolesAsync("dnd2024.mechanic.character-dossier-v1.project",
            new Dictionary<string, string> { ["subject"] = actorId }, "{}", 0, MechanicAudienceContext.Player);
        Assert.False(incompleteDossier.Ok);
        Assert.Contains(incompleteDossier.Problems, value => value.StartsWith("COMPONENT_REFERENCE_TARGET_MISSING:"));
        await harness.AddApplicationComponentAsync("dnd2024.feat.savage-attacker", "dnd2024.character.content-definition",
            """{"kind":"feature","contentKey":"savage-attacker","contentVersion":1,"status":"active","sourceRef":{"sourceId":"fixture","locator":"Dossier metadata fixture"}}""");
        var dossierProjection = await harness.EvaluateRolesAsync("dnd2024.mechanic.character-dossier-v1.project",
            new Dictionary<string, string> { ["subject"] = actorId }, "{}", 0, MechanicAudienceContext.Player);
        Assert.True(dossierProjection.Ok, string.Join("; ", dossierProjection.Problems) + dossierProjection.Run?.Error);
        var registeredDossier = await harness.ReadModels.ReadAsync(new(
            DndHarness.StateSpaceId, ApplicationIdentifier.Parse("dnd2024"),
            "dnd2024.query.character-dossier-v1",
            new Dictionary<string, string> { ["subject"] = actorId }, MechanicAudienceContext.Player));
        using (var dossier = JsonDocument.Parse(registeredDossier.DataJson))
            Assert.Equal(actorId, dossier.RootElement.GetProperty("sheet").GetProperty("subject").GetProperty("id").GetString());
        var initiative = await harness.EvaluateRolesAsync("dnd2024.mechanic.initiative.roll",
            new Dictionary<string, string> { ["subject"] = actorId }, "{}", 17);
        Assert.True(sheet.Ok, sheet.Run?.Error);
        Assert.True(projectedSheet.Ok,
            projectedSheet.Run?.Error ?? string.Join("; ", projectedSheet.Problems));
        using (var projected = JsonDocument.Parse(projectedSheet.Run!.Output.Data))
        {
            Assert.Equal(actorId, projected.RootElement.GetProperty("subject").GetProperty("id").GetString());
            Assert.Equal(1, projected.RootElement.GetProperty("level").GetInt32());
            Assert.Equal(2, projected.RootElement.GetProperty("proficiencyBonus").GetInt32());
            Assert.Equal(6, projected.RootElement.GetProperty("abilities").GetArrayLength());
            Assert.Equal(6, projected.RootElement.GetProperty("savingThrows").GetArrayLength());
            Assert.Equal(18, projected.RootElement.GetProperty("skills").GetArrayLength());
            Assert.Equal(12, projected.RootElement.GetProperty("hitPoints").GetProperty("maximum").GetInt32());
            Assert.Equal(12, projected.RootElement.GetProperty("armorClass").GetProperty("value").GetInt32());
            Assert.Equal(4, projected.RootElement.GetProperty("features").GetArrayLength());
            Assert.Empty(projected.RootElement.GetProperty("inventory").GetProperty("items").EnumerateArray());
        }
        using (var registered = JsonDocument.Parse(registeredSheet.DataJson))
        {
            Assert.Equal(actorId, registered.RootElement.GetProperty("subject").GetProperty("id").GetString());
            Assert.Equal(6, registered.RootElement.GetProperty("abilities").GetArrayLength());
            Assert.Equal(18, registered.RootElement.GetProperty("skills").GetArrayLength());
        }
        using (var registered = JsonDocument.Parse(registeredSheetV2.DataJson))
        {
            Assert.Equal(2, registered.RootElement.GetProperty("version").GetInt32());
            Assert.Equal("Fighter", registered.RootElement.GetProperty("classes")[0]
                .GetProperty("class").GetProperty("label").GetString());
            Assert.Equal("Strength", registered.RootElement.GetProperty("abilities")[0]
                .GetProperty("ability").GetProperty("label").GetString());
            Assert.Equal(0, registered.RootElement.GetProperty("wallet")
                .GetProperty("copperValue").GetInt32());
        }
        Assert.Matches("^[0-9A-F]{64}$", registeredSheet.StateSpaceFingerprint);
        Assert.Matches("^[0-9A-F]{64}$", registeredSheet.ResolutionFingerprint);
        Assert.Matches("^[0-9A-F]{64}$", registeredSheet.OutputSchemaHash);
        Assert.Matches("^[0-9A-F]{64}$", registeredSheet.ResultFingerprint);
        Assert.Matches("^[0-9A-F]{64}$", registeredSheet.SourceRevisionFingerprint);
        Assert.Matches("^[0-9A-F]{64}$", registeredSheetV2.OutputSchemaHash);
        Assert.Matches("^[0-9A-F]{64}$", registeredSheetV2.ResultFingerprint);
        Assert.Matches("^[0-9A-F]{64}$", registeredSheetV2.SourceRevisionFingerprint);
        Assert.True(initiative.Ok, initiative.Run?.Error);
    }

    [Fact]
    public async Task Character_origin_materialization_commits_one_receipted_component_and_replays()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.origin.materialize";
        const string input =
            "{\"characterId\":\"actor.origin.materialize\",\"name\":\"Restored Origin\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"}}";
        var created = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create",
            BasicCreationRoles("world.character-creation.fixture",
                "dnd2024.content.species.human.v1"),
            input, 0, "cc4a0000000000000000000000000000"));
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, created.Disposition);

        var origin = (await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
            actorId, "dnd2024.character.origin-selections"))!;
        Assert.True(await harness.Entities.RemoveComponentAsync(DndHarness.StateSpaceId,
            actorId, origin.Type, origin.Revision));
        var creationRecord = (await harness.Entities.GetComponentAsync(DndHarness.StateSpaceId,
            actorId, "dnd2024.character-creation-record"))!.ValueJson;
        var request = harness.ActionForRoles(
            "dnd2024.mechanic.character.origin.materialize",
            new Dictionary<string, string> { ["subject"] = actorId }, "{}", 0,
            "cc4a1000000000000000000000000000");

        var materialized = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, materialized.Disposition);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.Equal(actorId, Assert.Single(materialized.AffectedEntityIds));
        var receipt = Assert.Single(materialized.EffectReceipts);
        Assert.Equal("component.add", receipt.Type);
        Assert.Equal(actorId, receipt.EntityId);
        Assert.Equal("dnd2024.character.origin-selections", receipt.QualifiedTypeId);
        using var restored = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId,
            "dnd2024.character.origin-selections"))!.ValueJson);
        Assert.Equal("dnd2024.content.species.human.v1",
            restored.RootElement.GetProperty("speciesRef").GetProperty("entityId").GetString());
        Assert.Equal("dnd2024.content.background.soldier.v1",
            restored.RootElement.GetProperty("backgroundRef").GetProperty("entityId").GetString());
        Assert.Equal(creationRecord, (await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId,
            "dnd2024.character-creation-record"))!.ValueJson);
    }

    [Theory]
    [MemberData(nameof(BasicClassCreationCases))]
    public async Task Basic_character_creation_supports_every_srd_level_one_class_model(
        string classKey,
        int hitDieSides,
        string[] savingThrows,
        string[] classSkills,
        string[] weaponCategories,
        string[] restrictedMartialProperties,
        string[] armorTraining,
        bool spellcastingPending)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        var actorId = "actor.basic." + classKey;
        var classId = "dnd2024.content.class." + classKey + ".v1";
        var input = JsonSerializer.Serialize(new
        {
            characterId = actorId,
            name = "Test " + classKey,
            ability = new
            {
                scores = new { str = 15, dex = 14, con = 13, @int = 8, wis = 10, cha = 12 },
                increases = new { str = 2, con = 1 }
            },
            speciesSelection = new { size = "medium" }
        });

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create",
            BasicCreationRoles("world.character-creation.fixture",
                "dnd2024.content.species.human.v1", classId),
            input, 0, "1123456789abcdef0123456789abcdf0"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, result.Disposition);
        using var hitPoints = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature.hit-points"))!.ValueJson);
        Assert.Equal(hitDieSides + 2,
            hitPoints.RootElement.GetProperty("maximum").GetInt32());
        using var proficiencies = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId,
            "dnd2024.creature.proficiencies"))!.ValueJson);
        Assert.Equal(savingThrows, ReadSavingThrowIds(proficiencies.RootElement).ToArray());
        Assert.Equal(new[] { "athletics", "intimidation" }.Concat(classSkills)
                .Order(StringComparer.Ordinal),
            ReadSkillIds(proficiencies.RootElement));
        Assert.Equal(weaponCategories, ReadWeaponCategoryIds(proficiencies.RootElement).ToArray());
        Assert.Equal(restrictedMartialProperties,
            ReadWeaponPropertyIds(proficiencies.RootElement).ToArray());
        Assert.Equal(armorTraining, ReadArmorTrainingIds(proficiencies.RootElement).ToArray());

        using var record = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.character-creation-record"))!.ValueJson);
        Assert.Equal("soldier-" + classKey + "-level-1-v1",
            record.RootElement.GetProperty("templateKey").GetString());
        var selections = record.RootElement.GetProperty("selections");
        Assert.Equal(classId, selections.GetProperty("classDefinitionId").GetString());
        Assert.Equal(classSkills, selections.GetProperty("classSkillChoices").EnumerateArray()
            .Select(value => value.GetString()).ToArray());
        var pending = record.RootElement.GetProperty("unresolvedEntitlements")
            .EnumerateArray().ToArray();
        Assert.Equal(spellcastingPending, pending.Any(value =>
            value.GetProperty("ownerDefinitionId").GetString() == classId &&
            value.GetProperty("entitlementKey").GetString()!.StartsWith(
                "spellcasting:", StringComparison.Ordinal)));
        Assert.Equal(restrictedMartialProperties.Length > 0, pending.Any(value =>
            value.GetProperty("ownerDefinitionId").GetString() == classId &&
            value.GetProperty("entitlementKey").GetString()!.StartsWith(
                "weapon:restricted-martial-attack-enforcement:", StringComparison.Ordinal) &&
            value.GetProperty("reason").GetString() == "behavior-unimplemented"));
        Assert.DoesNotContain(pending, value =>
            value.GetProperty("ownerDefinitionId").GetString() == classId &&
            value.GetProperty("entitlementKey").GetString()!.StartsWith(
                "weapon:", StringComparison.Ordinal) &&
            value.GetProperty("reason").GetString() == "state-owner-unavailable");
        Assert.DoesNotContain(pending, value => value.GetProperty("entitlementKey").GetString()!
            .StartsWith("armor-training:", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(BasicClassSpellcastingCases))]
    public async Task Basic_character_creation_class_models_preserve_exact_level_one_spell_tables(
        string classKey,
        string kind,
        string ability,
        int cantrips,
        int preparedSpells,
        int spellbookSpells,
        int level1Slots,
        int slotLevel)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        var classId = "dnd2024.content.class." + classKey + ".v1";

        using var profile = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, classId,
            "dnd2024.class-creation-profile"))!.ValueJson);
        var spellcasting = profile.RootElement.GetProperty("spellcasting");
        Assert.Equal(kind, spellcasting.GetProperty("kind").GetString());
        Assert.Equal(ability, spellcasting.GetProperty("ability").GetString());
        Assert.Equal(cantrips, spellcasting.GetProperty("cantrips").GetInt32());
        Assert.Equal(preparedSpells, spellcasting.GetProperty("preparedSpells").GetInt32());
        Assert.Equal(spellbookSpells, spellcasting.GetProperty("spellbookSpells").GetInt32());
        Assert.Equal(level1Slots, spellcasting.GetProperty("level1Slots").GetInt32());
        Assert.Equal(slotLevel, spellcasting.GetProperty("slotLevel").GetInt32());
    }

    [Theory]
    [MemberData(nameof(BasicClassPrimaryAbilityCases))]
    public async Task Basic_character_creation_class_models_preserve_primary_ability_meaning(
        string classKey,
        string mode,
        string[] abilities)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        var classId = "dnd2024.content.class." + classKey + ".v1";

        using var profile = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, classId,
            "dnd2024.class-creation-profile"))!.ValueJson);
        var primary = profile.RootElement.GetProperty("primaryAbilities");
        Assert.Equal(mode, primary.GetProperty("mode").GetString());
        Assert.Equal(abilities, primary.GetProperty("abilities").EnumerateArray()
            .Select(value => value.GetString()).ToArray());
    }

    [Theory]
    [MemberData(nameof(BasicClassStartingCashCases))]
    public async Task Basic_character_creation_class_models_declare_exact_starting_cash(
        string classKey,
        int cashGp)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        var classId = "dnd2024.content.class." + classKey + ".v1";

        using var profile = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, classId,
            "dnd2024.class-creation-profile"))!.ValueJson);

        Assert.Equal(cashGp,
            profile.RootElement.GetProperty("startingEquipmentCashGp").GetInt32());
        Assert.Equal("dnd2024.equipment.currency.gold-piece",
            profile.RootElement.GetProperty("startingEquipmentCurrencyDefinitionId").GetString());
    }

    [Fact]
    public async Task Basic_character_creation_background_models_preserve_exact_srd_declarations()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        var cases = new[]
        {
            new
            {
                Key = "acolyte", EligibleAbilities = new[] { "int", "wis", "cha" },
                Skills = new[] { "insight", "religion" }, ToolKind = "fixed",
                ToolValue = "calligraphers-supplies",
                Feat = "dnd2024.feat.magic-initiate", Configuration = "cleric",
                CurrencyGp = 8,
                Entries = new[] { "calligraphers-supplies:1", "book-prayers:1", "holy-symbol:1", "parchment-sheet:10", "robe:1" }
            },
            new
            {
                Key = "criminal", EligibleAbilities = new[] { "dex", "con", "int" },
                Skills = new[] { "sleight-of-hand", "stealth" }, ToolKind = "fixed",
                ToolValue = "thieves-tools", Feat = "dnd2024.feat.alert",
                Configuration = "default", CurrencyGp = 16,
                Entries = new[] { "dagger:2", "thieves-tools:1", "crowbar:1", "pouch:2", "travelers-clothes:1" }
            },
            new
            {
                Key = "sage", EligibleAbilities = new[] { "con", "int", "wis" },
                Skills = new[] { "arcana", "history" }, ToolKind = "fixed",
                ToolValue = "calligraphers-supplies",
                Feat = "dnd2024.feat.magic-initiate", Configuration = "wizard",
                CurrencyGp = 8,
                Entries = new[] { "quarterstaff:1", "calligraphers-supplies:1", "book-history:1", "parchment-sheet:8", "robe:1" }
            },
            new
            {
                Key = "soldier", EligibleAbilities = new[] { "str", "dex", "con" },
                Skills = new[] { "athletics", "intimidation" }, ToolKind = "choice",
                ToolValue = "gaming-set", Feat = "dnd2024.feat.savage-attacker",
                Configuration = "default", CurrencyGp = 14,
                Entries = new[] { "spear:1", "shortbow:1", "arrow:20", "@background-tool:1", "healers-kit:1", "quiver:1", "travelers-clothes:1" }
            }
        };

        foreach (var background in cases)
        {
            var backgroundId = "dnd2024.content.background." + background.Key + ".v1";
            using var abilities = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, backgroundId,
                "dnd2024.background.ability-increase-options"))!.ValueJson);
            Assert.Equal(background.EligibleAbilities,
                abilities.RootElement.GetProperty("eligibleAbilities").EnumerateArray()
                    .Select(value => value.GetString()).ToArray());
            Assert.Equal(new[] { "plus-2-plus-1", "plus-1-each" },
                abilities.RootElement.GetProperty("allowedPatterns").EnumerateArray()
                    .Select(value => value.GetString()).ToArray());

            using var profile = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, backgroundId,
                "dnd2024.background-creation-profile"))!.ValueJson);
            Assert.Equal(background.Key, profile.RootElement.GetProperty("backgroundKey").GetString());
            Assert.Equal(background.Skills,
                profile.RootElement.GetProperty("skillProficiencies").EnumerateArray()
                    .Select(value => value.GetString()).ToArray());
            var tool = profile.RootElement.GetProperty("toolProficiency");
            Assert.Equal(background.ToolKind, tool.GetProperty("kind").GetString());
            Assert.Equal(background.ToolValue,
                tool.GetProperty(background.ToolKind == "fixed" ? "toolId" : "optionFamily").GetString());
            var feat = profile.RootElement.GetProperty("originFeat");
            Assert.Equal(background.Feat, feat.GetProperty("definitionId").GetString());
            Assert.Equal(background.Configuration, feat.GetProperty("configurationKey").GetString());
            var equipment = profile.RootElement.GetProperty("startingEquipment");
            Assert.Equal(background.CurrencyGp, equipment.GetProperty("packageCurrencyGp").GetInt32());
            Assert.Equal(50, equipment.GetProperty("cashAlternativeGp").GetInt32());
            Assert.Equal(background.Entries,
                equipment.GetProperty("packageEntries").EnumerateArray().Select(value =>
                    value.GetProperty("kind").GetString() == "item"
                        ? value.GetProperty("itemKey").GetString() + ":" + value.GetProperty("quantity").GetInt32()
                        : "@" + value.GetProperty("selectionKey").GetString() + ":" + value.GetProperty("quantity").GetInt32())
                    .ToArray());
        }
    }

    [Fact]
    public async Task Basic_character_creation_composes_every_srd_background_and_class_pair()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        var backgrounds = new[]
        {
            new { Key = "acolyte", PlusTwo = "int", PlusOne = "wis", Skills = new[] { "insight", "religion" }, FixedTool = (string?)"calligraphers-supplies", Feat = "dnd2024.feat.magic-initiate", Configuration = "cleric" },
            new { Key = "criminal", PlusTwo = "dex", PlusOne = "con", Skills = new[] { "sleight-of-hand", "stealth" }, FixedTool = (string?)"thieves-tools", Feat = "dnd2024.feat.alert", Configuration = "default" },
            new { Key = "sage", PlusTwo = "int", PlusOne = "wis", Skills = new[] { "arcana", "history" }, FixedTool = (string?)"calligraphers-supplies", Feat = "dnd2024.feat.magic-initiate", Configuration = "wizard" },
            new { Key = "soldier", PlusTwo = "str", PlusOne = "con", Skills = new[] { "athletics", "intimidation" }, FixedTool = (string?)null, Feat = "dnd2024.feat.savage-attacker", Configuration = "default" }
        };
        var classKeys = new[]
        {
            "barbarian", "bard", "cleric", "druid", "fighter", "monk", "paladin", "ranger",
            "rogue", "sorcerer", "warlock", "wizard"
        };
        var operation = 0;

        foreach (var background in backgrounds)
        foreach (var classKey in classKeys)
        {
            operation++;
            var actorId = "actor.basic." + background.Key + "." + classKey;
            var backgroundId = "dnd2024.content.background." + background.Key + ".v1";
            var classId = "dnd2024.content.class." + classKey + ".v1";
            using var classProfile = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, classId, "dnd2024.class-creation-profile"))!.ValueJson);
            var skillsProfile = classProfile.RootElement.GetProperty("skills");
            var classChoiceCount = skillsProfile.GetProperty("choiceCount").GetInt32();
            var classOptions = skillsProfile.GetProperty("options").EnumerateArray()
                .Select(value => value.GetString()!).ToHashSet(StringComparer.Ordinal);
            var classFixedTools = classProfile.RootElement.GetProperty("tools").GetProperty("fixed")
                .EnumerateArray().Select(value => value.GetString()!).ToArray();
            var hasClassToolChoice = classProfile.RootElement.GetProperty("tools")
                .GetProperty("choiceGroups").GetArrayLength() > 0;
            var expectedArmorTraining = classProfile.RootElement.GetProperty("armorTraining")
                .EnumerateArray().Select(value => value.GetString()).ToArray();
            var expectedWeaponRestrictions = classProfile.RootElement.GetProperty("weapons")
                .GetProperty("restrictedMartialProperties").EnumerateArray()
                .Select(value => value.GetString()).ToArray();
            var input = JsonSerializer.Serialize(new
            {
                characterId = actorId,
                name = background.Key + " " + classKey,
                ability = new
                {
                    scores = new { str = 15, dex = 14, con = 13, @int = 8, wis = 10, cha = 12 },
                    increases = new Dictionary<string, int>
                    {
                        [background.PlusTwo] = 2,
                        [background.PlusOne] = 1
                    }
                },
                speciesSelection = new { size = "medium" }
            });

            var result = await harness.Runner.RunAsync(harness.ActionForRoles(
                "dnd2024.mechanic.character.basic.create",
                BasicCreationRoles("world.character-creation.fixture",
                    "dnd2024.content.species.human.v1", classId, backgroundId),
                input, 0, operation.ToString("x32")));

            Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, result.Disposition);
            using var record = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId, "dnd2024.character-creation-record"))!.ValueJson);
            Assert.Equal(background.Key + "-" + classKey + "-level-1-v1",
                record.RootElement.GetProperty("templateKey").GetString());
            var selections = record.RootElement.GetProperty("selections");
            Assert.Equal(backgroundId, selections.GetProperty("backgroundDefinitionId").GetString());
            Assert.False(selections.TryGetProperty("classToolChoices", out _));
            Assert.False(selections.TryGetProperty("startingEquipmentChoices", out _));
            Assert.False(record.RootElement.TryGetProperty("createdItemIds", out _));
            Assert.Null(await harness.Entities.GetEntityAsync(
                DndHarness.StateSpaceId, "item.starting-gold." + actorId));
            var classSkills = selections.GetProperty("classSkillChoices").EnumerateArray()
                .Select(value => value.GetString()!).ToArray();
            Assert.Equal(classChoiceCount, classSkills.Length);
            Assert.Equal(classChoiceCount, classSkills.Distinct(StringComparer.Ordinal).Count());
            Assert.All(classSkills, value => Assert.Contains(value, classOptions));
            Assert.DoesNotContain(classSkills, value => background.Skills.Contains(
                value, StringComparer.Ordinal));

            using var proficiencies = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId, "dnd2024.creature.proficiencies"))!.ValueJson);
            Assert.Equal(background.Skills.Concat(classSkills).Order(StringComparer.Ordinal),
                ReadSkillIds(proficiencies.RootElement));
            using var languages = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId, "dnd2024.creature.languages"))!.ValueJson);
            Assert.Equal(new[] { "common" }, ReadLanguageIds(languages.RootElement).ToArray());
            Assert.Equal(expectedArmorTraining,
                ReadArmorTrainingIds(proficiencies.RootElement).ToArray());
            Assert.Equal(expectedWeaponRestrictions,
                ReadWeaponPropertyIds(proficiencies.RootElement).ToArray());

            using var backgroundProfile = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, backgroundId,
                "dnd2024.background-creation-profile"))!.ValueJson);
            using var classProgression = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, classId, "dnd2024.class-progression"))!.ValueJson);
            var expectedClassFeatures = classProgression.RootElement.GetProperty("levels")[0]
                .GetProperty("featureDefinitionIds").EnumerateArray()
                .Select(value => value.GetString()!).ToArray();
            using var featureEntitlements = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId,
                "dnd2024.character.feature-entitlements"))!.ValueJson);
            var entitlements = featureEntitlements.RootElement.GetProperty("entitlements")
                .EnumerateArray().ToArray();
            Assert.Equal(expectedClassFeatures.Length + 1, entitlements.Length);
            Assert.Equal(expectedClassFeatures,
                entitlements.Where(value => value.GetProperty("grantKind").GetString() == "class-feature")
                    .Select(value => value.GetProperty("featureRef").GetProperty("entityId").GetString()));
            Assert.All(entitlements.Where(value =>
                    value.GetProperty("grantKind").GetString() == "class-feature"), value =>
                {
                    Assert.Equal(classId,
                        value.GetProperty("grantedByRef").GetProperty("entityId").GetString());
                    Assert.Equal(1, value.GetProperty("classLevel").GetInt32());
                    Assert.Equal(classProfile.RootElement.GetProperty("sourceRef").GetRawText(),
                        value.GetProperty("sourceRef").GetRawText());
                    Assert.False(value.TryGetProperty("behaviorStatus", out _));
                });
            var originEntitlement = Assert.Single(entitlements, value =>
                value.GetProperty("grantKind").GetString() == "origin-feat");
            Assert.Equal(background.Feat,
                originEntitlement.GetProperty("featureRef").GetProperty("entityId").GetString());
            Assert.Equal(backgroundId,
                originEntitlement.GetProperty("grantedByRef").GetProperty("entityId").GetString());
            Assert.Equal(background.Configuration,
                originEntitlement.GetProperty("configurationKey").GetString());
            Assert.Equal(backgroundProfile.RootElement.GetProperty("sourceRef").GetRawText(),
                originEntitlement.GetProperty("sourceRef").GetRawText());
            Assert.False(originEntitlement.TryGetProperty("behaviorStatus", out _));

            foreach (var entitlement in entitlements.Where(value =>
                         value.GetProperty("grantKind").GetString() == "class-feature"))
            {
                var definitionId = entitlement.GetProperty("featureRef")
                    .GetProperty("entityId").GetString()!;
                using var definition = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                    DndHarness.StateSpaceId, definitionId,
                    "dnd2024.character.content-definition"))!.ValueJson);
                Assert.Equal("feature", definition.RootElement.GetProperty("kind").GetString());
                Assert.Equal("active", definition.RootElement.GetProperty("status").GetString());
                Assert.Equal("dnd2024.source.srd-5.2.1",
                    definition.RootElement.GetProperty("sourceRef").GetProperty("sourceId").GetString());
            }

            var expectedTools = classFixedTools
                .Concat(background.FixedTool is null ? [] : [background.FixedTool])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var toolState = await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId, "dnd2024.creature.proficiencies");
            Assert.NotNull(toolState);
            using var toolProficiencies = JsonDocument.Parse(toolState!.ValueJson);
            if (expectedTools.Length == 0)
            {
                Assert.Empty(ReadToolIds(toolProficiencies.RootElement));
                Assert.DoesNotContain("tool", toolProficiencies.RootElement
                    .GetProperty("recordedFamilies").EnumerateArray()
                    .Select(value => value.GetString()));
            }
            else
            {
                Assert.Equal(expectedTools, ReadToolIds(toolProficiencies.RootElement).ToArray());
            }

            var pending = record.RootElement.GetProperty("unresolvedEntitlements")
                .EnumerateArray().ToArray();
            Assert.Contains(pending, value =>
                value.GetProperty("ownerDefinitionId").GetString() == background.Feat &&
                value.GetProperty("entitlementKey").GetString() ==
                (background.Feat == "dnd2024.feat.alert"
                    ? "behavior:initiative-swap"
                    : background.Configuration == "default"
                    ? "behavior"
                    : "behavior:configuration:" + background.Configuration));
            Assert.All(expectedClassFeatures, featureId => Assert.Contains(pending, value =>
                value.GetProperty("ownerDefinitionId").GetString() == featureId &&
                value.GetProperty("entitlementKey").GetString() == "behavior" &&
                value.GetProperty("reason").GetString() == "behavior-unimplemented"));
            Assert.Contains(pending, value =>
                value.GetProperty("ownerDefinitionId").GetString() == backgroundId &&
                value.GetProperty("entitlementKey").GetString() == "origin-language-choice:2:standard");
            Assert.Contains(pending, value =>
                value.GetProperty("ownerDefinitionId").GetString() == backgroundId &&
                value.GetProperty("entitlementKey").GetString()!.StartsWith(
                    "equipment:starting-package-or-50-gp", StringComparison.Ordinal));
            Assert.Contains(pending, value =>
                value.GetProperty("ownerDefinitionId").GetString() == classId &&
                value.GetProperty("entitlementKey").GetString() ==
                    "equipment:starting-package");
            Assert.Equal(background.Key == "soldier", pending.Any(value =>
                value.GetProperty("ownerDefinitionId").GetString() == backgroundId &&
                value.GetProperty("entitlementKey").GetString() == "tool-choice:1:gaming-set"));
            Assert.Equal(hasClassToolChoice, pending.Any(value =>
                value.GetProperty("ownerDefinitionId").GetString() == classId &&
                value.GetProperty("entitlementKey").GetString()!.StartsWith(
                    "tool-choice:", StringComparison.Ordinal)));
            Assert.Equal(expectedWeaponRestrictions.Length > 0, pending.Any(value =>
                value.GetProperty("ownerDefinitionId").GetString() == classId &&
                value.GetProperty("entitlementKey").GetString()!.StartsWith(
                    "weapon:restricted-martial-attack-enforcement:", StringComparison.Ordinal) &&
                value.GetProperty("reason").GetString() == "behavior-unimplemented"));
            Assert.DoesNotContain(pending, value => value.GetProperty("entitlementKey").GetString()!
                .StartsWith("armor-training:", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Basic_character_creation_cash_alternative_composes_every_background_and_class_pair_and_replays()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        await harness.AddCanonicalGoldDefinitionFixtureAsync();
        var backgrounds = new[]
        {
            new { Key = "acolyte", PlusTwo = "int", PlusOne = "wis" },
            new { Key = "criminal", PlusTwo = "dex", PlusOne = "con" },
            new { Key = "sage", PlusTwo = "int", PlusOne = "wis" },
            new { Key = "soldier", PlusTwo = "str", PlusOne = "con" }
        };
        var classes = new[]
        {
            new { Key = "barbarian", CashGp = 75 },
            new { Key = "bard", CashGp = 90 },
            new { Key = "cleric", CashGp = 110 },
            new { Key = "druid", CashGp = 50 },
            new { Key = "fighter", CashGp = 155 },
            new { Key = "monk", CashGp = 50 },
            new { Key = "paladin", CashGp = 150 },
            new { Key = "ranger", CashGp = 150 },
            new { Key = "rogue", CashGp = 100 },
            new { Key = "sorcerer", CashGp = 50 },
            new { Key = "warlock", CashGp = 100 },
            new { Key = "wizard", CashGp = 55 }
        };
        var operation = 256;

        foreach (var background in backgrounds)
        foreach (var @class in classes)
        {
            operation++;
            var actorId = "actor.cash." + background.Key + "." + @class.Key;
            var itemId = "item.starting-gold." + actorId;
            var backgroundId = "dnd2024.content.background." + background.Key + ".v1";
            var classId = "dnd2024.content.class." + @class.Key + ".v1";
            var input = JsonSerializer.Serialize(new
            {
                characterId = actorId,
                name = background.Key + " cash " + @class.Key,
                ability = new
                {
                    scores = new { str = 15, dex = 14, con = 13, @int = 8, wis = 10, cha = 12 },
                    increases = new Dictionary<string, int>
                    {
                        [background.PlusTwo] = 2,
                        [background.PlusOne] = 1
                    }
                },
                speciesSelection = new { size = "medium" },
                equipmentChoices = new { background = "cash", @class = "cash" }
            });
            var roles = BasicCreationRoles("world.character-creation.fixture",
                "dnd2024.content.species.human.v1", classId, backgroundId);
            roles["currency"] = "dnd2024.equipment.currency.gold-piece";
            var request = harness.ActionForRoles(
                "dnd2024.mechanic.character.basic.create", roles, input, 0,
                operation.ToString("x32"));

            var created = await harness.Runner.RunAsync(request);
            var replay = await harness.Runner.RunAsync(request);

            Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, created.Disposition);
            Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
            Assert.NotNull(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, itemId));
            using var instance = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, itemId, "dnd2024.core.definition-link"))!.ValueJson);
            Assert.Equal("dnd2024.equipment.currency.gold-piece",
                instance.RootElement.GetProperty("definition").GetProperty("entityId").GetString());
            using var quantity = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, itemId, "dnd2024.item.quantity"))!.ValueJson);
            Assert.Equal(50 + @class.CashGp,
                quantity.RootElement.GetProperty("current").GetInt32());
            var containment = await harness.Edges.GetContainmentAsync(
                DndHarness.StateSpaceId, itemId);
            Assert.NotNull(containment);
            Assert.Equal(actorId, containment.ContainerEntityId);
            Assert.Equal("inventory.currency", containment.Slot);

            using var record = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId,
                "dnd2024.character-creation-record"))!.ValueJson);
            var choices = record.RootElement.GetProperty("selections")
                .GetProperty("startingEquipmentChoices");
            Assert.Equal("cash", choices.GetProperty("background").GetString());
            Assert.Equal("cash", choices.GetProperty("class").GetString());
            Assert.Equal(new[] { itemId }, record.RootElement.GetProperty("createdItemIds")
                .EnumerateArray().Select(value => value.GetString()).ToArray());
            var pending = record.RootElement.GetProperty("unresolvedEntitlements")
                .EnumerateArray().ToArray();
            Assert.DoesNotContain(pending, value =>
                value.GetProperty("ownerDefinitionId").GetString() == backgroundId &&
                value.GetProperty("entitlementKey").GetString()!.StartsWith(
                    "equipment:", StringComparison.Ordinal));
            Assert.DoesNotContain(pending, value =>
                value.GetProperty("ownerDefinitionId").GetString() == classId &&
                value.GetProperty("entitlementKey").GetString()!.StartsWith(
                    "equipment:", StringComparison.Ordinal));
            Assert.Contains(record.RootElement.GetProperty("sourceRefs").EnumerateArray(), value =>
                value.GetProperty("locator").GetString() ==
                    "Equipment > Coins > Coin Values > Gold Piece (SRD 5.2.1, pages 89-89)");
        }
    }

    [Fact]
    public async Task Basic_character_creation_cash_is_visible_to_existing_inventory_and_currency_readers()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        await harness.AddCanonicalGoldDefinitionFixtureAsync();
        const string actorId = "actor.cash.readers";
        const string itemId = "item.starting-gold.actor.cash.readers";
        var roles = BasicCreationRoles("world.character-creation.fixture",
            "dnd2024.content.species.human.v1",
            "dnd2024.content.class.wizard.v1",
            "dnd2024.content.background.sage.v1");
        roles["currency"] = "dnd2024.equipment.currency.gold-piece";
        const string input =
            "{\"characterId\":\"actor.cash.readers\",\"name\":\"Cash Readers\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"int\":2,\"wis\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"equipmentChoices\":{\"background\":\"cash\",\"class\":\"cash\"}}";

        var created = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles, input, 0,
            "cc3f1000000000000000000000000001"));
        var inventory = await harness.EvaluateRolesAsync("dnd2024.mechanic.inventory.read",
            new Dictionary<string, string> { ["root"] = actorId }, "{}", 0);
        var currency = await harness.EvaluateRolesAsync("dnd2024.mechanic.currency-value.read",
            new Dictionary<string, string> { ["root"] = actorId }, "{}", 0);

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, created.Disposition);
        Assert.True(inventory.Ok,
            string.Join("; ", inventory.Problems.Append(inventory.Run?.Error ?? string.Empty)));
        using var inventoryData = JsonDocument.Parse(inventory.Run!.Output.Data);
        var visible = Assert.Single(inventoryData.RootElement.GetProperty("items")
            .EnumerateArray());
        Assert.Equal(itemId, visible.GetProperty("itemId").GetString());
        Assert.Equal(105, visible.GetProperty("quantity").GetInt32());
        Assert.Equal("inventory.currency", visible.GetProperty("slot").GetString());
        Assert.True(currency.Ok, currency.Run?.Error);
        using var currencyData = JsonDocument.Parse(currency.Run!.Output.Data);
        Assert.Equal(105, currencyData.RootElement.GetProperty("coinCount").GetInt32());
        Assert.Equal(10500, currencyData.RootElement.GetProperty("copperValue").GetInt32());
        var gp = Assert.Single(currencyData.RootElement.GetProperty("denominations")
            .EnumerateArray());
        Assert.Equal("gp", gp.GetProperty("code").GetString());
        Assert.Equal(105, gp.GetProperty("count").GetInt32());
        Assert.Empty(inventory.Run.Output.Effects);
        Assert.Empty(currency.Run.Output.Effects);
    }

    [Theory]
    [InlineData("{\"background\":\"cash\"}")]
    [InlineData("{\"class\":\"cash\"}")]
    [InlineData("{\"background\":\"cash\",\"class\":\"package\"}")]
    [InlineData("{\"background\":\"cash\",\"class\":\"cash\",\"amount\":205}")]
    public async Task Basic_character_creation_cash_rejects_partial_non_cash_or_derived_choices_unchanged(
        string equipmentChoices)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        await harness.AddCanonicalGoldDefinitionFixtureAsync();
        const string actorId = "actor.cash.invalid-choice";
        const string itemId = "item.starting-gold.actor.cash.invalid-choice";
        var roles = BasicCreationRoles("world.character-creation.fixture",
            "dnd2024.content.species.human.v1");
        roles["currency"] = "dnd2024.equipment.currency.gold-piece";
        const string prefix =
            "{\"characterId\":\"actor.cash.invalid-choice\",\"name\":\"Invalid Cash Choice\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"equipmentChoices\":";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles,
            prefix + equipmentChoices + "}", 0,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(equipmentChoices)))
                .ToLowerInvariant()[..32]));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, itemId));
        Assert.Null(await harness.Edges.GetContainmentAsync(DndHarness.StateSpaceId, itemId));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong")]
    [InlineData("corrupt")]
    public async Task Basic_character_creation_cash_rejects_missing_wrong_or_corrupt_currency_role_unchanged(
        string invalidRole)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        await harness.AddCanonicalGoldDefinitionFixtureAsync();
        const string actorId = "actor.cash.invalid-role";
        const string itemId = "item.starting-gold.actor.cash.invalid-role";
        var roles = BasicCreationRoles("world.character-creation.fixture",
            "dnd2024.content.species.human.v1");
        if (invalidRole == "wrong")
        {
            await harness.AddItemDefinitionAsync("currency.test.wrong-gold.v1", "Wrong Gold",
                CurrencyItemDefinition("gp", 100));
            roles["currency"] = "currency.test.wrong-gold.v1";
        }
        else if (invalidRole == "corrupt")
        {
            roles["currency"] = "dnd2024.equipment.currency.gold-piece";
            await harness.ReplaceApplicationComponentRawAsync(
                "dnd2024.equipment.currency.gold-piece", "dnd2024.item.physical",
                "{\"weight\":{\"dimension\":\"mass\",\"value\":{\"numerator\":1,\"denominator\":1},\"unit\":{\"entityId\":\"dnd2024.vocabulary.mass-unit.kilogram\"}}}");
        }
        const string input =
            "{\"characterId\":\"actor.cash.invalid-role\",\"name\":\"Invalid Cash Role\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"equipmentChoices\":{\"background\":\"cash\",\"class\":\"cash\"}}";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles, input, 0,
            invalidRole switch
            {
                "missing" => "cc3f1000000000000000000000000010",
                "wrong" => "cc3f1000000000000000000000000011",
                _ => "cc3f1000000000000000000000000012"
            }));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, itemId));
        Assert.Null(await harness.Edges.GetContainmentAsync(DndHarness.StateSpaceId, itemId));
    }

    [Fact]
    public async Task Basic_character_creation_legacy_class_profile_keeps_omitted_path_but_denies_cash()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        await harness.AddCanonicalGoldDefinitionFixtureAsync();
        const string classId = "dnd2024.content.class.fighter.v1";
        var current = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, classId, "dnd2024.class-creation-profile");
        var legacy = current!.ValueJson.Replace(
            "\"startingEquipmentCashGp\":155,\"startingEquipmentCurrencyDefinitionId\":\"dnd2024.equipment.currency.gold-piece\",",
            "", StringComparison.Ordinal);
        Assert.NotEqual(current.ValueJson, legacy);
        await harness.ReplaceApplicationComponentRawAsync(
            classId, "dnd2024.class-creation-profile", legacy);
        var omittedRoles = BasicCreationRoles("world.character-creation.fixture",
            "dnd2024.content.species.human.v1");
        const string omittedInput =
            "{\"characterId\":\"actor.cash.legacy-omitted\",\"name\":\"Legacy Omitted\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"}}";
        var omitted = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", omittedRoles, omittedInput, 0,
            "cc3f1000000000000000000000000020"));
        var cashRoles = BasicCreationRoles("world.character-creation.fixture",
            "dnd2024.content.species.human.v1");
        cashRoles["currency"] = "dnd2024.equipment.currency.gold-piece";
        const string cashInput =
            "{\"characterId\":\"actor.cash.legacy-cash\",\"name\":\"Legacy Cash\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"equipmentChoices\":{\"background\":\"cash\",\"class\":\"cash\"}}";
        var cash = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", cashRoles, cashInput, 0,
            "cc3f1000000000000000000000000021"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, omitted.Disposition);
        using var omittedRecord = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "actor.cash.legacy-omitted",
            "dnd2024.character-creation-record"))!.ValueJson);
        Assert.False(omittedRecord.RootElement.TryGetProperty("createdItemIds", out _));
        Assert.Contains(omittedRecord.RootElement.GetProperty("unresolvedEntitlements")
            .EnumerateArray(), value => value.GetProperty("ownerDefinitionId").GetString() ==
                classId && value.GetProperty("entitlementKey").GetString() ==
                "equipment:starting-package");
        Assert.Equal(ApplicationActionExecutionDisposition.Failed, cash.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(
            DndHarness.StateSpaceId, "actor.cash.legacy-cash"));
        Assert.Null(await harness.Entities.GetEntityAsync(
            DndHarness.StateSpaceId, "item.starting-gold.actor.cash.legacy-cash"));
    }

    [Theory]
    [InlineData("missing-cash")]
    [InlineData("missing-definition")]
    [InlineData("invalid-cash")]
    public async Task Basic_character_creation_cash_rejects_partial_or_invalid_class_cash_declaration(
        string invalidProfile)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        await harness.AddCanonicalGoldDefinitionFixtureAsync();
        const string classId = "dnd2024.content.class.fighter.v1";
        var current = (await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, classId, "dnd2024.class-creation-profile"))!.ValueJson;
        var invalid = invalidProfile switch
        {
            "missing-cash" => current.Replace("\"startingEquipmentCashGp\":155,", "",
                StringComparison.Ordinal),
            "missing-definition" => current.Replace(
                "\"startingEquipmentCurrencyDefinitionId\":\"dnd2024.equipment.currency.gold-piece\",",
                "", StringComparison.Ordinal),
            _ => current.Replace("\"startingEquipmentCashGp\":155",
                "\"startingEquipmentCashGp\":0", StringComparison.Ordinal)
        };
        Assert.NotEqual(current, invalid);
        await harness.ReplaceApplicationComponentRawAsync(
            classId, "dnd2024.class-creation-profile", invalid);
        var roles = BasicCreationRoles("world.character-creation.fixture",
            "dnd2024.content.species.human.v1");
        roles["currency"] = "dnd2024.equipment.currency.gold-piece";
        const string input =
            "{\"characterId\":\"actor.cash.invalid-profile\",\"name\":\"Invalid Cash Profile\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"equipmentChoices\":{\"background\":\"cash\",\"class\":\"cash\"}}";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles, input, 0,
            invalidProfile switch
            {
                "missing-cash" => "cc3f1000000000000000000000000030",
                "missing-definition" => "cc3f1000000000000000000000000031",
                _ => "cc3f1000000000000000000000000032"
            }));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(
            DndHarness.StateSpaceId, "actor.cash.invalid-profile"));
        Assert.Null(await harness.Entities.GetEntityAsync(
            DndHarness.StateSpaceId, "item.starting-gold.actor.cash.invalid-profile"));
    }

    [Fact]
    public async Task Basic_character_creation_cash_rejects_invalid_background_cash_declaration()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        await harness.AddCanonicalGoldDefinitionFixtureAsync();
        const string backgroundId = "dnd2024.content.background.soldier.v1";
        var current = (await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, backgroundId,
            "dnd2024.background-creation-profile"))!.ValueJson;
        var invalid = current.Replace("\"cashAlternativeGp\":50",
            "\"cashAlternativeGp\":49", StringComparison.Ordinal);
        Assert.NotEqual(current, invalid);
        await harness.ReplaceApplicationComponentRawAsync(
            backgroundId, "dnd2024.background-creation-profile", invalid);
        var roles = BasicCreationRoles("world.character-creation.fixture",
            "dnd2024.content.species.human.v1");
        roles["currency"] = "dnd2024.equipment.currency.gold-piece";
        const string input =
            "{\"characterId\":\"actor.cash.invalid-background\",\"name\":\"Invalid Cash Background\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"equipmentChoices\":{\"background\":\"cash\",\"class\":\"cash\"}}";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles, input, 0,
            "cc3f1000000000000000000000000033"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(
            DndHarness.StateSpaceId, "actor.cash.invalid-background"));
        Assert.Null(await harness.Entities.GetEntityAsync(
            DndHarness.StateSpaceId,
            "item.starting-gold.actor.cash.invalid-background"));
    }

    [Fact]
    public async Task Basic_character_creation_cash_rejects_overlong_derived_item_id_unchanged()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        await harness.AddCanonicalGoldDefinitionFixtureAsync();
        var actorId = "actor." + new string('a', 184);
        Assert.InRange(actorId.Length, 1, 200);
        Assert.True(("item.starting-gold." + actorId).Length > 200);
        var input = JsonSerializer.Serialize(new
        {
            characterId = actorId,
            name = "Overlong Derived Item",
            ability = new
            {
                scores = new { str = 15, dex = 14, con = 13, @int = 8, wis = 10, cha = 12 },
                increases = new { str = 2, con = 1 }
            },
            speciesSelection = new { size = "medium" },
            equipmentChoices = new { background = "cash", @class = "cash" }
        });
        var roles = BasicCreationRoles("world.character-creation.fixture",
            "dnd2024.content.species.human.v1");
        roles["currency"] = "dnd2024.equipment.currency.gold-piece";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles, input, 0,
            "cc3f1000000000000000000000000040"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        Assert.DoesNotContain(await harness.Edges.ListContainmentsAsync(DndHarness.StateSpaceId),
            value => value.ContainedEntityId.StartsWith(
                "item.starting-gold.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Basic_character_creation_cash_collision_leaves_actor_and_existing_item_unchanged()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        await harness.AddCanonicalGoldDefinitionFixtureAsync();
        const string actorId = "actor.cash.collision";
        const string itemId = "item.starting-gold.actor.cash.collision";
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, itemId, "Existing Item");
        var roles = BasicCreationRoles("world.character-creation.fixture",
            "dnd2024.content.species.human.v1");
        roles["currency"] = "dnd2024.equipment.currency.gold-piece";
        const string input =
            "{\"characterId\":\"actor.cash.collision\",\"name\":\"Cash Collision\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"equipmentChoices\":{\"background\":\"cash\",\"class\":\"cash\"}}";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles, input, 0,
            "cc3f1000000000000000000000000041"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        var existing = await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, itemId);
        Assert.NotNull(existing);
        Assert.Equal("Existing Item", existing.Name);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, itemId, "dnd2024.core.definition-link"));
        Assert.Null(await harness.Edges.GetContainmentAsync(DndHarness.StateSpaceId, itemId));
    }

    [Fact]
    public async Task Basic_character_creation_cash_rolls_back_item_and_containment_after_late_failure()
    {
        await using var harness = await DndHarness.CreateAsync(failTransactionAfterEffects: true);
        await harness.AddBasicCharacterCreationFixturesAsync();
        await harness.AddCanonicalGoldDefinitionFixtureAsync();
        const string actorId = "actor.cash.rollback";
        const string itemId = "item.starting-gold.actor.cash.rollback";
        const string worldId = "world.character-creation.fixture";
        var roles = BasicCreationRoles(worldId, "dnd2024.content.species.human.v1");
        roles["currency"] = "dnd2024.equipment.currency.gold-piece";
        const string input =
            "{\"characterId\":\"actor.cash.rollback\",\"name\":\"Cash Rollback\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"equipmentChoices\":{\"background\":\"cash\",\"class\":\"cash\"}}";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles, input, 0,
            "cc3f1000000000000000000000000050"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, itemId));
        Assert.Null(await harness.Edges.GetContainmentAsync(DndHarness.StateSpaceId, itemId));
        var participationId = worldId + ".participation." + actorId;
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, participationId));
        Assert.Null(await harness.Edges.GetRelationshipAsync(DndHarness.StateSpaceId,
            worldId, participationId,
            "dnd2024.campaign.has-character-participation"));
        Assert.Null(await harness.Edges.GetRelationshipAsync(DndHarness.StateSpaceId,
            participationId, actorId,
            "dnd2024.campaign.character-participation.for-actor"));
    }

    [Fact]
    public async Task Basic_character_creation_origin_choices_apply_languages_and_background_tools_and_replay()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        var cases = new[]
        {
            new { Key = "acolyte", PlusTwo = "int", PlusOne = "wis", Languages = new[] { "elvish", "draconic" }, Tool = (string?)null, FixedTool = (string?)"calligraphers-supplies" },
            new { Key = "criminal", PlusTwo = "dex", PlusOne = "con", Languages = new[] { "orc", "dwarvish" }, Tool = (string?)null, FixedTool = (string?)"thieves-tools" },
            new { Key = "sage", PlusTwo = "int", PlusOne = "wis", Languages = new[] { "gnomish", "common-sign-language" }, Tool = (string?)null, FixedTool = (string?)"calligraphers-supplies" },
            new { Key = "soldier", PlusTwo = "str", PlusOne = "con", Languages = new[] { "goblin", "giant" }, Tool = (string?)"dice-set", FixedTool = (string?)null },
            new { Key = "soldier", PlusTwo = "str", PlusOne = "con", Languages = new[] { "halfling", "draconic" }, Tool = (string?)"dragonchess-set", FixedTool = (string?)null },
            new { Key = "soldier", PlusTwo = "str", PlusOne = "con", Languages = new[] { "elvish", "orc" }, Tool = (string?)"playing-cards", FixedTool = (string?)null },
            new { Key = "soldier", PlusTwo = "str", PlusOne = "con", Languages = new[] { "dwarvish", "gnomish" }, Tool = (string?)"three-dragon-ante", FixedTool = (string?)null }
        };
        var languageOrder = new[]
        {
            "common-sign-language", "draconic", "dwarvish", "elvish", "giant", "gnomish",
            "goblin", "halfling", "orc"
        };
        var operation = 0;

        foreach (var origin in cases)
        {
            operation++;
            var actorId = "actor.origin." + origin.Key + "." + operation;
            var backgroundId = "dnd2024.content.background." + origin.Key + ".v1";
            var originChoices = new Dictionary<string, object>
            {
                ["languages"] = origin.Languages
            };
            if (origin.Tool is not null) originChoices["backgroundTool"] = origin.Tool;
            var input = JsonSerializer.Serialize(new
            {
                characterId = actorId,
                name = "Complete " + origin.Key,
                ability = new
                {
                    scores = new { str = 15, dex = 14, con = 13, @int = 8, wis = 10, cha = 12 },
                    increases = new Dictionary<string, int>
                    {
                        [origin.PlusTwo] = 2,
                        [origin.PlusOne] = 1
                    }
                },
                speciesSelection = new { size = "medium" },
                originChoices
            });
            var request = harness.ActionForRoles(
                "dnd2024.mechanic.character.basic.create",
                BasicCreationRoles("world.character-creation.fixture",
                    "dnd2024.content.species.human.v1",
                    "dnd2024.content.class.rogue.v1", backgroundId),
                input, 0, (100 + operation).ToString("x32"));

            var created = await harness.Runner.RunAsync(request);
            var replay = await harness.Runner.RunAsync(request);

            Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, created.Disposition);
            Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
            var expectedLanguages = new[] { "common" }.Concat(origin.Languages
                .OrderBy(value => Array.IndexOf(languageOrder, value))).ToArray();
            using var languages = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId, "dnd2024.creature.languages"))!.ValueJson);
            Assert.Equal(expectedLanguages, ReadLanguageIds(languages.RootElement).ToArray());

            var expectedTools = new[] { "thieves-tools", origin.FixedTool, origin.Tool }
                .Where(value => value is not null).Select(value => value!)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            using var tools = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId, "dnd2024.creature.proficiencies"))!.ValueJson);
            Assert.Equal(expectedTools, ReadToolIds(tools.RootElement).ToArray());

            using var record = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId, "dnd2024.character-creation-record"))!.ValueJson);
            var selections = record.RootElement.GetProperty("selections");
            Assert.Equal(origin.Languages.OrderBy(value => Array.IndexOf(languageOrder, value)),
                selections.GetProperty("languageChoices").EnumerateArray()
                    .Select(value => value.GetString()));
            Assert.Equal(origin.Tool is not null,
                selections.TryGetProperty("backgroundToolChoice", out var selectedTool));
            if (origin.Tool is not null) Assert.Equal(origin.Tool, selectedTool.GetString());
            var pending = record.RootElement.GetProperty("unresolvedEntitlements")
                .EnumerateArray().ToArray();
            Assert.DoesNotContain(pending, value =>
                value.GetProperty("ownerDefinitionId").GetString() == backgroundId &&
                value.GetProperty("entitlementKey").GetString() == "origin-language-choice:2:standard");
            Assert.DoesNotContain(pending, value =>
                value.GetProperty("ownerDefinitionId").GetString() == backgroundId &&
                value.GetProperty("entitlementKey").GetString()!.StartsWith(
                    "tool-choice:", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("acolyte", "int", "wis", "{\"languages\":[\"draconic\"]}")]
    [InlineData("acolyte", "int", "wis", "{\"languages\":[\"draconic\",\"draconic\"]}")]
    [InlineData("acolyte", "int", "wis", "{\"languages\":[\"abyssal\",\"draconic\"]}")]
    [InlineData("acolyte", "int", "wis", "{\"languages\":[\"common\",\"draconic\"]}")]
    [InlineData("acolyte", "int", "wis", "{\"languages\":[\"draconic\",\"elvish\"],\"backgroundTool\":\"dice-set\"}")]
    [InlineData("soldier", "str", "con", "{\"languages\":[\"draconic\",\"elvish\"]}")]
    [InlineData("soldier", "str", "con", "{\"languages\":[\"draconic\",\"elvish\"],\"backgroundTool\":\"herbalism-kit\"}")]
    [InlineData("soldier", "str", "con", "{\"languages\":[\"draconic\",\"elvish\",\"orc\"],\"backgroundTool\":\"dice-set\"}")]
    [InlineData("soldier", "str", "con", "{\"languages\":[\"draconic\",\"elvish\"],\"backgroundTool\":\"dice-set\",\"extra\":true}")]
    public async Task Basic_character_creation_origin_choices_reject_invalid_or_cross_background_input(
        string backgroundKey,
        string plusTwo,
        string plusOne,
        string originChoicesJson)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.origin.invalid";
        var input = JsonSerializer.Serialize(new
        {
            characterId = actorId,
            name = "Invalid Origin",
            ability = new
            {
                scores = new { str = 15, dex = 14, con = 13, @int = 8, wis = 10, cha = 12 },
                increases = new Dictionary<string, int> { [plusTwo] = 2, [plusOne] = 1 }
            },
            speciesSelection = new { size = "medium" },
            originChoices = JsonSerializer.Deserialize<JsonElement>(originChoicesJson)
        });

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create",
            BasicCreationRoles("world.character-creation.fixture",
                "dnd2024.content.species.human.v1",
                backgroundId: "dnd2024.content.background." + backgroundKey + ".v1"),
            input, 0, "cc3b0000000000000000000000000000"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId,
            "world.character-creation.fixture.participation." + actorId));
    }

    [Fact]
    public async Task Basic_character_creation_origin_choices_roll_back_after_late_failure()
    {
        await using var harness = await DndHarness.CreateAsync(failTransactionAfterEffects: true);
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.origin.rollback";
        const string input =
            "{\"characterId\":\"actor.origin.rollback\",\"name\":\"Origin Rollback\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"originChoices\":{\"languages\":[\"draconic\",\"elvish\"],\"backgroundTool\":\"dice-set\"}}";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create",
            BasicCreationRoles("world.character-creation.fixture",
                "dnd2024.content.species.human.v1"),
            input, 0, "cc3b1000000000000000000000000000"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId,
            "world.character-creation.fixture.participation." + actorId));
    }

    [Fact]
    public async Task Basic_character_creation_class_tool_choices_apply_compose_and_replay()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        var cases = new[]
        {
            new
            {
                ClassKey = "bard", BackgroundKey = "acolyte", PlusTwo = "int", PlusOne = "wis",
                Choices = new[] { "lyre", "flute", "bagpipes" },
                FixedBackgroundTool = (string?)"calligraphers-supplies",
                BackgroundTool = (string?)null, Languages = (string[]?)null
            },
            new
            {
                ClassKey = "monk", BackgroundKey = "acolyte", PlusTwo = "int", PlusOne = "wis",
                Choices = new[] { "calligraphers-supplies" },
                FixedBackgroundTool = (string?)"calligraphers-supplies",
                BackgroundTool = (string?)null, Languages = (string[]?)null
            },
            new
            {
                ClassKey = "monk", BackgroundKey = "criminal", PlusTwo = "dex", PlusOne = "con",
                Choices = new[] { "lute" }, FixedBackgroundTool = (string?)"thieves-tools",
                BackgroundTool = (string?)null, Languages = (string[]?)null
            },
            new
            {
                ClassKey = "bard", BackgroundKey = "soldier", PlusTwo = "str", PlusOne = "con",
                Choices = new[] { "shawm", "drum", "horn" }, FixedBackgroundTool = (string?)null,
                BackgroundTool = (string?)"dice-set", Languages = (string[]?)["draconic", "elvish"]
            }
        };

        for (var index = 0; index < cases.Length; index++)
        {
            var testCase = cases[index];
            var actorId = "actor.class-tools." + testCase.ClassKey + "." + index;
            var classId = "dnd2024.content.class." + testCase.ClassKey + ".v1";
            var backgroundId = "dnd2024.content.background." + testCase.BackgroundKey + ".v1";
            var input = new Dictionary<string, object>
            {
                ["characterId"] = actorId,
                ["name"] = "Class Tool " + index,
                ["ability"] = new
                {
                    scores = new { str = 15, dex = 14, con = 13, @int = 8, wis = 10, cha = 12 },
                    increases = new Dictionary<string, int>
                    {
                        [testCase.PlusTwo] = 2,
                        [testCase.PlusOne] = 1
                    }
                },
                ["speciesSelection"] = new { size = "medium" },
                ["classToolChoices"] = testCase.Choices
            };
            if (testCase.Languages is not null)
            {
                input["originChoices"] = new
                {
                    languages = testCase.Languages,
                    backgroundTool = testCase.BackgroundTool
                };
            }

            var roles = BasicCreationRoles("world.character-creation.fixture",
                "dnd2024.content.species.human.v1", classId, backgroundId);
            var inputJson = JsonSerializer.Serialize(input);
            var evaluated = await harness.EvaluateRolesAsync(
                "dnd2024.mechanic.character.basic.create", roles, inputJson, 0);
            Assert.True(evaluated.Ok, evaluated.Run?.Error ?? string.Join("; ", evaluated.Problems));
            using var result = JsonDocument.Parse(evaluated.Run!.Output.Data);
            Assert.True(result.RootElement.GetProperty("classToolChoicesResolved").GetBoolean());
            var request = harness.ActionForRoles(
                "dnd2024.mechanic.character.basic.create", roles, inputJson, 0,
                (0xCC3E20 + index).ToString("x32"));
            var created = await harness.Runner.RunAsync(request);
            var replay = await harness.Runner.RunAsync(request);

            Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, created.Disposition);
            Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
            using var record = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId,
                "dnd2024.character-creation-record"))!.ValueJson);
            var selections = record.RootElement.GetProperty("selections");
            Assert.Equal(testCase.Choices.Order(StringComparer.Ordinal),
                selections.GetProperty("classToolChoices").EnumerateArray()
                    .Select(value => value.GetString()));
            var pending = record.RootElement.GetProperty("unresolvedEntitlements")
                .EnumerateArray().ToArray();
            Assert.DoesNotContain(pending, value =>
                value.GetProperty("ownerDefinitionId").GetString() == classId &&
                value.GetProperty("entitlementKey").GetString()!.StartsWith(
                    "tool-choice:", StringComparison.Ordinal));

            var expectedTools = testCase.Choices
                .Concat(testCase.FixedBackgroundTool is null ? [] : [testCase.FixedBackgroundTool])
                .Concat(testCase.BackgroundTool is null ? [] : [testCase.BackgroundTool])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            using var tools = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, actorId, "dnd2024.creature.proficiencies"))!.ValueJson);
            Assert.Equal(expectedTools, ReadToolIds(tools.RootElement).ToArray());
        }
    }

    [Theory]
    [InlineData("bard", "[]")]
    [InlineData("bard", "[\"lute\",\"flute\"]")]
    [InlineData("bard", "[\"lute\",\"flute\",\"drum\",\"viol\"]")]
    [InlineData("bard", "[\"lute\",\"lute\",\"drum\"]")]
    [InlineData("bard", "[\"lute\",\"flute\",\"smiths-tools\"]")]
    [InlineData("bard", "[\"lute\",\"flute\",\"kazoo\"]")]
    [InlineData("monk", "[\"lute\",\"flute\"]")]
    [InlineData("monk", "[\"dice-set\"]")]
    [InlineData("fighter", "[\"lute\"]")]
    public async Task Basic_character_creation_class_tool_choices_reject_invalid_or_cross_class_input(
        string classKey,
        string classToolChoicesJson)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.class-tools.invalid";
        var input = JsonSerializer.Serialize(new
        {
            characterId = actorId,
            name = "Invalid Class Tools",
            ability = new
            {
                scores = new { str = 15, dex = 14, con = 13, @int = 8, wis = 10, cha = 12 },
                increases = new { str = 2, con = 1 }
            },
            speciesSelection = new { size = "medium" },
            classToolChoices = JsonSerializer.Deserialize<JsonElement>(classToolChoicesJson)
        });

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create",
            BasicCreationRoles("world.character-creation.fixture",
                "dnd2024.content.species.human.v1",
                "dnd2024.content.class." + classKey + ".v1"),
            input, 0, "cc3e2100000000000000000000000000"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId,
            "world.character-creation.fixture.participation." + actorId));
    }

    [Fact]
    public async Task Basic_character_creation_class_tool_choices_roll_back_after_late_failure()
    {
        await using var harness = await DndHarness.CreateAsync(failTransactionAfterEffects: true);
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.class-tools.rollback";
        const string input =
            "{\"characterId\":\"actor.class-tools.rollback\",\"name\":\"Class Tool Rollback\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"classToolChoices\":[\"bagpipes\",\"flute\",\"viol\"]}";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create",
            BasicCreationRoles("world.character-creation.fixture",
                "dnd2024.content.species.human.v1", "dnd2024.content.class.bard.v1"),
            input, 0, "cc3e2200000000000000000000000000"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature.proficiencies"));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId,
            "world.character-creation.fixture.participation." + actorId));
    }

    [Fact]
    public async Task Basic_character_creation_supports_fixed_size_species_and_source_speed()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.basic.goliath";
        var roles = BasicCreationRoles(
            "world.character-creation.fixture", "dnd2024.content.species.goliath.v1");
        const string input =
            "{\"characterId\":\"actor.basic.goliath\",\"name\":\"Kava\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":1,\"dex\":1,\"con\":1}},\"speciesSelection\":{}}";

        var created = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create", roles, input, long.MaxValue,
            "b123456789abcdef0123456789abcdf0"));

        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, created.Disposition);
        using var size = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature.body"))!.ValueJson);
        using var speed = JsonDocument.Parse((await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.creature.movement"))!.ValueJson);
        Assert.Equal("dnd2024.vocabulary.size.medium",
            size.RootElement.GetProperty("sizeRef").GetProperty("entityId").GetString());
        var walk = speed.RootElement.GetProperty("speeds")
            .GetProperty("dnd2024.vocabulary.movement-mode.walk").GetProperty("distance");
        Assert.Equal(35 * 381, walk.GetProperty("value").GetProperty("numerator").GetInt32());
        Assert.Equal(1250, walk.GetProperty("value").GetProperty("denominator").GetInt32());
    }

    [Theory]
    [InlineData("{\"characterId\":\"actor.basic.invalid\",\"name\":\"Invalid\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"large\"}}")]
    [InlineData("{\"characterId\":\"actor.basic.invalid\",\"name\":\" Invalid\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"}}")]
    [InlineData("{\"characterId\":\"actor.basic.invalid\",\"name\":\"Invalid\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"},\"hitPoints\":12}")]
    public async Task Basic_character_creation_rejects_illegal_or_derived_input_unchanged(string input)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.basic.invalid";
        const string worldId = "world.character-creation.fixture";
        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create",
            BasicCreationRoles(worldId, "dnd2024.content.species.human.v1"), input, 0,
            "c123456789abcdef0123456789abcdf0"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId,
            worldId + ".participation." + actorId));
    }

    [Fact]
    public async Task Basic_character_creation_rolls_back_after_all_effects_are_staged()
    {
        await using var harness = await DndHarness.CreateAsync(failTransactionAfterEffects: true);
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.basic.rollback";
        const string worldId = "world.character-creation.fixture";
        const string input =
            "{\"characterId\":\"actor.basic.rollback\",\"name\":\"Rollback\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"small\"}}";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create",
            BasicCreationRoles(worldId, "dnd2024.content.species.human.v1"), input, 0,
            "d123456789abcdef0123456789abcdf0"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        var participationId = worldId + ".participation." + actorId;
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, participationId));
        Assert.Null(await harness.Edges.GetRelationshipAsync(DndHarness.StateSpaceId,
            worldId, participationId, "dnd2024.campaign.has-character-participation"));
        Assert.Null(await harness.Edges.GetRelationshipAsync(DndHarness.StateSpaceId,
            participationId, actorId,
            "dnd2024.campaign.character-participation.for-actor"));
    }

    [Theory]
    [InlineData("inactive-world")]
    [InlineData("source-drift")]
    [InlineData("class-profile-drift")]
    [InlineData("class-armor-order-drift")]
    [InlineData("class-weapon-restriction-order-drift")]
    [InlineData("background-profile-drift")]
    public async Task Basic_character_creation_rejects_inactive_or_source_drifted_state_unchanged(
        string invalidState)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.basic.invalid-state";
        const string worldId = "world.character-creation.fixture";
        if (invalidState == "inactive-world")
        {
            await harness.ReplaceApplicationComponentRawAsync(worldId, "game.core.world.root",
                "{\"status\":\"archived\",\"summary\":\"An inactive fixture.\",\"visibility\":\"party\"}");
        }
        else if (invalidState == "source-drift")
        {
            await harness.ReplaceApplicationComponentRawAsync("dnd2024.content.species.human.v1",
                "dnd2024.species-profile",
                "{\"contentKey\":\"human\",\"contentVersion\":1,\"sourceRef\":{\"sourceId\":\"dnd2024.source.drifted\",\"locator\":\"Character Origins > Character Species > Human\"},\"creatureType\":\"humanoid\",\"allowedSizes\":[\"small\",\"medium\"],\"baseSpeed\":{\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0},\"traitKeys\":[\"resourceful\",\"skillful\",\"versatile\"],\"choiceFamilies\":[]}");
        }
        else if (invalidState == "class-profile-drift")
        {
            await harness.ReplaceApplicationComponentRawAsync(
                "dnd2024.content.class.fighter.v1", "dnd2024.class-creation-profile",
                "{\"classKey\":\"wizard\",\"primaryAbilities\":{\"mode\":\"all\",\"abilities\":[\"int\"]},\"savingThrows\":[\"int\",\"wis\"],\"skills\":{\"choiceCount\":2,\"options\":[\"arcana\",\"history\",\"insight\",\"investigation\",\"medicine\",\"nature\",\"religion\"],\"fixedChoices\":[\"arcana\",\"investigation\"]},\"weapons\":{\"categories\":[\"simple\"],\"restrictedMartialProperties\":[]},\"armorTraining\":[],\"tools\":{\"fixed\":[],\"choiceGroups\":[]},\"spellcasting\":{\"kind\":\"full\",\"ability\":\"int\",\"cantrips\":3,\"preparedSpells\":4,\"spellbookSpells\":6,\"level1Slots\":2,\"slotLevel\":1},\"sourceRef\":{\"sourceId\":\"dnd2024.source.srd-5.2.1\",\"locator\":\"Classes > Wizard, PDF pages 77–78\"}}");
        }
        else if (invalidState == "class-armor-order-drift")
        {
            var current = await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, "dnd2024.content.class.fighter.v1",
                "dnd2024.class-creation-profile");
            await harness.ReplaceApplicationComponentRawAsync(
                "dnd2024.content.class.fighter.v1", "dnd2024.class-creation-profile",
                current!.ValueJson.Replace(
                    "[\"light\",\"medium\",\"heavy\",\"shield\"]",
                    "[\"shield\",\"light\",\"medium\",\"heavy\"]",
                    StringComparison.Ordinal));
        }
        else if (invalidState == "class-weapon-restriction-order-drift")
        {
            var current = await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, "dnd2024.content.class.fighter.v1",
                "dnd2024.class-creation-profile");
            await harness.ReplaceApplicationComponentRawAsync(
                "dnd2024.content.class.fighter.v1", "dnd2024.class-creation-profile",
                current!.ValueJson.Replace(
                    "\"categories\":[\"simple\",\"martial\"],\"restrictedMartialProperties\":[]",
                    "\"categories\":[\"simple\"],\"restrictedMartialProperties\":[\"light\",\"finesse\"]",
                    StringComparison.Ordinal));
        }
        else
        {
            var current = await harness.Entities.GetComponentAsync(
                DndHarness.StateSpaceId, "dnd2024.content.background.soldier.v1",
                "dnd2024.background-creation-profile");
            await harness.ReplaceApplicationComponentRawAsync(
                "dnd2024.content.background.soldier.v1", "dnd2024.background-creation-profile",
                current!.ValueJson.Replace("Soldier, PDF p. 83", "Soldier, PDF p. 82",
                    StringComparison.Ordinal));
        }

        const string input =
            "{\"characterId\":\"actor.basic.invalid-state\",\"name\":\"Invalid State\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"}}";
        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create",
            BasicCreationRoles(worldId, "dnd2024.content.species.human.v1"), input, 0,
            invalidState switch
            {
                "inactive-world" => "e123456789abcdef0123456789abcdf0",
                "source-drift" => "f123456789abcdef0123456789abcdf0",
                "class-profile-drift" => "a223456789abcdef0123456789abcdf0",
                "class-armor-order-drift" => "a323456789abcdef0123456789abcdf0",
                "class-weapon-restriction-order-drift" =>
                    "a423456789abcdef0123456789abcdf0",
                _ => "b223456789abcdef0123456789abcdf0"
            }));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId,
            worldId + ".participation." + actorId));
    }

    [Fact]
    public async Task Basic_character_creation_rejects_existing_actor_without_partial_participation()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddBasicCharacterCreationFixturesAsync();
        const string actorId = "actor.basic.existing";
        const string worldId = "world.character-creation.fixture";
        await harness.Entities.CreateEntityAsync(DndHarness.StateSpaceId, actorId, "Existing Actor");
        const string input =
            "{\"characterId\":\"actor.basic.existing\",\"name\":\"Replacement\",\"ability\":{\"scores\":{\"str\":15,\"dex\":14,\"con\":13,\"int\":8,\"wis\":10,\"cha\":12},\"increases\":{\"str\":2,\"con\":1}},\"speciesSelection\":{\"size\":\"medium\"}}";

        var result = await harness.Runner.RunAsync(harness.ActionForRoles(
            "dnd2024.mechanic.character.basic.create",
            BasicCreationRoles(worldId, "dnd2024.content.species.human.v1"), input, 0,
            "0123456789abcdef0123456789abcdf0"));

        Assert.Equal(ApplicationActionExecutionDisposition.Failed, result.Disposition);
        var existing = await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId, actorId);
        Assert.NotNull(existing);
        Assert.Equal("Existing Actor", existing.Name);
        Assert.Null(await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, actorId, "dnd2024.character-creation-record"));
        Assert.Null(await harness.Entities.GetEntityAsync(DndHarness.StateSpaceId,
            worldId + ".participation." + actorId));
    }

    [Fact]
    public async Task Character_sheet_reader_derives_complete_canonical_effect_free_view_and_replays()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.ReplaceCoreComponentRawAsync("subject.high", "dnd2024.creature.ability-scores",
            "{\"str\":16,\"dex\":14,\"con\":14,\"int\":10,\"wis\":15,\"cha\":8}");
        await harness.AddProficiencyStateAsync("subject.high", 1, ["athletics", "perception"]);
        await harness.AddSavingThrowStateAsync("subject.high", ["str", "con"]);

        var first = await harness.EvaluateAsync("subject.high", "{}", 1,
            "dnd2024.mechanic.character-sheet.read");
        var otherSeed = await harness.EvaluateAsync("subject.high", "{}", long.MaxValue,
            "dnd2024.mechanic.character-sheet.read");

        Assert.True(first.Ok, first.Run?.Error);
        Assert.True(otherSeed.Ok, otherSeed.Run?.Error);
        Assert.Equal(first.Run!.Output.Data, otherSeed.Run!.Output.Data);
        Assert.Empty(first.Run.Output.Effects);
        Assert.Empty(first.Run.Output.Events);
        Assert.Empty(first.Run.Output.Notifications);

        using var result = JsonDocument.Parse(first.Run.Output.Data);
        var data = result.RootElement;
        Assert.Equal("character-sheet-core", data.GetProperty("test").GetString());
        Assert.Equal(1, data.GetProperty("level").GetInt32());
        Assert.Equal(2, data.GetProperty("proficiencyBonus").GetInt32());
        var abilities = data.GetProperty("abilities").EnumerateArray().ToArray();
        Assert.Equal(["str", "dex", "con", "int", "wis", "cha"],
            abilities.Select(value => value.GetProperty("id").GetString()!).ToArray());
        Assert.Equal(16, abilities[0].GetProperty("score").GetInt32());
        Assert.Equal(3, abilities[0].GetProperty("modifier").GetInt32());
        var saves = data.GetProperty("savingThrows").EnumerateArray().ToArray();
        Assert.Equal(6, saves.Length);
        Assert.Equal(["str", "dex", "con", "int", "wis", "cha"],
            saves.Select(value => value.GetProperty("ability").GetString()!).ToArray());
        Assert.True(saves[0].GetProperty("proficient").GetBoolean());
        Assert.Equal(5, saves[0].GetProperty("modifier").GetInt32());
        var skills = data.GetProperty("skills").EnumerateArray().ToArray();
        Assert.Equal(18, skills.Length);
        Assert.Equal(["acrobatics", "animal-handling", "arcana", "athletics", "deception",
            "history", "insight", "intimidation", "investigation", "medicine", "nature",
            "perception", "performance", "persuasion", "religion", "sleight-of-hand", "stealth",
            "survival"], skills.Select(value => value.GetProperty("id").GetString()!).ToArray());
        Assert.Equal(["dex", "wis", "int", "str", "cha", "int", "wis", "cha", "int", "wis",
            "int", "wis", "cha", "cha", "int", "dex", "dex", "wis"],
            skills.Select(value => value.GetProperty("ability").GetString()!).ToArray());
        var perception = skills.Single(value =>
            value.GetProperty("id").GetString() == "perception");
        Assert.Equal("wis", perception.GetProperty("ability").GetString());
        Assert.True(perception.GetProperty("proficient").GetBoolean());
        Assert.Equal(4, perception.GetProperty("modifier").GetInt32());
        Assert.Equal("dex", data.GetProperty("initiative").GetProperty("ability").GetString());
        Assert.Equal(2, data.GetProperty("initiative").GetProperty("modifier").GetInt32());
        var passive = data.GetProperty("basePassivePerceptionBreakdown");
        Assert.Equal(10, passive.GetProperty("base").GetInt32());
        Assert.Equal(4, passive.GetProperty("modifier").GetInt32());
        Assert.Equal(14, passive.GetProperty("total").GetInt32());

        var before = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.ability-scores");
        var request = harness.ActionFor("dnd2024.mechanic.character-sheet.read", "subject.high",
            "{}", 99, "a123456789abcdef0123456789abcde0");
        var committed = await harness.Runner.RunAsync(request);
        var replay = await harness.Runner.RunAsync(request);
        var after = await harness.Entities.GetComponentAsync(
            DndHarness.StateSpaceId, "subject.high", "dnd2024.creature.ability-scores");
        Assert.Equal(ApplicationActionExecutionDisposition.Succeeded, committed.Disposition);
        Assert.Equal(0, committed.AppliedEffectCount);
        Assert.Equal(ApplicationActionExecutionDisposition.Replayed, replay.Disposition);
        Assert.Equal(before!.Revision, after!.Revision);
        Assert.Equal(before.ValueJson, after.ValueJson);
    }

    [Fact]
    public async Task Character_sheet_reader_covers_score_level_and_proficiency_boundaries()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.ReplaceCoreComponentRawAsync("subject.low", "dnd2024.creature.ability-scores",
            "{\"str\":1,\"dex\":30,\"con\":2,\"int\":3,\"wis\":30,\"cha\":1}");
        await harness.AddProficiencyStateAsync("subject.low", 20, []);
        await harness.AddSavingThrowStateAsync("subject.low", ["cha", "wis", "int", "con", "dex", "str"]);

        var result = await harness.EvaluateAsync("subject.low", "{}", 0,
            "dnd2024.mechanic.character-sheet.read");

        Assert.True(result.Ok, result.Run?.Error);
        using var document = JsonDocument.Parse(result.Run!.Output.Data);
        var data = document.RootElement;
        Assert.Equal(6, data.GetProperty("proficiencyBonus").GetInt32());
        Assert.Equal(-5, data.GetProperty("abilityModifiers").GetProperty("str").GetInt32());
        Assert.Equal(10, data.GetProperty("abilityModifiers").GetProperty("dex").GetInt32());
        Assert.Equal(16, data.GetProperty("savingThrowModifiers").GetProperty("dex").GetInt32());
        Assert.Equal(["str", "dex", "con", "int", "wis", "cha"],
            data.GetProperty("savingThrowProficiencies").EnumerateArray()
                .Select(value => value.GetString()!).ToArray());
        Assert.Equal(10, data.GetProperty("skillModifiers").GetProperty("perception").GetInt32());
        Assert.Equal(20, data.GetProperty("basePassivePerception").GetInt32());
        Assert.Equal(20, data.GetProperty("basePassivePerceptionBreakdown")
            .GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(8, 3)]
    [InlineData(9, 4)]
    [InlineData(12, 4)]
    [InlineData(13, 5)]
    [InlineData(16, 5)]
    [InlineData(17, 6)]
    [InlineData(20, 6)]
    public async Task Character_sheet_reader_derives_every_proficiency_bonus_boundary(
        int level, int expectedBonus)
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddProficiencyStateAsync("subject.high", level, []);
        await harness.AddSavingThrowStateAsync("subject.high", []);

        var result = await harness.EvaluateAsync("subject.high", "{}", 0,
            "dnd2024.mechanic.character-sheet.read");

        Assert.True(result.Ok, result.Run?.Error);
        using var document = JsonDocument.Parse(result.Run!.Output.Data);
        Assert.Equal(expectedBonus, document.RootElement.GetProperty("proficiencyBonus").GetInt32());
    }

    [Fact]
    public async Task Character_sheet_reader_rejects_input_injection_and_corrupt_source_state()
    {
        await using var harness = await DndHarness.CreateAsync();
        await harness.AddProficiencyStateAsync("subject.high", 1, ["perception"]);
        await harness.AddSavingThrowStateAsync("subject.high", ["str"]);

        var injected = await harness.EvaluateAsync("subject.high", "{\"proficiencyBonus\":6}", 0,
            "dnd2024.mechanic.character-sheet.read");
        Assert.False(injected.Ok);
        Assert.Contains("empty object", injected.Run?.Error, StringComparison.Ordinal);

        await harness.ReplaceCoreComponentRawAsync("subject.high", "dnd2024.creature.proficiencies",
            "{\"entries\":{\"dnd2024.vocabulary.skill.perception\":{\"rankRef\":{\"entityId\":\"dnd2024.vocabulary.proficiency-rank.invalid\"},\"sourceRefs\":[{\"entityId\":\"dnd2024.source.srd-5.2.1\"}]}},\"recordedFamilies\":[\"saving-throw\",\"skill\"]}");
        var duplicate = await harness.EvaluateAsync("subject.high", "{}", 0,
            "dnd2024.mechanic.character-sheet.read");
        Assert.False(duplicate.Ok);
        Assert.Contains("invalid rank or source list", duplicate.Run?.Error, StringComparison.Ordinal);

        await harness.ReplaceClassMembershipRawAsync("subject.high",
            "{\"classRef\":{\"entityId\":\"content.extension.class.invalid.v1\"},\"level\":1}");
        var drifted = await harness.EvaluateAsync("subject.high", "{}", 0,
            "dnd2024.mechanic.character-sheet.read");
        Assert.False(drifted.Ok);
        Assert.True(drifted.Run is null || drifted.Run.Output.Effects.Count == 0);
    }

    [Fact]
    public async Task Character_sheet_javascript_rejects_a_malformed_raw_projection()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "catalog", "applications",
            "dnd2024", "mechanics", "proficiency", "dnd2024.mechanic.character-sheet.read.js"));
        var valid = new Dictionary<string, string>
        {
            ["dnd2024.creature.ability-scores"] = "{\"scores\":{\"dnd2024.vocabulary.ability.strength\":10,\"dnd2024.vocabulary.ability.dexterity\":10,\"dnd2024.vocabulary.ability.constitution\":10,\"dnd2024.vocabulary.ability.intelligence\":10,\"dnd2024.vocabulary.ability.wisdom\":10,\"dnd2024.vocabulary.ability.charisma\":10}}",
            ["dnd2024.creature.proficiencies"] =
                "{\"entries\":{},\"recordedFamilies\":[\"saving-throw\",\"skill\"]}"
        };
        static MechanicProjection Projection(IReadOnlyDictionary<string, string> components) => new()
        {
            Roles = new Dictionary<string, EntityProjection>
            {
                ["subject"] = new("subject", "Subject", components)
            },
            Children = new Dictionary<string, IReadOnlyList<ChildMechanicResult>>
            {
                ["level"] =
                [
                    new("dnd2024.mechanic.character-level.read", 1, 0,
                        new Dictionary<string, string> { ["subject"] = "subject" },
                        new MechanicOutput
                        {
                            Data = "{\"test\":\"character-level-read\",\"subjectId\":\"subject\",\"present\":true,\"valid\":true,\"problem\":null,\"totalLevel\":1,\"proficiencyBonus\":2,\"membershipCount\":1}",
                            HasData = true
                        }, [], 0)
                ]
            },
            Input = "{}",
            Seed = 0
        };
        var cases = new[]
        {
            (Component: "dnd2024.creature.proficiencies", Value: "{",
                Error: "missing or malformed"),
            (Component: "dnd2024.creature.ability-scores",
                Value: "{\"scores\":{\"dnd2024.vocabulary.ability.strength\":31,\"dnd2024.vocabulary.ability.dexterity\":10,\"dnd2024.vocabulary.ability.constitution\":10,\"dnd2024.vocabulary.ability.intelligence\":10,\"dnd2024.vocabulary.ability.wisdom\":10,\"dnd2024.vocabulary.ability.charisma\":10}}",
                Error: "1 through 30"),
            (Component: "dnd2024.creature.proficiencies",
                Value: "{\"entries\":{},\"recordedFamilies\":[\"skill\"]}",
                Error: "must be recorded")
        };
        foreach (var @case in cases)
        {
            var components = new Dictionary<string, string>(valid) { [@case.Component] = @case.Value };
            var result = await new JintMechanicEngine().RunAsync(source, Projection(components),
                ExecutionLimits.Default);
            Assert.False(result.Ok);
            Assert.Contains(@case.Error, result.Error, StringComparison.Ordinal);
            Assert.Empty(result.Output.Effects);
            Assert.Empty(result.Output.Events);
            Assert.Empty(result.Output.Notifications);
        }
    }

}
