using DantesRoleplay.Actions;
using DantesRoleplay.Campaign;
using DantesRoleplay.Characters;
using DantesRoleplay.DataAccess.Retrieval;
using DantesRoleplay.Quest;
using DantesRoleplay.Retrieval;
using DantesRoleplay.Story;
using DantesRoleplay.World;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess.Composition;

internal static class DantesRoleplayGameAdapterRegistration
{
    internal static IServiceCollection AddDantesRoleplayGameAdapters(
        this IServiceCollection services,
        DatabaseProvider provider,
        string sqliteConnectionString,
        KnowledgeRetrievalOptions knowledgeRetrieval)
    {
        services.AddScoped<IStoryPlanStore, StoryPlanStore>();
        services.AddSingleton<StoryPlanWakeQueue>();
        services.AddScoped<StoryPlanActionExecutor>();
        services.AddScoped<IKnowledgeStateCoordinator, KnowledgeStateCoordinator>();
        services.AddScoped<IKnowledgeAcquisitionCoordinator, KnowledgeAcquisitionCoordinator>();
        services.AddScoped<IKnowledgeTimelineCoordinator, KnowledgeTimelineCoordinator>();

        if (provider == DatabaseProvider.Sqlite)
        {
            services.AddSingleton(knowledgeRetrieval);
            services.AddSingleton(knowledgeRetrieval.Embedding);
            services.AddSingleton(knowledgeRetrieval.Vector);
            services.AddSingleton(knowledgeRetrieval.Completion);
            services.AddSingleton(knowledgeRetrieval.Background);
            services.AddSingleton<IKnowledgeLexicalIndex>(_ =>
                new SqliteKnowledgeLexicalIndex(sqliteConnectionString));
            services.AddSingleton<ITextEmbeddingProvider>(_ =>
                new OllamaEmbeddingProvider(new HttpClient(), knowledgeRetrieval.Embedding));
            services.AddSingleton<IKnowledgeVectorIndex>(_ =>
                new SqliteVecKnowledgeVectorIndex(sqliteConnectionString, knowledgeRetrieval.Vector));
            services.AddSingleton<ILocalStructuredCompletionProvider>(_ =>
                new OllamaStructuredCompletionProvider(new HttpClient(), knowledgeRetrieval.Completion));
            services.AddSingleton<KnowledgeBackgroundQueue>();
            services.AddSingleton<IKnowledgeBackgroundQueue>(serviceProvider =>
                serviceProvider.GetRequiredService<KnowledgeBackgroundQueue>());
            services.AddScoped<IKnowledgeSearchDocumentSource, KnowledgeSearchDocumentSource>();
            services.AddScoped<IKnowledgeLexicalSearchCoordinator, KnowledgeLexicalSearchCoordinator>();
            services.AddScoped<IKnowledgeHybridSearchCoordinator, KnowledgeHybridSearchCoordinator>();
            services.AddScoped<IKnowledgeFactAnswerCoordinator, KnowledgeFactAnswerCoordinator>();
            services.AddScoped<IKnowledgeReadAgentCoordinator, KnowledgeReadAgentCoordinator>();
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
        services.AddScoped<IStoryPlanActionRunner>(serviceProvider =>
            serviceProvider.GetRequiredService<ActionRunner>());
        return services;
    }

    internal static IServiceCollection AddDantesRoleplayAuthenticatedGameAdapters(
        this IServiceCollection services)
    {
        services.AddScoped<IAuthorizedKnowledgeCandidateResolver, AuthorizedKnowledgeCandidateResolver>();
        services.AddScoped<IAuthorizedKnowledgeAnswerCoordinator, AuthorizedKnowledgeAnswerCoordinator>();
        services.AddScoped<IStoryPlanStepProcessor, StoryPlanStepProcessor>();
        return services;
    }
}
