using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Interactions;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Web.Data;
using DantesRoleplay.Web.Pages;
using Fixture = DantesRoleplay.Tests.WebPagePublicationSelectionTests.Fixture;

namespace DantesRoleplay.Tests;

public sealed class CompositionPageBindingCoordinatorTests
{
    [Fact]
    public async Task Exact_retained_query_uses_server_route_role_and_shared_page_budget()
    {
        await using var fixture = await Fixture.CreateAsync();
        var queryId = fixture.ApplicationId.Value + ".query.page-summary";
        var (selection, parsed) = await PublishCompositionAsync(fixture, $$$$"""
            {"formatVersion":1,"generation":"retained","queries":[{"name":"summary","query":"{{{{queryId}}}}"}],
             "components":[],"root":{"kind":"element","tag":"p","children":[{"kind":"value","path":"summary.label"}]}}
            """);
        var schemas = new BoundedJsonSchemaValidator();
        var catalog = Catalog(selection, schemas, out var catalogQueryId, out var target);
        Assert.Equal(queryId, catalogQueryId);
        var adapter = new ReadAdapter();
        var candidate = Grant(selection, queryId);
        var coordinator = new CompositionPageBindingCoordinator(
            new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
                { [fixture.ApplicationId] = catalog }),
            new ApplicationQueryRoleBindingResolver(schemas), schemas, new TargetResolver(target),
            new AllowPolicy(candidate), new CandidateReader(candidate),
            new CompositionQueryMaterializer(adapter), new ActionAdapter());
        var budget = new InteractionInvocationBudget(2, DateTime.UtcNow.AddMinutes(1));
        Assert.True(budget.TryConsumeOperation());
        var host = InteractionInvocationHost.ForApplication(
            TrustedPrincipalContext.VerifiedPrincipal(candidate.PrincipalReference, "test"),
            selection.Publication.ApplicationRevision, "page@1", "page.command",
            InteractionExecutionProfile.ReadOnly, budget);

        var rendered = await coordinator.RenderAsync(selection, parsed, host, default);

