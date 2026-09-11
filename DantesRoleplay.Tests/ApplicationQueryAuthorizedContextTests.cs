using DantesRoleplay.Applications;
using DantesRoleplay.Ecs;
using DantesRoleplay.Knowledge;
using DantesRoleplay.MCPServer;
using Microsoft.Extensions.Configuration;

namespace DantesRoleplay.Tests;

public sealed class ApplicationQueryAuthorizedContextTests
{
    [Fact]
    public void Trusted_configuration_loads_only_the_explicit_opaque_role_entity_map()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Knowledge:LocalPlayer:Enabled"] = "true",
                ["Knowledge:LocalPlayer:ApplicationId"] = "sample-app",
                ["Knowledge:LocalPlayer:CampaignId"] = "legacy-context",
                ["Knowledge:LocalPlayer:ActorId"] = "legacy-actor",
                ["Knowledge:LocalPlayer:RoleEntityIds:session.owner"] = "entity.owner"
            }).Build();

        var seat = new ConfigurationLocalKnowledgeSeatProvider(configuration).Current();

        var roleEntityIds = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(
            seat.AuthorizedRoleEntityIds);
        Assert.Equal("entity.owner", Assert.Single(roleEntityIds).Value);
        Assert.DoesNotContain("CampaignId", roleEntityIds.Keys, StringComparer.Ordinal);
        Assert.DoesNotContain("ActorId", roleEntityIds.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Provider_returns_only_configured_existing_entities_in_the_exact_application_state_space()
    {
        var app = ApplicationIdentifier.Parse("sample-app");
        var seat = new LocalKnowledgeSeatSnapshot(true, "principal", app.Value,
            "legacy-context", "legacy-actor", KnowledgeAudienceRole.Actor,
            AuthorizedRoleEntityIds: new Dictionary<string, string>
            {
                ["session.owner"] = "entity.owner"
            });
        var provider = new LocalApplicationQueryAuthorizedContextProvider(
            new Seats(seat), new StateSpaces(app), new Entities("state.one", "entity.owner"));

        var result = await provider.ResolveAsync(app, "state.one");

        Assert.Single(result!);
        Assert.Equal("entity.owner", result!["session.owner"]);
        Assert.Null(await provider.ResolveAsync(ApplicationIdentifier.Parse("other-app"), "state.one"));
        Assert.Null(await provider.ResolveAsync(app, "state.other"));
    }

    [Fact]
    public async Task Provider_fails_closed_for_missing_configured_entity_and_never_derives_legacy_fields()
    {
        var app = ApplicationIdentifier.Parse("sample-app");
        var missing = new LocalApplicationQueryAuthorizedContextProvider(
            new Seats(new(true, "principal", app.Value, "legacy-context", "legacy-actor",
                AuthorizedRoleEntityIds: new Dictionary<string, string>
                {
                    ["session.owner"] = "entity.missing"
                })),
            new StateSpaces(app), new Entities("state.one", "entity.owner"));
        Assert.Null(await missing.ResolveAsync(app, "state.one"));

        var empty = new LocalApplicationQueryAuthorizedContextProvider(
            new Seats(new(true, "principal", app.Value, "legacy-context", "legacy-actor",
                AuthorizedRoleEntityIds: new Dictionary<string, string>())),
            new StateSpaces(app), new Entities("state.one", "entity.owner"));
        Assert.Empty((await empty.ResolveAsync(app, "state.one"))!);
    }

    private sealed class Seats(LocalKnowledgeSeatSnapshot value) : ILocalKnowledgeSeatProvider
    {
        public LocalKnowledgeSeatSnapshot Current() => value;
    }

    private sealed class StateSpaces(ApplicationIdentifier app) : IStateSpaceRegistry
    {
        private readonly StateSpaceView state = new("state.one",
            new(app, 1, new string('A', 64), []), new string('B', 64), 1,
            DateTime.UnixEpoch, DateTime.UnixEpoch);
        public StateSpaceView? Get(string stateSpaceId) => stateSpaceId == state.StateSpaceId ? state : null;
        public StateSpaceView Create(StateSpaceBinding binding) => throw new NotSupportedException();
        public StateSpaceDiscoveryPage ListPage(ApplicationIdentifier applicationId,
            string? afterStateSpaceId, int limit) => throw new NotSupportedException();
    }

    private sealed class Entities(string stateSpaceId, string entityId) : IEntityComponentStore
    {
        public Task<EcsEntityView?> GetEntityAsync(string state, string entity,
            CancellationToken cancellationToken = default) => Task.FromResult<EcsEntityView?>(
                state == stateSpaceId && entity == entityId
                    ? new(state, entity, "Entity", 1, DateTime.UnixEpoch, null)
                    : null);
        public Task<EcsEntityView> CreateEntityAsync(string state, string entity, string name,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsEntityDiscoveryPage> ListEntitiesAsync(string state, string? afterEntityId,
            int limit, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteEntityAsync(string state, string entity, int expectedRevision,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsComponentView?> GetComponentAsync(string state, string entity,
            string qualifiedTypeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<EcsComponentView>> GetComponentsAsync(string state,
            IReadOnlyList<EcsComponentLocator> locators, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<EcsComponentDiscoveryPage> ListComponentsAsync(string state, string entity,
            string? afterQualifiedTypeId, int limit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<EcsComponentView> AddComponentAsync(EcsComponentWrite write,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsComponentView> SetComponentAsync(EcsComponentWrite write,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EcsComponentView> MergeComponentAsync(EcsComponentWrite write,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RemoveComponentAsync(string state, string entity, EcsComponentReference type,
            int expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
