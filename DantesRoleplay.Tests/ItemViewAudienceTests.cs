using System.Text.Json;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Knowledge.Tests;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.Tests;

public sealed class ItemViewAudienceTests
{
    [Fact]
    public async Task Actor_and_same_observer_GM_preview_have_equal_player_content_without_mutating_the_seat()
    {
        using var fixture = await Fixture.Create();
        await fixture.Game.AddKnowledgeAsync("fact.1", "Only this observer knows the application.", fixture.Item);
        await fixture.Game.RelateAsync(fixture.Game.Actor, "fact.1", fixture.Game.Binding.ExplicitStateRelationshipKind, "{\"stance\":\"known\"}");
        var actor = await fixture.Read();
        fixture.Policy.Grant = fixture.Policy.Grant with { Role = KnowledgeAudienceRole.GameMaster, ActorId = null };
        var preview = await fixture.Read();
        Assert.True(actor.Ok, string.Join(';', actor.Problems));
        Assert.True(preview.Ok, string.Join(';', preview.Problems));
        Assert.Equal(JsonSerializer.Serialize(actor.Projection!.References), JsonSerializer.Serialize(preview.Projection!.References));
        Assert.Equal(fixture.Game.Actor, preview.Projection.AuthorizedObserver!.Value.GetProperty("observerId").GetString());
        Assert.Equal(KnowledgeAudienceRole.GameMaster, fixture.Policy.Grant.Role);
        Assert.Null(fixture.Policy.Grant.ActorId);
    }

    [Fact]
    public async Task Explicit_unknown_overrides_world_baseline_and_hydration_does_not_leak_the_statement()
    {
        using var fixture = await Fixture.Create();
        await fixture.Game.AddKnowledgeAsync("fact.1", "Private recipe text", fixture.Item);
        await fixture.Game.RelateAsync(fixture.Game.World, "fact.1", fixture.Game.Binding.BaselineRelationshipKind, "{\"inheritance\":\"current\"}");
        var known = await fixture.Read();
        Assert.True(known.Ok, string.Join(';', known.Problems));
        Assert.Contains("fact.1", known.Projection!.References.Keys);
        await fixture.Game.RelateAsync(fixture.Game.Actor, "fact.1", fixture.Game.Binding.ExplicitStateRelationshipKind, "{\"stance\":\"unknown\"}");
        var unknown = await fixture.Read();
        Assert.True(unknown.Ok, string.Join(';', unknown.Problems));
        Assert.DoesNotContain("fact.1", unknown.Projection!.References.Keys);
        Assert.DoesNotContain("Private recipe text", JsonSerializer.Serialize(unknown.Projection));
        Assert.NotEqual(known.Projection.AuthorizedSourceRevision, unknown.Projection.AuthorizedSourceRevision);
    }

    [Fact]
    public async Task Two_active_observers_do_not_share_effective_knowledge_for_the_same_definition()
    {
        using var fixture = await Fixture.Create();
        await fixture.Game.AddKnowledgeAsync("fact.1", "A learned definition", fixture.Definition);
        await fixture.Game.RelateAsync(fixture.Game.Actor, "fact.1", fixture.Game.Binding.ExplicitStateRelationshipKind, "{\"stance\":\"known\"}");
        await fixture.AddSecondObserver();
        fixture.Policy.Grant = fixture.Policy.Grant with { Role = KnowledgeAudienceRole.GameMaster, ActorId = null };
        var first = await fixture.Read();
        var second = await fixture.Read(observer: "actor.second", item: "item.second");
        Assert.True(first.Ok, string.Join(';', first.Problems));
        Assert.True(second.Ok, string.Join(';', second.Problems));
        Assert.Contains("fact.1", first.Projection!.References.Keys);
        Assert.DoesNotContain("fact.1", second.Projection!.References.Keys);
    }

    [Fact]
    public async Task Familiarity_does_not_hydrate_proposition_text()
    {
        using var fixture = await Fixture.Create();
        await fixture.Game.AddKnowledgeAsync("fact.1", "Private familiar proposition", fixture.Item);
        await fixture.Game.RelateAsync(fixture.Game.Actor, "fact.1", fixture.Game.Binding.ExplicitStateRelationshipKind, "{\"stance\":\"familiar\"}");
        var result = await fixture.Read();
        Assert.True(result.Ok, string.Join(';', result.Problems));
        Assert.DoesNotContain("fact.1", result.Projection!.References.Keys);
        Assert.DoesNotContain("Private familiar proposition", JsonSerializer.Serialize(result.Projection));
    }

