using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Events;
using DantesRoleplay.Mechanics;
using DantesRoleplay.MCPServer.Tools;
using DantesRoleplay.Notifications;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class CategoryToolTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Browses_procedure_roots_and_intermediate_branches_through_query()
    {
        await using var db = _fixture.CreateContext();
        var procedures = new ProcedureStore(db);

        await procedures.WriteAsync(Procedure("procedure.system.inspect", "system"));
        await procedures.WriteAsync(Procedure("procedure.ruleset.ability", "ruleset.dnd2024.core.gameplay.ability"));
        await procedures.WriteAsync(Procedure("procedure.ruleset.fixed-dc", "ruleset.dnd2024.core.gameplay.checks.fixed-dc"));
        await procedures.WriteAsync(Procedure("procedure.ruleset.player", "ruleset.dnd2024.core.gameplay.player"));

        var root = await QueryAsync(db, catalog: "procedures");
        var rootData = Data(root);
        var rootChildren = rootData.GetProperty("Branch").GetProperty("Children").EnumerateArray().ToList();

        Assert.True(root.Ok, JsonSerializer.Serialize(root));
        Assert.Equal("procedures", rootData.GetProperty("Catalog").GetString());
        Assert.Equal(["ruleset", "system"], rootChildren.Select(child => child.GetProperty("Segment").GetString()));
        Assert.Equal(3, rootChildren.Single(child => child.GetProperty("Path").GetString() == "ruleset").GetProperty("Subtree").GetInt32());
        Assert.StartsWith("query(kind: \"categories\"", root.NextSteps[0], StringComparison.Ordinal);
        Assert.StartsWith("query(kind: \"procedures\"", root.NextSteps[1], StringComparison.Ordinal);

        var branch = await QueryAsync(db, catalog: "procedures", category: "ruleset.dnd2024.core.gameplay");
        var branchData = Data(branch).GetProperty("Branch");
        var children = branchData.GetProperty("Children").EnumerateArray().ToList();

        Assert.True(branch.Ok, JsonSerializer.Serialize(branch));
        Assert.Equal(0, branchData.GetProperty("Direct").GetInt32());
        Assert.Equal(3, branchData.GetProperty("Subtree").GetInt32());
        Assert.Equal(
            ["ability", "checks", "player"],
            children.Select(child => child.GetProperty("Segment").GetString()));
        Assert.Equal(1, children.Single(child => child.GetProperty("Segment").GetString() == "checks").GetProperty("Subtree").GetInt32());
        Assert.Equal(2, await db.Operations.CountAsync(operation => operation.Tool == "query"));
    }

    [Fact]
    public async Task Browses_mechanic_categories_with_listing_visibility()
    {
        await using var db = _fixture.CreateContext();
        var mechanics = new MechanicStore(db);

        await mechanics.WriteAsync(Mechanic("mechanic.system.inspect", "system", MechanicStatus.Active));
        await mechanics.WriteAsync(Mechanic("mechanic.ruleset.play", "ruleset.dnd2024.play", MechanicStatus.Archived));
        await mechanics.WriteAsync(Mechanic("mechanic.ruleset.player", "ruleset.dnd2024.player", MechanicStatus.Archived));

        var visible = await QueryAsync(db, catalog: "mechanics");
        var all = await QueryAsync(db, catalog: "mechanics", includeInactive: true);
        var ruleset = await QueryAsync(
            db,
            catalog: "mechanics",
            category: "ruleset.dnd2024.play",
            includeInactive: true);

        Assert.True(visible.Ok, JsonSerializer.Serialize(visible));
        Assert.Equal(
            ["system"],
            Data(visible).GetProperty("Branch").GetProperty("Children")
                .EnumerateArray().Select(child => child.GetProperty("Segment").GetString()));

        Assert.True(all.Ok, JsonSerializer.Serialize(all));
        Assert.Contains(
            Data(all).GetProperty("Branch").GetProperty("Children").EnumerateArray(),
            child => child.GetProperty("Segment").GetString() == "ruleset");

        var branch = Data(ruleset).GetProperty("Branch");
        Assert.True(ruleset.Ok, JsonSerializer.Serialize(ruleset));
        Assert.Equal(1, branch.GetProperty("Direct").GetInt32());
        Assert.Equal(1, branch.GetProperty("Subtree").GetInt32());
        Assert.Empty(branch.GetProperty("Children").EnumerateArray());
    }

    [Fact]
    public async Task Rejects_invalid_catalog_and_category_with_callable_fixes()
    {
        await using var db = _fixture.CreateContext();

        var invalidCatalog = await QueryAsync(db, catalog: "events");
        var invalidCategory = await QueryAsync(db, catalog: "procedures", category: "Ruleset.Dnd2024");

        Assert.False(invalidCatalog.Ok);
        Assert.Equal("INVALID_CATALOG", invalidCatalog.Error?.Code);
        Assert.Equal("query(kind: \"categories\", catalog: \"procedures\")", invalidCatalog.Error?.Fix);

        Assert.False(invalidCategory.Ok);
        Assert.Equal("INVALID_CATEGORY", invalidCategory.Error?.Code);
        Assert.Contains("not valid", invalidCategory.Error?.Why, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("query(kind: \"categories\", catalog: \"procedures\")", invalidCategory.Error?.Fix);
    }

    [Fact]
    public async Task Categories_are_advertised_through_capabilities()
    {
        await using var db = _fixture.CreateContext();

        var capabilities = await QueryAsync(db, kind: "capabilities");
        var categorySpec = Data(capabilities)
            .GetProperty("Query")
            .GetProperty("categories");

        Assert.True(capabilities.Ok, JsonSerializer.Serialize(capabilities));
        Assert.Equal(
            ["catalog", "category", "includeInactive"],
            categorySpec.GetProperty("Reads").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains(
            "procedure.system.hierarchical-catalogs",
            categorySpec.GetProperty("Contracts").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task Orient_adds_rollup_roots_and_callable_category_navigation()
    {
        await using var db = _fixture.CreateContext();
        var procedures = new ProcedureStore(db);
        var mechanics = new MechanicStore(db);

        await procedures.WriteAsync(Procedure("procedure.system.inspect", "system"));
        await procedures.WriteAsync(Procedure("procedure.ruleset.play", "ruleset.dnd2024.play"));
        await mechanics.WriteAsync(Mechanic(
            "mechanic.ruleset.play",
            "ruleset.dnd2024.play",
            MechanicStatus.Archived));

        var result = await new OrientTool().OrientAsync(
            procedures,
            new WorldStore(db),
            mechanics,
            new OperationLog(db));
        var data = Data(result);
        var procedureRoots = data.GetProperty("Procedures").GetProperty("CategoryRoots")
            .EnumerateArray().ToList();
        var mechanicRoots = data.GetProperty("Rules").GetProperty("CategoryRoots")
            .EnumerateArray().ToList();

        Assert.True(result.Ok, JsonSerializer.Serialize(result));
        Assert.Equal(2, data.GetProperty("Procedures").GetProperty("Total").GetInt32());
        Assert.Equal(1, data.GetProperty("Rules").GetProperty("Total").GetInt32());
        Assert.Equal("query(kind: \"categories\", catalog: \"procedures\")",
            data.GetProperty("Procedures").GetProperty("HowToBrowse").GetString());
        Assert.Equal("query(kind: \"categories\", catalog: \"mechanics\")",
            data.GetProperty("Rules").GetProperty("HowToBrowse").GetString());
        Assert.Equal(["ruleset", "system"], procedureRoots.Select(root => root.GetProperty("Path").GetString()));
        Assert.Equal(1, procedureRoots.Single(root => root.GetProperty("Path").GetString() == "ruleset")
            .GetProperty("Subtree").GetInt32());
        Assert.Equal(["ruleset"], mechanicRoots.Select(root => root.GetProperty("Path").GetString()));
        Assert.Equal(1, mechanicRoots.Single().GetProperty("Subtree").GetInt32());
        Assert.Contains(result.NextSteps,
            step => step.StartsWith("query(kind: \"categories\", catalog: \"procedures\")", StringComparison.Ordinal));
        Assert.Contains(result.NextSteps,
            step => step.StartsWith("query(kind: \"categories\", catalog: \"mechanics\")", StringComparison.Ordinal));
    }

    private static WriteProcedureRequest Procedure(string id, string category) =>
        new()
        {
            Id = id,
            Category = category,
            Name = id,
            Description = "Category browsing fixture.",
            Governs = "query",
            Instructions = "1. Browse the category.",
            Status = ProcedureStatus.Active
        };

    private static WriteMechanicRequest Mechanic(string id, string category, MechanicStatus status) =>
        new()
        {
            Id = id,
            Category = category,
            Name = id,
            Description = "Category browsing fixture.",
            Matches = id,
            Requirements = "{}",
            Source = "return { narration: 'ok', effects: [] };",
            Status = status
        };

    private static async Task<ToolEnvelope> QueryAsync(
        DantesRoleplayDbContext db,
        string kind = "categories",
        string? catalog = null,
        string? category = null,
        bool includeInactive = false)
    {
        var world = new WorldStore(db);

        return await new QueryTool().QueryAsync(
            procedures: new ProcedureStore(db),
            world: world,
            graphs: new GraphProjectionReader(world),
            journeys: new JourneyPlanReader(world),
            itineraries: new ModeAwareItineraryReader(world),
            campaignResumes: null!,
            questSummaries: null!,
            mechanics: new MechanicStore(db),
            eventTypes: new EventTypeStore(db),
            subscriptions: new SubscriptionStore(db),
            events: new EventLedger(db),
            log: new OperationLog(db),
            notifications: new NotificationStore(db),
            kind: kind,
            category: category,
            catalog: catalog,
            includeInactive: includeInactive);
    }

    private static JsonElement Data(ToolEnvelope envelope)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(envelope.Data));
        return document.RootElement.Clone();
    }
}
