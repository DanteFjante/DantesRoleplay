using System.Text.Encodings.Web;
using System.Text.Json;
using DantesRoleplay.DataAccess;
using DantesRoleplay.MCPServer.Tools;
using DantesRoleplay.Mechanics;
using DantesRoleplay.RuleAccess;
using DantesRoleplay.World;
using DantesRoleplay.Information;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Retrieval;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.Sources;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Applications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol;

namespace DantesRoleplay.MCPServer;

/// <summary>
/// Everything this application registers, in one method both the host and the end-to-end test
/// call.
///
/// The test could have built its own equivalent service collection. That is exactly how a test
/// stops testing the thing it is named after: the day someone registers a fourth tool in
/// <c>Program.cs</c>, the test's private copy would still walk the old surface and still pass.
/// One method, two callers, no second version to keep in step.
/// </summary>
public static class ServerConfiguration
{
    /// <summary>
    /// The MCP endpoint path. Explicit rather than the root, so the endpoint is unambiguous with
    /// any future page route and the client's URL has a visible protocol in it.
    /// </summary>
    public const string McpEndpoint = "/mcp";

    /// <summary>
    /// How tool results are serialised on the way out.
    ///
    /// The default encoder escapes every quote as <c>\u0022</c>, so the envelope a client shows its
    /// model reads `query(kind: \u0022capabilities\u0022)` where the point of the field is that it
    /// can be copied and sent. It is correct JSON and unreadable prose, and this system's whole
    /// recovery story is a model reading a `fix` and making that call. Nothing here is rendered as
    /// HTML, so the escaping the strict encoder buys protects against nothing we do.
    /// </summary>
    private static readonly JsonSerializerOptions ResponseJson = new(McpJsonUtilities.DefaultOptions)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static IServiceCollection AddDantesRoleplayMcpServer(
        this IServiceCollection services,
        string connectionString,
        DatabaseProvider provider = DatabaseProvider.Sqlite,
        string? developmentInformationScope = null,
        IReadOnlyDictionary<string, string>? allowedSourceRoots = null,
        IReadOnlyCollection<string>? publishedApplicationCatalogs = null,
        IConfiguration? hostConfiguration = null)
    {
        // The kernel. One call registers the DbContext and every store.
        //
        // SQLite by default: one file you can copy to snapshot a campaign and delete to reset.
        // ARCHITECTURE.md §8.3 explains why there is no Postgres and no vector store yet, and
        // names the conditions that would change that.
        services.AddDantesRoleplayDataAccess(connectionString, provider);
        var configuredRoots = new ConfiguredAllowedSourceRootResolver(allowedSourceRoots);
        services.Replace(ServiceDescriptor.Singleton<IAllowedSourceRootResolver>(configuredRoots));
        services.Replace(ServiceDescriptor.Singleton<IAllowedSourceRootCatalog>(configuredRoots));
        services.Replace(ServiceDescriptor.Singleton<IPublicApplicationCatalogPolicy>(
            new ConfiguredPublicApplicationCatalogPolicy(publishedApplicationCatalogs)));
        services.AddHttpContextAccessor();
        services.TryAddSingleton<IPrivateOperatorAuthorizationPolicy, PrivateOperatorAuthorizationPolicy>();
        services.AddScoped<IPrivateOperatorRequestAuthorizer, McpPrivateOperatorAuthorizer>();
        services.Replace(ServiceDescriptor.Scoped<IInteractionAuthorizationPolicy, PrivateHostInteractionAuthorizationPolicy>());
        services.AddSingleton<IInformationScopePolicy>(new DevelopmentInformationScopePolicy(
            developmentInformationScope ?? "local.*"));
        services.AddScoped<IInformationAnswerCoordinator>(provider =>
        {
            var completion = provider.GetService<ILocalStructuredCompletionProvider>();
            return completion is null
                ? new UnavailableInformationAnswerCoordinator()
                : new InformationAnswerCoordinator(
                    provider.GetRequiredService<IInformationScopePolicy>(),
                    provider.GetRequiredService<IInformationStore>(),
                    completion);
        });
        services.AddScoped<IInformationActionCoordinator, InformationActionCoordinator>();
        services.AddScoped<IInformationActionExecutor, MechanicActionInformationExecutor>();
        var localKnowledgeSeat = new ConfigurationLocalKnowledgeSeatProvider(hostConfiguration);
        services.AddSingleton<ILocalKnowledgeSeatProvider>(localKnowledgeSeat);
        var configuredApplication = localKnowledgeSeat.Current().ApplicationId;
        services.AddSingleton(new KnowledgeApplicationSelection(
            ValidApplicationId(configuredApplication) ? configuredApplication : "disabled"));
        services.AddSingleton<IAuthorizedKnowledgeAudiencePolicy, LocalKnowledgeAudiencePolicy>();
        services.AddScoped<IKnowledgeApplicationBindingResolver, ActivatedKnowledgeApplicationBindingResolver>();
        services.AddScoped<IKnowledgeActorParticipationVerifier, ApplicationKnowledgeActorParticipationVerifier>();
        services.AddAuthorizedKnowledgeCore();
        // The sandbox that runs game rules. A singleton because it holds no state between runs:
        // every call builds a fresh Jint engine, which is what stops one mechanic seeing what
        // another left.
        //
        // Registered here by name rather than behind a helper, so that the one component in this
        // system that executes code an LLM wrote appears in the startup path a reader follows.
        services.AddSingleton<IMechanicEngine, JintMechanicEngine>();

        services
            .AddMcpServer()
            .WithHttpTransport(options =>
            {
                // Stateless: no server-to-client requests (sampling, elicitation) are needed.
                options.Stateless = true;
            })
            // The entire public surface. A guard test asserts these are exactly orient, query and
            // commit, and that the two dispatchers handle exactly the kinds the catalog offers.
            .WithTools<OrientTool>(ResponseJson)
            .WithTools<QueryTool>(ResponseJson)
            .WithTools<CommitTool>(ResponseJson);

        return services;
    }

    private static bool ValidApplicationId(string value)
    {
        try { return ApplicationIdentifier.Parse(value).Value == value; }
        catch (ArgumentException) { return false; }
    }

    /// <summary>
    /// Keeps the generic host usable when its optional local-completion component is disabled.
    /// Installing an <see cref="ILocalStructuredCompletionProvider"/> replaces this fallback at
    /// scoped-coordinator construction time; the MCP host never selects or starts a model itself.
    /// </summary>
    private sealed class UnavailableInformationAnswerCoordinator : IInformationAnswerCoordinator
    {
        public Task<InformationAnswerResult> AnswerAsync(
            InformationAnswerRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InformationAnswerResult.Unknown(
                "INFORMATION_MODEL_UNAVAILABLE",
                "The optional local answer model is not configured."));
    }
}
