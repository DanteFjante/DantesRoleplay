using System.ComponentModel;
using System.Text.Json.Nodes;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.Capabilities;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Operations;
using DantesRoleplay.StateSpaceAdministration;
using DantesRoleplay.SystemCapabilities;
using ModelContextProtocol.Server;

namespace DantesRoleplay.MCPServer.Mcp;

/// <summary>
/// A current, authorization-scoped projection over registered capability descriptors and runtime
/// application owners. It contains no hand-maintained capability inventory.
/// </summary>
[McpServerToolType]
public sealed class OrientMcpTool
{
    private const int DiscoveryLimit = 100;

    [McpServerTool(Name = "orient")]
    [Description(
        "START HERE. Returns the current principal and audience boundary, authorized applications " +
        "and state spaces, registered capability families, schema links, and deprecated-route " +
        "replacements. Read-only and safe to call again whenever context changes.")]
    public Task<ToolEnvelope> OrientAsync(
        IOperationLog log,
        CancellationToken cancellationToken = default,
        IPrivateOperatorRequestAuthorizer? privateOperator = null,
        IApplicationRegistry? applications = null,
        IStateSpaceAdministrationReader? stateSpaces = null,
        ISystemCapabilityCatalog? systemCapabilities = null,
        ILocalKnowledgeSeatProvider? localKnowledgeSeats = null,
        IAuthorizedKnowledgeAudiencePolicy? knowledgeAudiences = null,
        IKnowledgeApplicationBindingResolver? knowledgeBindings = null,
        IKnowledgeActorParticipationVerifier? knowledgeParticipation = null) =>
        ToolRunner.RunAsync(log, "orient", async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = Authorize(privateOperator, PrivateOperatorCapability.Read);
            var modify = Authorize(privateOperator, PrivateOperatorCapability.Modify);
            var audience = await SystemAudienceContextHandler.ResolveAsync(
                localKnowledgeSeats, knowledgeAudiences, knowledgeBindings, knowledgeParticipation,
                cancellationToken);

            var mcpContracts = McpVerbCatalog.Descriptors
                .Where(value => Authorized(value, read.Allowed, modify.Allowed))
                .Select(value => new RegisteredCapability(value, "mcp"))
                .ToArray();
            var directContracts = DiscoverDirect(systemCapabilities, read)
                .Where(value => value.Authorization.Sensitivity != "secret")
                .Where(value => Authorized(value, read.Allowed, modify.Allowed))
                .Select(value => new RegisteredCapability(value, "direct-ai"))
                .ToArray();
            var registered = mcpContracts.Concat(directContracts).ToArray();
            var active = registered
                .Where(value => value.Contract.Lifecycle == CapabilityContractLifecycle.Active)
                .OrderBy(value => value.Contract.Id, StringComparer.Ordinal)
                .ThenBy(value => value.Interface, StringComparer.Ordinal)
                .ToArray();
            var deprecated = registered
                .Where(value => value.Contract.Lifecycle == CapabilityContractLifecycle.Deprecated)
                .OrderBy(value => value.Contract.Id, StringComparer.Ordinal)
                .Select(value => DeprecatedView(value.Contract))
                .ToArray();
            var families = active.GroupBy(Family)
                .OrderBy(value => FamilyOrder(value.Key))
                .Select(value => new
                {
                    Id = value.Key,
                    Description = FamilyDescription(value.Key),
                    Capabilities = value.Select(CapabilityView).ToArray()
                })
                .ToArray();
            var applicationView = Applications(applications, stateSpaces, read);
            var nextActions = NextActions(active.Select(value => value.Contract).ToArray(),
                applicationView, read.Allowed);
            var nextSteps = nextActions.Select(McpNextActionFactory.Advice).ToArray();

            var data = new
            {
                GeneratedFrom = new
                {
                    McpDescriptorCount = mcpContracts.Length,
                    DirectAiDescriptorCount = directContracts.Length,
                    ApplicationRegistryAvailable = applications is not null,
                    StateSpaceRegistryAvailable = stateSpaces is not null
                },
                Principal = new
                {
                    Reference = read.Evidence.PrincipalReference,
                    AuthenticationMethod = read.Evidence.AuthenticationMethod,
                    Scope = read.Evidence.Scope,
                    CanRead = read.Allowed,
                    CanModify = modify.Allowed,
                    ReadDecision = read.Code,
                    ModifyDecision = modify.Code
                },
                Audience = audience.Error is null
                    ? new { Status = "bound", Context = audience.Data, Error = (object?)null }
                    : new
                    {
                        Status = "unavailable",
                        Context = (object?)null,
                        Error = (object?)new
                        {
                            audience.Error.Code,
                            audience.Error.Why,
                            audience.Error.Fix
                        }
                    },
                Applications = applicationView,
                CapabilityFamilies = families,
                Schemas = new
                {
                    McpCatalog = "query(kind: \"capabilities\")",
                    Rule = "Each capability entry names its descriptor id, fingerprint, and input/output schema hashes. " +
                        "MCP schemas are read from the MCP catalog; direct-AI schemas are supplied by that tool definition."
                },
                Limitations = new
                {
                    Authorization = read.Allowed
                        ? Array.Empty<object>()
                        : new object[] { new { read.Code, read.Recovery } },
                    DeprecatedCapabilities = deprecated,
                    Rule = "A deprecated capability is callable only for compatibility and is never included in an active family. " +
                        "Use its structured replacement capability."
                }
            };

            return new ToolOutcome(data,
                $"Oriented from {active.Length} currently authorized active capability descriptor(s), " +
                    $"{applicationView.Items.Count} application(s), and {applicationView.StateSpaceCount} state space(s).",
                nextSteps,
                NextActions: nextActions);
        });

    private static PrivateOperatorAuthorizationDecision Authorize(
        IPrivateOperatorRequestAuthorizer? authorizer,
        PrivateOperatorCapability capability)
    {
        if (authorizer is not null) return authorizer.Authorize(capability);
        PrivateOperatorCapabilityNames.TryGetAuditName(capability, out var name);
        var evidence = new AuthorizationAuditEvidence("", "", name,
            PrivateOperatorAuthorizationPolicy.PrivateHostScope, "orient", false,
            "PRIVATE_OPERATOR_AUTHORIZATION_UNAVAILABLE");
        return new(false, evidence.ReasonCode,
            "Authenticate through the configured private host and call orient again.", evidence);
    }

    private static IReadOnlyList<CapabilityContractDescriptor> DiscoverDirect(
        ISystemCapabilityCatalog? catalog,
        PrivateOperatorAuthorizationDecision read)
    {
        if (catalog is null || !read.Allowed) return [];
        var discovery = catalog.Discover(SystemCapabilityInvocationContext.FromAuthorization(read.Evidence));
        return discovery.Ok
            ? discovery.Capabilities.Select(value => value.Contract).ToArray()
            : [];
    }

    private static bool Authorized(CapabilityContractDescriptor descriptor, bool canRead, bool canModify) =>
        descriptor.Operations.ChangesState ? canModify : canRead;

    private static ApplicationOrientation Applications(
        IApplicationRegistry? applications,
        IStateSpaceAdministrationReader? stateSpaces,
        PrivateOperatorAuthorizationDecision read)
    {
        if (!read.Allowed)
            return new("denied", [], 0, false,
                "Application and state-space metadata require the current private read grant.");
        if (applications is null)
            return new("unavailable", [], 0, false,
                "The application registry is not configured in this host.");

        var page = applications.ListPage(null, DiscoveryLimit);
        var count = 0;
        var items = page.Applications.Select(registration =>
        {
            var revision = applications.Get(registration.Id)!;
            var spaces = stateSpaces?.List(registration.Id, DiscoveryLimit) ?? [];
            count += spaces.Count;
            return new ApplicationView(
                registration.Id.Value,
                registration.DisplayName,
                registration.Description,
                revision.Revision,
                revision.Fingerprint,
                registration.BaseApplications.Select(value => value.Value).ToArray(),
                spaces.Select(value => new StateSpaceView(
                    value.StateSpaceId,
                    value.Scope.ToString().ToLowerInvariant(),
                    value.ApplicationRevision,
                    value.BindingRevision,
                    value.BindingFingerprint,
                    value.ActiveFingerprint)).ToArray(),
                $"query(kind: \"system.catalogs\", applicationId: \"{registration.Id.Value}\")",
                $"query(kind: \"system.feature-search\", applicationId: \"{registration.Id.Value}\", query: \"describe the needed capability\")");
        }).ToArray();
        return new("authorized", items, count, page.NextApplicationId is not null,
            stateSpaces is null
                ? "Applications are visible, but the state-space registry is unavailable."
                : "Every listed state space is visible under the current private read grant.");
    }

    private static string Family(RegisteredCapability value)
    {
        var id = value.Contract.Id;
        if (value.Contract.SourceKind == "application-mechanic"
            || id.EndsWith(".application.action.execute", StringComparison.Ordinal))
            return "direct-execution";
        if (id.Contains(".system.interaction-", StringComparison.Ordinal)
            || id.StartsWith("system.interaction-", StringComparison.Ordinal))
            return "planned-interaction";
        if (!value.Contract.Operations.ChangesState) return "read-query";
        if (id.StartsWith("system.mechanic-sandbox.", StringComparison.Ordinal))
            return "draft-authoring";
        return "state-change";
    }

    private static int FamilyOrder(string value) => value switch
    {
        "read-query" => 0,
        "direct-execution" => 1,
        "planned-interaction" => 2,
        "draft-authoring" => 3,
        _ => 4
    };

    private static string FamilyDescription(string value) => value switch
    {
        "read-query" => "Read current state without changing it.",
        "direct-execution" => "Execute an already selected exact application mechanic in one confirmed idempotent call.",
        "planned-interaction" => "Resolve or verify ambiguity as an inert proposal before confirmed execution.",
        "draft-authoring" => "Create or revise reusable definitions and mechanics; preview when the descriptor allows it.",
        _ => "Apply an authorized state or administration change under the descriptor's confirmation and idempotency rules."
    };

    private static object CapabilityView(RegisteredCapability value)
    {
        var descriptor = value.Contract;
        return new
        {
            descriptor.Id,
            descriptor.Version,
            descriptor.Fingerprint,
            descriptor.Owner,
            descriptor.Name,
            descriptor.Description,
            Interface = value.Interface,
            descriptor.Operations,
            descriptor.Scope,
            descriptor.Authorization,
            descriptor.RequiresConfirmation,
            descriptor.RequiresIdempotencyKey,
            Schemas = new
            {
                InputHash = descriptor.Input.SchemaHash,
                InputStatus = descriptor.Input.Status,
                OutputHash = descriptor.Output.SchemaHash,
                OutputStatus = descriptor.Output.Status,
                ReadFrom = value.Interface == "mcp"
                    ? "query(kind: \"capabilities\")"
                    : $"direct-ai-tool-definition:{descriptor.Id}"
            }
        };
    }

    private static object DeprecatedView(CapabilityContractDescriptor descriptor) => new
    {
        descriptor.Id,
        descriptor.Version,
        descriptor.Fingerprint,
        descriptor.Description,
        descriptor.Lifecycle,
        Replacements = descriptor.RecoveryActions.Select(value => new
        {
            value.CapabilityId,
            value.Description,
            value.InputJson
        }).ToArray()
    };

    private static IReadOnlyList<ToolNextAction> NextActions(
        IReadOnlyList<CapabilityContractDescriptor> descriptors,
        ApplicationOrientation applications,
        bool canRead)
    {
        var ids = descriptors.Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        var result = new List<ToolNextAction>();
        if (ids.Contains("mcp.query.capabilities"))
            result.Add(McpNextActionFactory.Create(
                "read-capability-contracts",
                "Read exact current schemas and recovery contracts.",
                "mcp.query.capabilities", new JsonObject(), []));
        if (ids.Contains("mcp.query.system.audience-context"))
            result.Add(McpNextActionFactory.Create(
                "refresh-audience-context",
                "Refresh the host-bound table and actor boundary.",
                "mcp.query.system.audience-context", new JsonObject(), []));
        if (canRead && ids.Contains("mcp.query.system.applications"))
            result.Add(McpNextActionFactory.Create(
                "inspect-applications",
                "Inspect current application activation and state-space evidence.",
                "mcp.query.system.applications", new JsonObject(), []));
        if (ids.Contains("mcp.query.system.interaction-plan"))
        {
            var known = new JsonObject();
            var missing = new List<McpNextActionFactory.MissingArgument>();
            var applicationId = applications.Items.FirstOrDefault()?.Id;
            if (applicationId is null)
                missing.Add(new("applicationId", "Choose one currently authorized application id.",
                    JsonValue.Create("application-id")!));
            else
                known["applicationId"] = applicationId;
            missing.Add(new("request", "Describe the ambiguous request to resolve without changing state.",
                JsonValue.Create("describe the intended action")!));
            result.Add(McpNextActionFactory.Create(
                "plan-ambiguous-request",
                "Resolve an ambiguous request without changing state.",
                "mcp.query.system.interaction-plan", known, missing, "applicationId", "request"));
        }
        if (ids.Contains("mcp.query.procedures"))
            result.Add(McpNextActionFactory.Create(
                "read-operating-contract",
                "Read the operating contract.",
                "mcp.query.procedures", new JsonObject { ["id"] = "procedure.system.use" }, [], "id"));
        return result;
    }

    private sealed record RegisteredCapability(CapabilityContractDescriptor Contract, string Interface);
    private sealed record ApplicationOrientation(
        string Status,
        IReadOnlyList<ApplicationView> Items,
        int StateSpaceCount,
        bool Truncated,
        string Boundary);
    private sealed record ApplicationView(
        string Id,
        string DisplayName,
        string Description,
        int Revision,
        string Fingerprint,
        IReadOnlyList<string> BaseApplications,
        IReadOnlyList<StateSpaceView> StateSpaces,
        string Catalogs,
        string CapabilitySearch);
    private sealed record StateSpaceView(
        string Id,
        string Scope,
        int ApplicationRevision,
        int BindingRevision,
        string BindingFingerprint,
        string ActiveFingerprint);
}