    [Fact]
    public async Task Known_identity_with_unknown_curse_exposes_only_validated_discovery_context()
    {
        using var fixture = await Fixture.Create();
        await fixture.Game.RelateAsync(fixture.Game.Actor, fixture.Item, fixture.Game.Binding.ExplicitStateRelationshipKind, "{\"stance\":\"known\"}");
        await fixture.Game.ComponentAsync(fixture.Item, fixture.DiscoveryType, JsonSerializer.Serialize(new
        {
            knowledgeRelationship = new { stateSpaceId = fixture.Game.Campaign, fromEntityId = fixture.Game.Actor,
                toEntityId = fixture.Item, qualifiedKind = fixture.Game.Binding.ExplicitStateRelationshipKind },
            identityKnown = true, curseKnown = false, knownProperties = Array.Empty<object>()
        }));
        await fixture.Game.ComponentAsync(fixture.Item, fixture.PrivateType, "{\"text\":\"Hidden curse\"}");
        var player = await fixture.Read();
        Assert.True(player.Ok, string.Join(';', player.Problems));
        var data = player.Projection!.Roles["subject"].Contains!.Single().Components!;
        using var discovery = JsonDocument.Parse(data[fixture.DiscoveryType]);
        Assert.True(discovery.RootElement.GetProperty("identityKnown").GetBoolean());
        Assert.False(discovery.RootElement.GetProperty("curseKnown").GetBoolean());
        Assert.DoesNotContain(fixture.PrivateType, data.Keys);
        Assert.DoesNotContain("Hidden curse", JsonSerializer.Serialize(player.Projection));
        fixture.Policy.Grant = fixture.Policy.Grant with { Role = KnowledgeAudienceRole.GameMaster, ActorId = null };
        var dm = await fixture.Read(perspective: "dm");
        Assert.True(dm.Ok, string.Join(';', dm.Problems));
        Assert.Contains("Hidden curse", JsonSerializer.Serialize(dm.Projection));
    }

    [Theory]
    [InlineData("actor.other", "player", "campaign-fixture")]
    [InlineData("actor-fixture", "dm", "campaign-fixture")]
    [InlineData("actor-fixture", "player", "foreign-space")]
    public async Task Forged_observer_perspective_or_state_space_is_denied(string observer, string perspective, string state)
    {
        using var fixture = await Fixture.Create();
        var result = await fixture.Read(observer, perspective: perspective, state: state);
        Assert.Null(result.Projection);
        Assert.Equal(["READ_MODEL_FORBIDDEN"], result.Problems);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("missing")]
    public async Task Inactive_or_missing_participation_is_denied_for_GM_preview(string status)
    {
        using var fixture = await Fixture.Create(participation: status);
        fixture.Policy.Grant = fixture.Policy.Grant with { Role = KnowledgeAudienceRole.GameMaster, ActorId = null };
        var result = await fixture.Read();
        Assert.Null(result.Projection);
        Assert.Equal(["READ_MODEL_FORBIDDEN"], result.Problems);
    }

    [Fact]
    public async Task Cross_campaign_binding_is_denied_even_for_GM()
    {
        using var fixture = await Fixture.Create();
        fixture.Binding.Value = fixture.Game.Binding with { CampaignEntityId = "campaign.other" };
        fixture.Policy.Grant = fixture.Policy.Grant with { Role = KnowledgeAudienceRole.GameMaster, ActorId = null };
        Assert.Equal(["READ_MODEL_FORBIDDEN"], (await fixture.Read()).Problems);
    }

    [Theory]
    [InlineData("missing.item")]
    [InlineData("actor-fixture")]
    [InlineData("definition.shared")]
    public async Task Non_descendant_selection_has_one_non_disclosing_failure(string item)
    {
        using var fixture = await Fixture.Create();
        var result = await fixture.Read(item: item);
        Assert.Null(result.Projection);
        Assert.Equal(["READ_MODEL_SELECTION_UNAVAILABLE"], result.Problems);
    }

    [Fact]
    public async Task Transfer_invalidates_old_inventory_and_empty_knowledge_invalidates_when_a_fact_is_added()
    {
        using var fixture = await Fixture.Create();
        var before = await fixture.Read();
        Assert.True(before.Ok, string.Join(';', before.Problems));
        await fixture.Game.AddKnowledgeAsync("fact.new", "Newly learned", fixture.Item);
        await fixture.Game.RelateAsync(fixture.Game.Actor, "fact.new", fixture.Game.Binding.ExplicitStateRelationshipKind, "{\"stance\":\"known\"}");
        var after = await fixture.Read();
        Assert.True(after.Ok, string.Join(';', after.Problems));
        Assert.NotEqual(before.Projection!.AuthorizedSourceRevision, after.Projection!.AuthorizedSourceRevision);
        await fixture.Game.Edges.MoveContainmentAsync(fixture.Game.Campaign, fixture.Item, fixture.Game.World, "elsewhere", 1);
        Assert.Equal(["READ_MODEL_SELECTION_UNAVAILABLE"], (await fixture.Read()).Problems);
    }

