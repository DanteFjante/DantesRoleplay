using DantesRoleplay.Actions;
using DantesRoleplay.Characters;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.DataAccess.Retrieval;
using DantesRoleplay.Campaign;
using DantesRoleplay.Quest;
using DantesRoleplay.Effects;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Events;
using DantesRoleplay.Notifications;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.Retrieval;
using DantesRoleplay.Snapshots;
using DantesRoleplay.SystemFeedback;
using DantesRoleplay.Story;
using DantesRoleplay.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess;

/// <summary>
/// Which database engine backs the kernel.
///
/// SQLite is the default and the right choice while the schema is moving: one file you can copy
/// to snapshot and delete to reset. Postgres exists as an option because the entity-component
/// model stores everything as JSON, and JSONB indexes that far better than SQLite's json1 —
/// so the day this stops being a single-user prototype, the switch is a connection string.
/// See ARCHITECTURE.md §8.3.
/// </summary>
public enum DatabaseProvider
{
    Sqlite,
    Postgres
}

public static class DataAccessServiceCollectionExtensions
{
    /// <summary>
    /// Registers the kernel. A host wires everything up with one call and needs to know nothing
    /// else about the internals.
    /// </summary>
    /// <param name="connectionString">
    /// For SQLite this may be a bare file path — the directory is created and it is turned into
    /// a proper connection string.
    /// </param>
    public static IServiceCollection AddDantesRoleplayDataAccess(
        this IServiceCollection services,
        string connectionString,
        DatabaseProvider provider = DatabaseProvider.Sqlite,
        KnowledgeRetrievalOptions? knowledgeRetrieval = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        knowledgeRetrieval ??= new KnowledgeRetrievalOptions();
        var invalidRetrieval = knowledgeRetrieval.Validate();
        if (invalidRetrieval is not null)
            throw new ArgumentException(invalidRetrieval, nameof(knowledgeRetrieval));

        services.AddDbContext<DantesRoleplayDbContext>(options =>
        {
            switch (provider)
            {
                case DatabaseProvider.Sqlite:
                    options.UseSqlite(NormaliseSqlite(connectionString));
                    break;

                case DatabaseProvider.Postgres:
                    // Requires the Npgsql.EntityFrameworkCore.PostgreSQL package. Left as a throw
                    // rather than a silent fallback so switching provider fails loudly and early.
                    throw new NotSupportedException(
                        "Postgres support needs the Npgsql.EntityFrameworkCore.PostgreSQL package. " +
                        "Add it to DantesRoleplay.DataAccess and replace this branch with " +
                        "options.UseNpgsql(connectionString).");

                default:
                    throw new ArgumentOutOfRangeException(nameof(provider), provider, null);
            }
        });

        services.AddScoped<IProcedureStore, ProcedureStore>();
        services.AddScoped<IStoryPlanStore, StoryPlanStore>();
        services.AddSingleton<StoryPlanWakeQueue>();
        services.AddScoped<StoryPlanActionExecutor>();
        services.AddScoped<IStoryPlanStepProcessor, StoryPlanStepProcessor>();
        services.AddScoped<IOperationLog, OperationLog>();
        services.AddScoped<IWorldStore, WorldStore>();
        services.AddScoped<IGraphProjectionReader, GraphProjectionReader>();
        services.AddScoped<IKnowledgeStateCoordinator, KnowledgeStateCoordinator>();
        services.AddScoped<IKnowledgeAcquisitionCoordinator, KnowledgeAcquisitionCoordinator>();
        services.AddScoped<IKnowledgeTimelineCoordinator, KnowledgeTimelineCoordinator>();
        if (provider == DatabaseProvider.Sqlite)
        {
            var sqlite = NormaliseSqlite(connectionString);
            services.AddSingleton(knowledgeRetrieval);
            services.AddSingleton(knowledgeRetrieval.Embedding);
            services.AddSingleton(knowledgeRetrieval.Vector);
            services.AddSingleton(knowledgeRetrieval.Completion);
            services.AddSingleton(knowledgeRetrieval.Background);
            services.AddSingleton<IKnowledgeLexicalIndex>(_ => new SqliteKnowledgeLexicalIndex(NormaliseSqlite(connectionString)));
            services.AddSingleton<ITextEmbeddingProvider>(_ =>
                new OllamaEmbeddingProvider(new HttpClient(), knowledgeRetrieval.Embedding));
            services.AddSingleton<IKnowledgeVectorIndex>(_ =>
                new SqliteVecKnowledgeVectorIndex(sqlite, knowledgeRetrieval.Vector));
            services.AddSingleton<ILocalStructuredCompletionProvider>(_ =>
                new OllamaStructuredCompletionProvider(new HttpClient(), knowledgeRetrieval.Completion));
            services.AddSingleton<KnowledgeBackgroundQueue>();
            services.AddSingleton<IKnowledgeBackgroundQueue>(provider =>
                provider.GetRequiredService<KnowledgeBackgroundQueue>());
            services.AddScoped<IKnowledgeSearchDocumentSource, KnowledgeSearchDocumentSource>();
            services.AddScoped<IKnowledgeLexicalSearchCoordinator, KnowledgeLexicalSearchCoordinator>();
            services.AddScoped<IKnowledgeHybridSearchCoordinator, KnowledgeHybridSearchCoordinator>();
            services.AddScoped<IKnowledgeFactAnswerCoordinator, KnowledgeFactAnswerCoordinator>();
            services.AddScoped<IKnowledgeReadAgentCoordinator, KnowledgeReadAgentCoordinator>();
            // IAuthenticatedCampaignAudiencePolicy is deliberately host-supplied. Without one,
            // this host-only player-safe path cannot resolve and therefore cannot be exposed.
            services.AddScoped<IAuthorizedKnowledgeCandidateResolver, AuthorizedKnowledgeCandidateResolver>();
            services.AddScoped<IAuthorizedKnowledgeAnswerCoordinator, AuthorizedKnowledgeAnswerCoordinator>();
            services.AddScoped<ILocalRouteProposalCoordinator, LocalRouteProposalCoordinator>();
            services.AddScoped<IProcedureBoundActionVerifier, ProcedureBoundActionVerifier>();
            services.AddScoped<StoryActionStepPreparer>();
            services.AddScoped<KnowledgeBackgroundJobProcessor>();
        }
        services.AddScoped<IJourneyPlanReader, JourneyPlanReader>();
        services.AddScoped<IModeAwareItineraryReader, ModeAwareItineraryReader>();
        services.AddScoped<ICampaignBlueprintValidator, CampaignBlueprintValidator>();
        services.AddScoped<ICampaignBootstrapper, CampaignBootstrapper>();
        services.AddScoped<ICampaignContinuityRunner, CampaignContinuityRunner>();
        services.AddScoped<ICampaignQuestContextRunner, CampaignQuestContextRunner>();
        services.AddScoped<ICampaignResumeReader, CampaignResumeReader>();
        services.AddScoped<ICampaignSessionValidator, CampaignSessionValidator>();
        services.AddScoped<ICampaignSessionStarter, CampaignSessionStarter>();
        services.AddScoped<ICampaignSessionResumeReader, CampaignSessionResumeReader>();
        services.AddScoped<ICampaignSessionEndValidator, CampaignSessionEndValidator>();
        services.AddScoped<ICampaignSessionEnder, CampaignSessionEnder>();
        services.AddScoped<ICampaignSessionRecapReader, CampaignSessionRecapReader>();
        services.AddScoped<ICampaignSessionCheckpointValidator, CampaignSessionCheckpointValidator>();
        services.AddScoped<ICampaignSessionCheckpointCreator, CampaignSessionCheckpointCreator>();
        services.AddScoped<ICampaignSessionEvidenceProducer, CampaignSessionEvidenceProducer>();
        services.AddScoped<ICampaignCharacterParticipationVerifier, CampaignCharacterParticipationVerifier>();
        services.AddScoped<ICampaignCharacterParticipationAttacher, CampaignCharacterParticipationAttacher>();
        services.AddScoped<ICampaignCharacterParticipationPlanner, CampaignCharacterParticipationPlanner>();
        services.AddScoped<ICampaignCharacterParticipationWithdrawalPlanner, CampaignCharacterParticipationWithdrawalPlanner>();
        services.AddScoped<ICharacterAbilityAssignmentValidator, CharacterAbilityAssignmentValidator>();
        services.AddScoped<ICharacterAbilityScoreRecorder, CharacterAbilityScoreRecorder>();
        services.AddScoped<IBackgroundAbilityScoreIncreaseResolver, BackgroundAbilityScoreIncreaseResolver>();
        services.AddScoped<ICharacterOriginLanguageResolver, CharacterOriginLanguageResolver>();
        services.AddScoped<ICharacterSpeciesSelectionResolver, CharacterSpeciesSelectionResolver>();
        services.AddScoped<ICharacterProfileRecorder, CharacterProfileRecorder>();
        services.AddScoped<IQuestCreator, QuestCreator>();
        services.AddScoped<IQuestLifecycleRunner, QuestLifecycleRunner>();
        services.AddScoped<IQuestSummaryReader, QuestSummaryReader>();
        services.AddScoped<IEffectApplier, EffectApplier>();
        services.AddScoped<IMechanicStore, MechanicStore>();
        services.AddScoped<IEventTypeStore, EventTypeStore>();
        services.AddScoped<ISubscriptionStore, SubscriptionStore>();
        services.AddScoped<IGuardRouter, GuardRouter>();
        services.AddScoped<IEventLedger, EventLedger>();
        services.AddScoped<IEventRouter, EventRouter>();
        services.AddScoped<INotificationStore, NotificationStore>();
        services.AddScoped<IProjectionResolver, ProjectionResolver>();
        services.AddScoped<IMechanicComposer, MechanicComposer>();
        services.AddScoped<ISnapshotPackageStore, SnapshotPackageStore>();
        services.AddScoped<ISystemFeedbackService, SystemFeedbackService>();
        services.AddScoped<ISystemFeedbackAdministrationService, SystemFeedbackAdministrationService>();
        services.AddScoped<ISystemFeedbackRetentionService, SystemFeedbackRetentionService>();
        services.AddScoped<IStagedWorldComposer, StagedWorldComposer>();

        // Missing until the end-to-end walk went looking for it. commit takes IActionRunner as a
        // parameter for every kind, not only "action", so an unregistered runner made the whole
        // write verb fail at invocation with "An error occurred invoking 'commit'" — no envelope,
        // no fix, nothing in history. Every direct-call test passed a null runner in, so nothing
        // below the protocol could have noticed.
        services.AddScoped<ActionRunner>();
        services.AddScoped<IActionRunner>(provider => provider.GetRequiredService<ActionRunner>());
        services.AddScoped<IStoryPlanActionRunner>(provider => provider.GetRequiredService<ActionRunner>());

        services.AddScoped<ProcedureSeeder>();
        services.AddScoped<EventTypeSeeder>();
        services.AddScoped<MechanicSeeder>();
        services.AddScoped<ContentHashBackfill>();

        return services;
    }

