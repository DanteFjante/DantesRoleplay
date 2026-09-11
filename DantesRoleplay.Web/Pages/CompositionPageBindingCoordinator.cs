using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Interactions;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Web.Hosting;
using DantesRoleplay.Web.Data;

namespace DantesRoleplay.Web.Pages;

/// <summary>Resolves retained binding intent to exact current catalog records and current grants.</summary>
public sealed class CompositionPageBindingCoordinator(
    IPublicApplicationCatalogProvider catalogs,
    IApplicationQueryRoleBindingResolver roleBindings,
    IBoundedJsonSchemaValidator schemas,
    IStandingGrantTargetResolver targets,
    IStandingGrantPolicy grants,
    IStandingGrantReadCandidateReader candidates,
    CompositionQueryMaterializer materializer,
    IApplicationActionInvocationAdapter actions)
{
    public async Task<WebCompositionRenderResult> RenderAsync(
        WebPagePublicationSelection selection,
        WebCompositionDocument document,
        InteractionInvocationHost pageHost,
        CancellationToken cancellationToken)
    {
        if (document.QueryBindings.Count == 0 && document.ActionBindings.Count == 0)
            return new WebCompositionRenderer().Render(document,
                new Dictionary<string, JsonElement>(), selection.AssetBasePath);
        if (document.QueryBindings.Count > 15 || pageHost.Budget.MaximumOperations != document.QueryBindings.Count + 1)
            return Error("COMPOSITION_QUERY_BUDGET_INVALID", "The page query set exceeds its shared operation budget.");
        var application = selection.Publication.ApplicationRevision;
        if (!TryCatalog(selection, out var catalog)) return UnavailableCatalog();
        foreach (var action in document.ActionBindings.Values)
            if (action.QualifiedMechanicId is not { } mechanicId
                || !TryRecord(catalog, application.ApplicationId, mechanicId, "mechanic", selection, out _))
                return Error("COMPOSITION_ACTION_SELECTION_UNAVAILABLE", "A declared page action is unavailable.");
        if (document.QueryBindings.Count == 0)
            return WithActions(new WebCompositionRenderer().Render(document,
                new Dictionary<string, JsonElement>(), selection.AssetBasePath), document, selection);

        var selected = new Dictionary<string, QuerySelection>(StringComparer.Ordinal);
        foreach (var pair in document.QueryBindings.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            if (pair.Value.QualifiedQueryId is not { } queryId
                || !TryRecord(catalog, application.ApplicationId, queryId, "query", selection, out var record))
                return Error("COMPOSITION_QUERY_SELECTION_UNAVAILABLE", "A declared page query is unavailable.");
            ApplicationQueryContract contract;
            try { contract = ApplicationQueryContract.Parse(record!.ContentJson, application.ApplicationId); }
            catch (ArgumentException) { return Error("COMPOSITION_QUERY_CONTRACT_INVALID", "A declared page query is invalid."); }
            if (contract.Status != "active" || contract.Id != queryId || contract.RoleBindings is null
                || contract.RoleBindings.Values.Any(value => value.Source != "route-entity"))
                return Error("COMPOSITION_QUERY_ROLES_UNAVAILABLE", "A page query must bind every role from its server-selected route entity.");
            var schema = schemas.Compile(contract.OutputSchemaJson);
            if (!schema.IsAccepted || schema.SchemaHash != contract.OutputSchemaHash)
                return Error("COMPOSITION_QUERY_SCHEMA_STALE", "A page query schema is unavailable.");
            InteractionQueryContractReference expected;
            IReadOnlyDictionary<string, string> roles;
            try
            {
                expected = new(contract.Executor, contract.ProjectionQualifiedId, contract.ProjectionVersion,
                    contract.ProjectionContentHash, schema.SchemaHash, schema.NormalizedSchema,
                    contract.Exposure, contract.Roles.Keys, contract.ObjectCollectionId);
                roles = roleBindings.Resolve(contract, pair.Value.InputJson,
                    new(selection.Entity.EntityId, new ReadOnlyDictionary<string, string>(
                        new Dictionary<string, string>())));
            }
            catch (Exception exception) when (exception is InteractionContractException or ApplicationReadModelException)
            {
                return Error("COMPOSITION_QUERY_BINDING_INVALID", "A declared page query binding is invalid.");
            }
            selected.Add(pair.Key, new(queryId, expected, roles, pair.Value.InputJson));
        }

        var grantCandidates = await candidates.ReadAsync(pageHost.Principal, application.ApplicationId,
            StandingGrantScope.StateSpace, selection.Publication.StateSpaceId,
            new HashSet<StandingGrantCapability> { StandingGrantCapability.Read }, cancellationToken);
        if (grantCandidates.Status != StandingGrantReadCandidateStatus.Available)
            return Error("COMPOSITION_QUERY_AUTHORITY_UNAVAILABLE", "Page query authority is unavailable.");
        foreach (var candidate in grantCandidates.Candidates)
        {
            var queryBudget = pageHost.Budget.Child(document.QueryBindings.Count, pageHost.Budget.DeadlineUtc);
            var host = new InteractionInvocationHost(pageHost.Principal, application,
                selection.Publication.StateSpaceId, candidate.GrantReference, pageHost.CommandId + ".queries",
                InteractionStateRevision.From(selection.Publication), InteractionExecutionProfile.ReadOnly,
                queryBudget, pageHost.CommandId);
            var resolvedTargets = new List<StandingGrantDefinitionTarget>();
            var exact = true;
            foreach (var value in selected.Values)
            {
                var resolved = await targets.ResolveCurrentAsync(host, value.QueryId, "query", cancellationToken);
                if (resolved is not { Status: StandingGrantTargetResolutionStatus.Available, Target: { } target }
                    || target.DefinitionId != value.QueryId || target.Kind != "query"
                    || target.OwnerApplicationId != application.ApplicationId)
                { exact = false; break; }
                resolvedTargets.Add(target);
            }
            if (!exact) continue;
            var decision = await grants.EvaluateAsync(host, new(StandingGrantCapability.Read,
                StandingGrantScope.StateSpace, resolvedTargets.AsReadOnly(), []), cancellationToken);
            if (!decision.Allowed) continue;
            var requests = selected.ToDictionary(pair => pair.Key, pair =>
                new ApplicationReadModelInvocationRequest(host, pair.Value.QueryId, pair.Value.Contract,
                    pair.Value.Roles, pair.Value.InputJson), StringComparer.Ordinal);
            var materialized = await materializer.ReadAsync(host, requests, cancellationToken);
            if (materialized.Results.Values.Any(result => result.Tag != InteractionInvocationResultTag.Completed))
                return Error("COMPOSITION_QUERY_READ_FAILED", "A page query could not be completed.");
            var rendered = new WebCompositionRenderer().Render(document, materialized.Values, selection.AssetBasePath);
            return rendered.IsSuccess
                ? WithActions(rendered, document, selection)
                : Error("WEB_COMPOSITION_INVALID", "The page composition could not be rendered.");
        }
        return Error("COMPOSITION_QUERY_NOT_AUTHORIZED", "The current principal is not authorized for every page query.");
    }

    public async Task<InteractionInvocationResult> InvokeActionAsync(
        WebPagePublicationSelection selection,
        WebCompositionDocument document,
        TrustedPrincipalContext principal,
        string bindingName,
        string commandId,
        string inputJson,
        DateTime deadlineUtc,
        CancellationToken cancellationToken)
    {
        if (!document.ActionBindings.TryGetValue(bindingName, out var binding)
            || binding.QualifiedMechanicId is not { } mechanicId)
            return InteractionInvocationResult.Failed("COMPOSITION_ACTION_BINDING_UNKNOWN", "The selected page action is unavailable.");
        if (!TryCatalog(selection, out var catalog)
            || !TryRecord(catalog, selection.Publication.ApplicationRevision.ApplicationId,
                mechanicId, "mechanic", selection, out var record))
            return InteractionInvocationResult.Unavailable("COMPOSITION_ACTION_SELECTION_UNAVAILABLE", "The exact page action is unavailable.");
        string canonicalInput;
        try { canonicalInput = InteractionCanonicalJson.CanonicalizeObject(inputJson); }
        catch (InteractionContractException exception)
        { return InteractionInvocationResult.Failed(exception.Code, "The page action input is invalid."); }
        if (Encoding.UTF8.GetByteCount(canonicalInput) > InteractionContractLimits.JsonBytes)
            return InteractionInvocationResult.Failed("JSON_TOO_LARGE", "The page action input is too large.");

        var application = selection.Publication.ApplicationRevision;
        var grantCandidates = await candidates.ReadAsync(principal, application.ApplicationId,
            StandingGrantScope.Application, null,
            new HashSet<StandingGrantCapability> { StandingGrantCapability.Read, StandingGrantCapability.Execute },
            cancellationToken);
        if (grantCandidates.Status != StandingGrantReadCandidateStatus.Available)
            return InteractionInvocationResult.Unavailable("COMPOSITION_ACTION_AUTHORITY_UNAVAILABLE", "Page action authority is unavailable.");
        var budget = new InteractionInvocationBudget(1, deadlineUtc);
        foreach (var candidate in grantCandidates.Candidates)
        {
            var host = InteractionInvocationHost.ForApplication(principal, application, candidate.GrantReference, commandId,
                InteractionExecutionProfile.Atomic, budget);
            var result = await actions.ExecuteAsync(new(host, mechanicId, record!.Summary.Version,
                record.Summary.ContentFingerprint, new Dictionary<string, string>(), canonicalInput), cancellationToken);
            if (result.Code == "INVOCATION_NOT_AUTHORIZED") continue;
            return result;
        }
        return InteractionInvocationResult.Failed("INVOCATION_NOT_AUTHORIZED", "The page action is not authorized.");
    }

    private bool TryCatalog(WebPagePublicationSelection selection, out ICatalogNavigator catalog) =>
        catalogs.TryGet(selection.Publication.ApplicationRevision.ApplicationId, out catalog!);

    private static bool TryRecord(ICatalogNavigator catalog, DantesRoleplay.Applications.ApplicationIdentifier application,
        string qualifiedId, string kind, WebPagePublicationSelection selection, out CatalogRecordView? record)
    {
        record = null;
        try
        {
            var effective = catalog.EffectiveContent(new(application, PageSize: 100,
                Kinds: [kind], QualifiedIds: [qualifiedId]));
            if (effective.ResolutionFingerprint != selection.Publication.ResolutionFingerprint) return false;
            var winner = effective.ResolvedWinners.SingleOrDefault(value => value.Record.Kind == kind
                && value.Record.QualifiedId == qualifiedId && value.Record.Status == "active");
            if (winner is null) return false;
            record = catalog.Inspect(new(application, winner.Record.Collection, qualifiedId));
            return record.Summary == winner.Record;
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException or InvalidOperationException)
        { return false; }
    }

    private static WebCompositionRenderResult Error(string code, string message) =>
        new(null, [new("$", code, message)]);
    private static WebCompositionRenderResult UnavailableCatalog() =>
        Error("COMPOSITION_CATALOG_UNAVAILABLE", "The exact active application catalog is unavailable.");

    private static WebCompositionRenderResult WithActions(WebCompositionRenderResult rendered,
        WebCompositionDocument document, WebPagePublicationSelection selection)
    {
        if (!rendered.IsSuccess || document.ActionBindings.Count == 0) return rendered;
        var names = document.ActionBindings.Where(pair => pair.Value.QualifiedMechanicId is not null)
            .Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
        if (names.Length != document.ActionBindings.Count)
            return Error("COMPOSITION_ACTION_SELECTION_UNAVAILABLE", "A declared page action is unavailable.");
        var contentMarker = "/content/";
        var marker = selection.AssetBasePath.IndexOf(contentMarker, StringComparison.Ordinal);
        if (marker < 0) return Error("COMPOSITION_ACTION_ROUTE_UNAVAILABLE", "The page action route is unavailable.");
        var route = selection.AssetBasePath[..marker] + "/actions/";
        var configuration = JsonSerializer.Serialize(new { route, names });
        var script = """
            <div data-web-action-result aria-live="polite"></div>
            <script type="module">
            import { renderInvocation } from "/components/composition-bindings.js";
            const configuration = __WEB_ACTION_CONFIGURATION__;
            const available = new Set(configuration.names);
            const result = document.querySelector("[data-web-action-result]");
            for (const button of document.querySelectorAll("button[data-web-action]")) {
              const binding = button.getAttribute("data-web-action");
              if (!available.has(binding)) continue;
              button.disabled = false; button.setAttribute("aria-disabled", "false");
              button.addEventListener("click", async event => {
                event.preventDefault(); if (button.disabled) return;
                button.disabled = true; button.setAttribute("aria-disabled", "true");
                const commandId = "web-page." + crypto.randomUUID().replaceAll("-", "");
                try {
                  const response = await fetch(configuration.route + encodeURIComponent(binding), {
                    method: "POST", headers: {"content-type": "application/json"},
                    body: JSON.stringify({commandId, input: {}})
                  });
                  const value = await response.json();
                  const view = renderInvocation(result, value);
                  if (view.refresh) location.reload();
                  else if (!["pending", "unavailable"].includes(view.tag) && view.recoveryIdentity === null) {
                    button.disabled = false; button.setAttribute("aria-disabled", "false");
                  }
                } catch { renderInvocation(result, {tag:"unavailable",code:"COMPOSITION_ACTION_UNRESOLVED",message:"The action result is unknown. Reconcile before retrying.",dataJson:null,readEvidence:null,receipt:null,proposal:null,pending:null,completionEvidenceReference:null,previousCommits:[],recoveryIdentity:null}); }
              });
            }
            </script>
            """.Replace("__WEB_ACTION_CONFIGURATION__", configuration, StringComparison.Ordinal);
        if (rendered.Html!.Length > WebComposition.MaximumRenderedCharacters - script.Length)
            return Error("RENDER_OUTPUT_LIMIT_EXCEEDED", "The rendered page exceeds its output limit.");
        return new(rendered.Html + script, rendered.Errors);
    }
    private sealed record QuerySelection(string QueryId, InteractionQueryContractReference Contract,
        IReadOnlyDictionary<string, string> Roles, string InputJson);
}