    [Fact]
    public async Task Depth_and_count_bounds_reject_unmaterialized_selection()
    {
        using var fixture = await Fixture.Create();
        var parent = fixture.Item;
        for (var depth = 2; depth <= 5; depth++)
        {
            var id = "depth." + depth;
            await fixture.Game.AddEntityAsync(id, id);
            await fixture.Game.Edges.MoveContainmentAsync(fixture.Game.Campaign, id, parent, "inside", 0);
            parent = id;
        }
        Assert.Equal(["READ_MODEL_SELECTION_UNAVAILABLE"], (await fixture.Read(item: "depth.5")).Problems);
        var root = await fixture.Read();
        Assert.True(root.Ok, string.Join(';', root.Problems));
        Assert.False(root.Projection!.AuthorizedObserver!.Value.GetProperty("inventoryComplete").GetBoolean());
        fixture.Requirements = fixture.Requirements with { AuthorizedContext = fixture.Requirements.AuthorizedContext! with { MaxInventoryItems = 1 } };
        Assert.Equal(["READ_MODEL_SELECTION_UNAVAILABLE"], (await fixture.Read(item: "depth.2")).Problems);
    }

    [Fact]
    public async Task Revoked_grant_during_materialization_returns_stale_without_a_payload()
    {
        using var fixture = await Fixture.Create();
        fixture.Policy.ChangeAfterFirstRead = true;
        var result = await fixture.Read();
        Assert.Null(result.Projection);
        Assert.Equal(["READ_MODEL_SOURCE_STALE"], result.Problems);
    }

    [Fact]
    public async Task Context_is_frozen_in_the_real_sandbox_and_reading_writes_no_game_rows()
    {
        using var fixture = await Fixture.Create();
        var before = fixture.Game.Db.ChangeTracker.Entries().Count();
        var result = await fixture.Read();
        Assert.True(result.Ok, string.Join(';', result.Problems));
        var engine = new JintMechanicEngine();
        var run = await engine.RunAsync("return { data: { frozen: Object.isFrozen(ctx.authorizedObserver), observer: ctx.authorizedObserver.observerId } };",
            result.Projection!, ExecutionLimits.Default);
        Assert.True(run.Ok, run.Error);
        using var data = JsonDocument.Parse(run.Output.Data);
        Assert.True(data.RootElement.GetProperty("frozen").GetBoolean());
        Assert.Empty(run.Output.Effects);
        Assert.Empty(run.Output.Events);
        Assert.Empty(run.Output.Notifications);
        Assert.Equal(before, fixture.Game.Db.ChangeTracker.Entries().Count());
        Assert.DoesNotContain(fixture.Game.Db.ChangeTracker.Entries(), entry => entry.State is
            Microsoft.EntityFrameworkCore.EntityState.Added or Microsoft.EntityFrameworkCore.EntityState.Modified or Microsoft.EntityFrameworkCore.EntityState.Deleted);
    }