    /// <summary>
    /// Applies pending migrations, brings content fingerprints up to date, then seeds bootstrap
    /// contracts from the embedded markdown files. Called once by the host at startup.
    ///
    /// Migrate rather than EnsureCreated: the world schema is fixed, but the kernel still gains
    /// tables when a subsystem lands (mechanics, events), and EnsureCreated cannot evolve a
    /// database that already holds contracts you wrote.
    /// </summary>
    public static async Task InitialiseDantesRoleplayAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<DantesRoleplayDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        // BEFORE the seeders, not after. Both of them decide whether to write by comparing the
        // stored fingerprint against the file's, so running them against stale fingerprints would
        // append a pointless new version of every bootstrap record on the first start after this
        // landed — and then the fingerprints would agree, hiding the fact that it happened.
        var backfill = scope.ServiceProvider.GetRequiredService<ContentHashBackfill>();
        await backfill.RunAsync(cancellationToken);

        var seeder = scope.ServiceProvider.GetRequiredService<ProcedureSeeder>();
        await seeder.SeedAsync(cancellationToken);

        // The nine world.* structural event types, before any rule. Every accepted world change
        // records an event against one of them, so a database without them cannot change the world
        // at all — they are kernel contracts, not content, and a fresh install has to have them
        // without anyone remembering to import a catalog first.
        var eventTypes = scope.ServiceProvider.GetRequiredService<EventTypeSeeder>();
        await eventTypes.SeedAsync(cancellationToken);

        // The bootstrap rules, after the contracts, so that a fresh database has both the manual
        // and two worked examples of what the manual is describing.
        var rules = scope.ServiceProvider.GetRequiredService<MechanicSeeder>();
        await rules.SeedAsync(cancellationToken);
    }

    private static string NormaliseSqlite(string connectionStringOrPath)
    {
        if (connectionStringOrPath.Contains('=', StringComparison.Ordinal))
        {
            return connectionStringOrPath;
        }

        var full = Path.GetFullPath(connectionStringOrPath);
        var directory = Path.GetDirectoryName(full);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return $"Data Source={full}";
    }
}