        Assert.True(rendered.IsSuccess, string.Join("; ", rendered.Errors.Select(value => value.Code)));
        Assert.Equal("<p>ready</p>", rendered.Html);
        Assert.Equal(0, budget.RemainingOperations);
        var request = Assert.Single(adapter.Requests);
        Assert.Equal(Fixture.EntityId, request.RoleBindings["subject"]);
        Assert.Equal(selection.Publication.StateSpaceId, request.Host.StateSpaceId);
        Assert.Equal(candidate.GrantReference, request.Host.GrantReference);
    }

    [Fact]
    public async Task Missing_catalog_reference_and_revoked_query_authority_render_no_data()
    {
        await using var fixture = await Fixture.CreateAsync();
        var expectedQueryId = fixture.ApplicationId.Value + ".query.page-summary";
        var (selection, parsed) = await PublishCompositionAsync(fixture, $$$$"""
            {"formatVersion":1,"generation":"retained","queries":[{"name":"summary","query":"{{{{expectedQueryId}}}}"}],
             "components":[],"root":{"kind":"value","path":"summary"}}
            """);
        var schemas = new BoundedJsonSchemaValidator();
        var catalog = Catalog(selection, schemas, out var queryId, out var target);
        Assert.Equal(expectedQueryId, queryId);
        var candidate = Grant(selection, queryId);
        async Task<WebCompositionRenderResult> Render(IPublicApplicationCatalogProvider provider, IStandingGrantPolicy policy)
        {
            var budget = new InteractionInvocationBudget(2, DateTime.UtcNow.AddMinutes(1));
            Assert.True(budget.TryConsumeOperation());
            var host = InteractionInvocationHost.ForApplication(
                TrustedPrincipalContext.VerifiedPrincipal(candidate.PrincipalReference, "test"),
                selection.Publication.ApplicationRevision, "page@1", Guid.NewGuid().ToString("N"),
                InteractionExecutionProfile.ReadOnly, budget);
            return await new CompositionPageBindingCoordinator(provider,
                new ApplicationQueryRoleBindingResolver(schemas), schemas, new TargetResolver(target), policy,
                new CandidateReader(candidate), new CompositionQueryMaterializer(new ReadAdapter()),
                new ActionAdapter()).RenderAsync(selection, parsed, host, default);
        }

        var absent = await Render(new InMemoryPublicApplicationCatalogProvider(
            new Dictionary<ApplicationIdentifier, ICatalogNavigator>()), new AllowPolicy(candidate));
        var revoked = await Render(new InMemoryPublicApplicationCatalogProvider(
            new Dictionary<ApplicationIdentifier, ICatalogNavigator> { [fixture.ApplicationId] = catalog }),
            new AllowPolicy(candidate, allowed: false));

        Assert.Null(absent.Html);
        Assert.Equal("COMPOSITION_CATALOG_UNAVAILABLE", Assert.Single(absent.Errors).Code);
        Assert.Null(revoked.Html);
        Assert.Equal("COMPOSITION_QUERY_NOT_AUTHORIZED", Assert.Single(revoked.Errors).Code);
    }

    [Fact]
    public async Task Retained_action_selects_exact_active_mechanic_without_accepting_roles_or_scope()
    {
        await using var fixture = await Fixture.CreateAsync();
        var expectedMechanicId = fixture.ApplicationId.Value + ".mechanic.calculate";
        var (selection, document) = await PublishCompositionAsync(fixture, $$$$"""
            {"formatVersion":1,"generation":"retained","actions":[{"name":"calculate","mechanic":"{{{{expectedMechanicId}}}}"}],
             "components":[],"root":{"kind":"element","tag":"button","action":"calculate","children":[]}}
            """);
        var schemas = new BoundedJsonSchemaValidator();
        var catalog = ActionCatalog(selection, out var mechanicId, out var target);
        Assert.Equal(expectedMechanicId, mechanicId);
        var grant = new StandingGrantRevision("action@1", "action", 1, new string('D', 64),
            "principal." + new string('a', 64), fixture.ApplicationId, StandingGrantScope.Application, null,
            [StandingGrantCapability.Read, StandingGrantCapability.Execute],
            new(StandingGrantDefinitionMode.ExactIds, [mechanicId], []), [], 1,
            DateTime.UtcNow.AddMinutes(5), false, "fixture-operation");
        var action = new ActionAdapter(InteractionInvocationResult.CompletedComputation(
            "{\"changed\":true}", "fixture.action"));
        var coordinator = new CompositionPageBindingCoordinator(
            new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
                { [fixture.ApplicationId] = catalog }),
            new ApplicationQueryRoleBindingResolver(schemas), schemas, new TargetResolver(target),
            new AllowPolicy(grant), new CandidateReader(grant),
            new CompositionQueryMaterializer(new ReadAdapter()), action);
        var principal = TrustedPrincipalContext.VerifiedPrincipal(grant.PrincipalReference, "test");
        var pageHost = InteractionInvocationHost.ForApplication(principal,
            selection.Publication.ApplicationRevision, "page@1", "page.render",
            InteractionExecutionProfile.ReadOnly, new(1, DateTime.UtcNow.AddMinutes(1)));

        var rendered = await coordinator.RenderAsync(selection, document, pageHost, default);
        var result = await coordinator.InvokeActionAsync(selection, document, principal,
            "calculate", "web-page.action-1", "{\"amount\":2}", DateTime.UtcNow.AddMinutes(1), default);

        Assert.True(rendered.IsSuccess);
        Assert.Contains("/ui/example/actions/", rendered.Html, StringComparison.Ordinal);
        Assert.Contains("composition-bindings.js", rendered.Html, StringComparison.Ordinal);
        Assert.Equal(InteractionInvocationResultTag.Completed, result.Tag);
        var request = Assert.Single(action.Requests);
        Assert.Equal(mechanicId, request.QualifiedMechanicId);
        Assert.Equal("{\"amount\":2}", request.InputJson);
        Assert.Empty(request.RoleEntityIds);
        Assert.Null(request.Host.StateSpaceId);
        Assert.Equal(grant.GrantReference, request.Host.GrantReference);
    }

    [Fact]
    public async Task Stateful_action_binds_its_single_role_to_the_server_selected_page_entity_and_state_scope()
    {
        await using var fixture = await Fixture.CreateAsync();
        var expectedMechanicId = fixture.ApplicationId.Value + ".mechanic.update";
        var (selection, document) = await PublishCompositionAsync(fixture, $$$$"""
            {"formatVersion":1,"generation":"retained","actions":[{"name":"update","mechanic":"{{{{expectedMechanicId}}}}"}],
             "components":[],"root":{"kind":"element","tag":"button","action":"update","children":[]}}
            """);
        var schemas = new BoundedJsonSchemaValidator();
        var catalog = ActionCatalog(selection, out var mechanicId, out var target,
            "{\"roles\":{\"subject\":{\"components\":[]}}}");
        Assert.Equal(expectedMechanicId, mechanicId);
        var grant = new StandingGrantRevision("action@1", "action", 1, new string('D', 64),
            "principal." + new string('a', 64), fixture.ApplicationId,
            StandingGrantScope.StateSpace, selection.Publication.StateSpaceId,
            [StandingGrantCapability.Read, StandingGrantCapability.Execute],
            new(StandingGrantDefinitionMode.ExactIds, [mechanicId], []), ["component.set"], 1,
            DateTime.UtcNow.AddMinutes(5), false, "fixture-operation");
        var action = new ActionAdapter(InteractionInvocationResult.Committed(
            new("a".PadLeft(32, 'a'), new string('E', 64), [], false)));
        var candidates = new CandidateReader(grant);
        var coordinator = new CompositionPageBindingCoordinator(
            new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
                { [fixture.ApplicationId] = catalog }),
            new ApplicationQueryRoleBindingResolver(schemas), schemas, new TargetResolver(target),
            new AllowPolicy(grant), candidates, new CompositionQueryMaterializer(new ReadAdapter()),
            action, action);
        var principal = TrustedPrincipalContext.VerifiedPrincipal(grant.PrincipalReference, "test");

        var result = await coordinator.InvokeActionAsync(selection, document, principal,
            "update", "web-page.action-2", "{\"amount\":2}", DateTime.UtcNow.AddMinutes(1), default);

        Assert.Equal(InteractionInvocationResultTag.Committed, result.Tag);
        Assert.Equal(StandingGrantScope.StateSpace, candidates.RequestedScope);
        Assert.Equal(selection.Publication.StateSpaceId, candidates.RequestedStateSpaceId);
        var request = Assert.Single(action.Requests);
        Assert.Equal(selection.Publication.StateSpaceId, request.Host.StateSpaceId);
        Assert.Equal(InteractionStateRevision.From(selection.Publication), request.Host.StateRevision);
        Assert.Equal(new[] { "subject" }, request.RoleEntityIds.Keys);
        Assert.All(request.RoleEntityIds.Values, value => Assert.Equal(selection.Entity.EntityId, value));
    }

    [Fact]
    public async Task Stateful_action_with_no_roles_uses_state_scope_without_accepting_authored_subjects()
    {
        await using var fixture = await Fixture.CreateAsync();
        var expectedMechanicId = fixture.ApplicationId.Value + ".mechanic.update";
        var (selection, document) = await PublishCompositionAsync(fixture, $$$$"""
            {"formatVersion":1,"generation":"retained","actions":[{"name":"update","mechanic":"{{{{expectedMechanicId}}}}"}],
             "components":[],"root":{"kind":"element","tag":"button","action":"update","children":[]}}
            """);
        var schemas = new BoundedJsonSchemaValidator();
        var catalog = ActionCatalog(selection, out var mechanicId, out var target,
            "{\"effectComponentIds\":[\"counter\"]}");
        var grant = new StandingGrantRevision("action@1", "action", 1, new string('D', 64),
            "principal." + new string('a', 64), fixture.ApplicationId,
            StandingGrantScope.StateSpace, selection.Publication.StateSpaceId,
            [StandingGrantCapability.Read, StandingGrantCapability.Execute],
            new(StandingGrantDefinitionMode.ExactIds, [mechanicId], []), ["component.set"], 1,
            DateTime.UtcNow.AddMinutes(5), false, "fixture-operation");
        var action = new ActionAdapter(InteractionInvocationResult.Committed(
            new("b".PadLeft(32, 'b'), new string('E', 64), [], false)));
        var candidates = new CandidateReader(grant);
        var coordinator = new CompositionPageBindingCoordinator(
            new InMemoryPublicApplicationCatalogProvider(new Dictionary<ApplicationIdentifier, ICatalogNavigator>
                { [fixture.ApplicationId] = catalog }),
            new ApplicationQueryRoleBindingResolver(schemas), schemas, new TargetResolver(target),
            new AllowPolicy(grant), candidates, new CompositionQueryMaterializer(new ReadAdapter()),
            action, action);

        var result = await coordinator.InvokeActionAsync(selection, document,
            TrustedPrincipalContext.VerifiedPrincipal(grant.PrincipalReference, "test"),
            "update", "web-page.action-3", "{}", DateTime.UtcNow.AddMinutes(1), default);

        Assert.Equal(InteractionInvocationResultTag.Committed, result.Tag);
        Assert.Equal(StandingGrantScope.StateSpace, candidates.RequestedScope);
        Assert.Empty(Assert.Single(action.Requests).RoleEntityIds);
    }

    private static async Task<(WebPagePublicationSelection Selection, WebCompositionDocument Document)>
        PublishCompositionAsync(Fixture fixture, string composition)
    {
        await fixture.Content.AppendBundleDraftAsync(Fixture.ContentId, 2,
            new WebPageBundle("", [])
            {
                ContentFormat = WebPageContentFormat.Composition,
                CompositionJson = composition
            });
        await fixture.Publication.CompareExchangeContentReferenceAsync(
            await fixture.Publication.SelectDraftAsync(fixture.ApplicationId, Fixture.EntityId, 3));
        var selection = await fixture.Publication.SelectPublishedAsync(fixture.ApplicationId, Fixture.EntityId);
        var retained = await fixture.Publication.RevalidateSelectionAsync(selection);
        var parsed = new WebCompositionParser().Parse(retained.CompositionJson!, retained.Assets.Select(value => value.Path));
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors.Select(value => value.Code)));
        return (selection, parsed.Document!);
    }

    private static ICatalogNavigator Catalog(WebPagePublicationSelection selection,
        BoundedJsonSchemaValidator schemas, out string queryId, out StandingGrantDefinitionTarget target)
    {
        queryId = selection.Publication.ApplicationRevision.ApplicationId.Value + ".query.page-summary";
        const string schemaJson = "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"label\"],\"properties\":{\"label\":{\"type\":\"string\"}}}";
        var schema = schemas.Compile(schemaJson);
        var content = $$$$"""
            {"id":"{{{{queryId}}}}","category":"page","name":"Page summary","description":"Page summary.","matches":[],
             "roles":{"subject":"Selected page entity."},"executor":"mechanic-projection",
             "projection":{"qualifiedId":"{{{{selection.Publication.ApplicationRevision.ApplicationId.Value}}}}.projection.page-summary","version":1,"contentHash":"{{{{new string('A', 64)}}}}","outputSchemaHash":"{{{{schema.SchemaHash}}}}"},
             "outputSchema":{{{{schema.NormalizedSchema}}}},"exposure":"binding-only","status":"active",
             "roleBindings":{"subject":{"source":"route-entity"}}}
            """;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        var record = new CatalogRecordDefinition("bindings", "query", queryId, "Page summary", "Page summary.", [], [],
            "", "active", 1, content, hash, "fixture", "queries/page-summary.json");
        var manifest = CatalogNavigationManifest.Create(selection.Publication.ApplicationRevision.ApplicationId,
            new string('C', 64), "catalog-lexical-v1", [new("bindings", "Bindings", "Bindings.")],
            [new("bindings", "", "Bindings", "Bindings.", CatalogDescriptionStatus.Authored)], [record]);
        var resolution = CatalogExtensionResolutionContext.Create(selection.Publication.ApplicationRevision.ApplicationId,
            selection.Publication.ResolutionFingerprint, []);
        target = new(queryId, "query", selection.Publication.ApplicationRevision.ApplicationId,
            selection.Publication.ApplicationRevision.ApplicationId.Value, "fixture.query", 1, hash);
        return new InMemoryCatalogNavigator(manifest,
            new CatalogCursorCodec(Encoding.UTF8.GetBytes(new string('k', 64))), resolution);
    }

    private static ICatalogNavigator ActionCatalog(WebPagePublicationSelection selection,
        out string mechanicId, out StandingGrantDefinitionTarget target,
        string? requirementsJson = null)
    {
        mechanicId = selection.Publication.ApplicationRevision.ApplicationId.Value + ".mechanic.calculate";
        if (requirementsJson is not null) mechanicId = selection.Publication.ApplicationRevision.ApplicationId.Value + ".mechanic.update";
        var content = requirementsJson is null
            ? "{}"
            : JsonSerializer.Serialize(new { requirements = requirementsJson });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        var record = new CatalogRecordDefinition("bindings", "mechanic", mechanicId, "Calculate", "Calculate.", [], [],
            "", "active", 1, content, hash, "fixture", "mechanics/calculate.json");
        var manifest = CatalogNavigationManifest.Create(selection.Publication.ApplicationRevision.ApplicationId,
            new string('C', 64), "catalog-lexical-v1", [new("bindings", "Bindings", "Bindings.")],
            [new("bindings", "", "Bindings", "Bindings.", CatalogDescriptionStatus.Authored)], [record]);
        var resolution = CatalogExtensionResolutionContext.Create(selection.Publication.ApplicationRevision.ApplicationId,
            selection.Publication.ResolutionFingerprint, []);
        target = new(mechanicId, "mechanic", selection.Publication.ApplicationRevision.ApplicationId,
            selection.Publication.ApplicationRevision.ApplicationId.Value, "fixture.mechanic", 1, hash);
        return new InMemoryCatalogNavigator(manifest,
            new CatalogCursorCodec(Encoding.UTF8.GetBytes(new string('k', 64))), resolution);
    }

    private static StandingGrantRevision Grant(WebPagePublicationSelection selection, string queryId) =>
        new("query@1", "query", 1, new string('D', 64),
            "principal." + new string('a', 64), selection.Publication.ApplicationRevision.ApplicationId,
            StandingGrantScope.StateSpace, selection.Publication.StateSpaceId, [StandingGrantCapability.Read],
            new(StandingGrantDefinitionMode.ExactIds, [queryId], []), [], 1,
            DateTime.UtcNow.AddMinutes(5), false, "fixture-operation");

    private sealed class CandidateReader(StandingGrantRevision grant) : IStandingGrantReadCandidateReader
    {
        public StandingGrantScope? RequestedScope { get; private set; }
        public string? RequestedStateSpaceId { get; private set; }
        public Task<StandingGrantReadCandidateResult> ReadAsync(TrustedPrincipalContext principal,
            ApplicationIdentifier applicationId, CancellationToken cancellationToken = default) => Result();
        public Task<StandingGrantReadCandidateResult> ReadAsync(TrustedPrincipalContext principal,
            ApplicationIdentifier applicationId, StandingGrantScope scope, string? stateSpaceId,
            IReadOnlySet<StandingGrantCapability> requiredCapabilities, CancellationToken cancellationToken = default)
        {
            RequestedScope = scope;
            RequestedStateSpaceId = stateSpaceId;
            return Result();
        }
        private Task<StandingGrantReadCandidateResult> Result() => Task.FromResult(new StandingGrantReadCandidateResult(
            StandingGrantReadCandidateStatus.Available, "available", [grant]));
    }

    private sealed class TargetResolver(StandingGrantDefinitionTarget target) : IStandingGrantTargetResolver
    {
        public Task<StandingGrantTargetResolution> ResolveCurrentAsync(InteractionInvocationHost host,
            string definitionId, string kind, CancellationToken cancellationToken = default) => Task.FromResult(
                new StandingGrantTargetResolution(StandingGrantTargetResolutionStatus.Available, "available", target));
        public Task<StandingGrantTargetResolution> ResolveAsync(InteractionInvocationHost host,
            StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default) =>
            ResolveCurrentAsync(host, selection.DefinitionId, selection.Kind, cancellationToken);
        public Task<StandingGrantTargetResolution> ResolveCandidateAsync(InteractionInvocationHost host,
            DantesRoleplay.ApplicationActivation.ApplicationCandidateSnapshot candidate,
            StandingGrantDefinitionReference selection, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StandingGrantTargetResolution(StandingGrantTargetResolutionStatus.Unavailable,
                "unavailable", null));
    }

    private sealed class AllowPolicy(StandingGrantRevision grant, bool allowed = true) : IStandingGrantPolicy
    {
        public Task<StandingGrantDecision> EvaluateAsync(InteractionInvocationHost host,
            StandingGrantRequirement requirement, CancellationToken cancellationToken = default) => Task.FromResult(
                new StandingGrantDecision(allowed, allowed ? "allowed" : "revoked", allowed ? grant : null,
                    new(host.Principal.PrincipalId, host.Principal.AuthenticationMethod, "standing-grant",
                        host.StateSpaceId!, host.CommandId, allowed, allowed ? "allowed" : "revoked")));
    }

    private sealed class ReadAdapter : IStandingGrantApplicationReadModelInvocationAdapter
    {
        public List<ApplicationReadModelInvocationRequest> Requests { get; } = [];
        public Task<InteractionInvocationResult> ReadAsync(ApplicationReadModelInvocationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (!request.Host.Budget.TryConsumeOperation())
                return Task.FromResult(InteractionInvocationResult.Failed("INVOCATION_BUDGET_EXHAUSTED", "exhausted"));
            var hash = new string('A', 64);
            return Task.FromResult(InteractionInvocationResult.Completed("{\"label\":\"ready\"}",
                new(hash, hash, request.ExpectedContract.OutputSchemaHash, hash, hash)));
        }
    }

    private sealed class ActionAdapter(InteractionInvocationResult? result = null) :
        IApplicationActionInvocationAdapter, IStandingGrantApplicationActionInvocationAdapter
    {
        public List<ApplicationActionInvocationRequest> Requests { get; } = [];
        public Task<InteractionInvocationResult> ExecuteAsync(ApplicationActionInvocationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(result ?? InteractionInvocationResult.Unavailable("unused", "unused"));
        }
    }
}