    private sealed class Policy(KnowledgeAudienceGrant grant) : IAuthorizedKnowledgeAudiencePolicy
    {
        public KnowledgeAudienceGrant Grant = grant;
        public bool ChangeAfterFirstRead;
        private int reads;
        public Task<KnowledgeAudienceResolution> ResolveAsync(string campaignId, CancellationToken cancellationToken = default)
        {
            if (ChangeAfterFirstRead && ++reads > 1) Grant = Grant with { PolicyRevision = "revoked" };
            return Task.FromResult(new KnowledgeAudienceResolution(Grant));
        }
    }
    private sealed class Binding(KnowledgeApplicationBinding value) : IKnowledgeApplicationBindingResolver
    {
        public KnowledgeApplicationBinding Value = value;
        public Task<KnowledgeApplicationBinding?> ResolveAsync(string campaignId, CancellationToken cancellationToken = default) => Task.FromResult<KnowledgeApplicationBinding?>(Value);
    }
    private sealed class Fixture : IDisposable
    {
        public KnowledgeCoreTests.KnowledgeFixture Game { get; } = new();
        public string Item => "item.first";
        public string Definition => "definition.shared";
        public string LinkType => "fixture-knowledge.definition-link";
        public string DiscoveryType => "fixture-knowledge.discovery";
        public string PrivateType => "fixture-knowledge.private-facet";
        public Policy Policy { get; }
        public Binding Binding { get; }
        public MechanicRequirements Requirements { get; set; }
        private readonly ApplicationAuthorizedProjectionResolver resolver;
        private readonly ApplicationMechanicProjectionMapping mapping;
        private Fixture()
        {
            Policy = new(new("principal", Game.Campaign, KnowledgeAudienceRole.Actor, Game.Actor, "policy.1"));
            Binding = new(Game.Binding);
            mapping = new(new Dictionary<string, EcsComponentReference>
            {
                [LinkType] = Game.DefineComponent(LinkType),
                [DiscoveryType] = Game.DefineComponent(DiscoveryType),
                [PrivateType] = Game.DefineComponent(PrivateType)
            }, new Dictionary<string, string>());
            Requirements = new()
            {
                Roles = new() { ["subject"] = new([], IncludeContents: true, ContentsDepth: 4, ContentComponentIds: [LinkType]), ["campaign"] = new([]) },
                AuthorizedContext = new()
                {
                    ObserverRole = "subject", CampaignRole = "campaign", RequireActiveParticipation = true,
                    KnowledgeBinding = "application-metadata", ContentPolicy = "authorize-before-materialization",
                    MaxInventoryItems = 512, MaxInventoryDepth = 4, MaxKnowledgeCandidates = 10000, MaxSerializedOutputBytes = 65536,
                    SourceSets = new()
                    {
                        Selection = new("itemId", "subject", LinkType, "definition"),
                        Knowledge = new("application-metadata") { SubjectSources = ["selected-item", "selected-definition"], FilterBeforeContent = true },
                        Discovery = new(DiscoveryType, "knowledgeRelationship", "knownProperties", true),
                        OptionalSelectedItemComponents = [DiscoveryType, PrivateType]
                    }
                }
            };
            resolver = new(Game.Db, Policy, Binding, new ApplicationKnowledgeActorParticipationVerifier(Game.Entities, Game.Edges), Game.Source, Game.States);
        }
        public static async Task<Fixture> Create(string participation = "ready")
        {
            var fixture = new Fixture();
            await fixture.Game.AddCoreAsync();
            if (participation != "missing") await fixture.Game.AddParticipationAsync(participation);
            await fixture.Game.AddEntityAsync(fixture.Item, "Carried instance");
            await fixture.Game.AddEntityAsync(fixture.Definition, "Shared definition");
            await fixture.Game.ComponentAsync(fixture.Item, fixture.LinkType, JsonSerializer.Serialize(new { definition = new { entityId = fixture.Definition } }));
            await fixture.Game.Edges.MoveContainmentAsync(fixture.Game.Campaign, fixture.Item, fixture.Game.Actor, "pack", 0);
            return fixture;
        }
        public Task<ProjectionResult> Read(string? observer = null, string? item = null, string perspective = "player", string? state = null) =>
            resolver.ResolveAsync(new(state ?? Game.Campaign, ApplicationIdentifier.Parse(Game.ApplicationId), "fixture-knowledge.read", new string('A', 64),
                mapping, new Dictionary<string, string> { ["subject"] = observer ?? Game.Actor, ["campaign"] = Game.Campaign },
                JsonSerializer.Serialize(new { itemId = item ?? Item }), 0, Audience: new(perspective)), Requirements);
        public async Task AddSecondObserver()
        {
            await Game.AddEntityAsync("actor.second", "Second observer");
            await Game.AddEntityAsync("participation.second", "Second membership");
            await Game.ComponentAsync("participation.second", Game.Binding.ParticipationComponentTypeId, "{\"state\":\"ready\"}");
            await Game.RelateAsync(Game.Campaign, "participation.second", Game.Binding.CampaignParticipationRelationshipKind, "{}");
            await Game.RelateAsync("participation.second", "actor.second", Game.Binding.ParticipationActorRelationshipKind, "{}");
            await Game.AddEntityAsync("item.second", "Second instance");
            await Game.ComponentAsync("item.second", LinkType, JsonSerializer.Serialize(new { definition = new { entityId = Definition } }));
            await Game.Edges.MoveContainmentAsync(Game.Campaign, "item.second", "actor.second", "pack", 0);
        }
        public void Dispose() => Game.Dispose();
    }
}
