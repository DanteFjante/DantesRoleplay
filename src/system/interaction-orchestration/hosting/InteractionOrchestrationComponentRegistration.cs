using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.Interactions;
using DantesRoleplay.Knowledge;
using DantesRoleplay.Play;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DantesRoleplay.DataAccess.Composition;

internal static class InteractionOrchestrationComponentRegistration
{
    /// <summary>
    /// Registers lexical retrieval by default. A host may separately register an embedding provider
    /// and derived-vector index after configuring a safe disposable index location.
    /// </summary>
    internal static IServiceCollection AddInteractionOrchestrationComponent(this IServiceCollection services)
    {
        services.TryAddSingleton<IInteractionAuthorizationPolicy, UnconfiguredInteractionAuthorizationPolicy>();
        services.TryAddScoped<InteractionReceiptStore>();
        services.TryAddScoped<IInteractionReceiptStore>(provider =>
            provider.GetRequiredService<InteractionReceiptStore>());
        services.TryAddScoped<IInteractionExecutionAuthorityStore>(provider =>
            provider.GetRequiredService<InteractionReceiptStore>());
        services.TryAddScoped<IInteractionRecentReceiptReader>(provider =>
            provider.GetRequiredService<InteractionReceiptStore>());
        services.TryAddScoped<IInteractionRecipeStore, InteractionRecipeStore>();
        services.TryAddScoped<IInteractionMechanicOpportunityStore, InteractionMechanicOpportunityStore>();
        services.TryAddScoped<IInteractionMechanicOpportunityLearner, InteractionMechanicOpportunityLearner>();
        services.TryAddScoped<IInteractionMechanicSandboxService, InteractionMechanicSandboxService>();
        services.AddScoped<ISystemReadCapabilityHandler, InteractionMechanicSandboxReadCapabilityHandler>();
        foreach (var id in new[]
        {
            SystemCapabilityIds.InteractionContextPack,
            SystemCapabilityIds.InteractionRecipes,
            SystemCapabilityIds.MechanicOpportunities
        })
        {
            var capabilityId = id;
            services.AddScoped<ISystemReadCapabilityHandler>(provider =>
                new InteractionGovernanceReadCapabilityHandler(
                    capabilityId,
                    provider.GetRequiredService<IInteractionRecipeStore>(),
                    provider.GetRequiredService<IInteractionMechanicOpportunityStore>(),
                    provider.GetRequiredService<IInteractionEnvelopeFactory>(),
                    provider.GetRequiredService<IInteractionTaskContextMaterializer>()));
        }
        services.AddScoped<ISystemWriteCapabilityHandler>(provider =>
            new InteractionMechanicSandboxWriteCapabilityHandler(
                SystemCapabilityIds.MechanicSandboxDraft,
                provider.GetRequiredService<IInteractionMechanicSandboxService>(),
                provider.GetRequiredService<IInteractionMechanicOpportunityStore>()));
        services.AddScoped<ISystemWriteCapabilityHandler>(provider =>
            new InteractionMechanicSandboxWriteCapabilityHandler(
                SystemCapabilityIds.MechanicSandboxPromote,
                provider.GetRequiredService<IInteractionMechanicSandboxService>(),
                provider.GetRequiredService<IInteractionMechanicOpportunityStore>()));
        services.AddScoped<ISystemWriteCapabilityHandler>(provider =>
            new InteractionRecipeReviewCapabilityHandler(
                provider.GetRequiredService<IInteractionRecipeStore>(),
                provider.GetRequiredService<IInteractionRecipeReviewService>()));
        services.TryAddScoped<ISystemAiToolSource, InteractionRecipeAiToolSource>();
        services.TryAddScoped<IInteractionRecipeAutoVerificationEvidenceReader, InteractionRecipeAutoVerificationEvidenceReader>();
        services.TryAddScoped<IInteractionRecipeAutoVerifier, InteractionRecipeAutoVerifier>();
        services.TryAddScoped<IInteractionRecipeLearner, InteractionRecipeLearner>();
        services.TryAddScoped<IInteractionRecipeProvenanceReader, InteractionRecipeProvenanceReader>();
        services.TryAddScoped<IInteractionRecipeReviewService, InteractionRecipeReviewService>();
        services.TryAddScoped<IInteractionFeatureRetriever>(provider => new InteractionFeatureRetriever(
            provider.GetRequiredService<IActiveCatalogFeatureSnapshotProvider>(),
            provider.GetService<ITextEmbeddingProvider>(),
            provider.GetService<IInteractionDerivedVectorIndex>(),
            provider.GetService<ICatalogNamespaceRegistry>()));
        services.TryAddScoped<IVerifiedInteractionRecipeResolver>(provider => new VerifiedInteractionRecipeResolver(
            provider.GetRequiredService<IInteractionRecipeStore>(),
            provider.GetRequiredService<IActiveCatalogFeatureSnapshotProvider>(),
            provider.GetRequiredService<IInteractionProposalVerifier>(),
            provider.GetService<ITextEmbeddingProvider>(),
            provider.GetService<IInteractionDerivedVectorIndex>()));
        services.TryAddScoped<IInteractionProposalVerifier, InteractionProposalVerifier>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IInteractionQueryExecutor, ProjectionInteractionQueryExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IInteractionQueryExecutor, MechanicProjectionInteractionQueryExecutor>());
        services.TryAddScoped<IInteractionQueryExecutorRegistry, InteractionQueryExecutorRegistry>();
        services.TryAddScoped<IApplicationReadModelService, ApplicationReadModelService>();
        services.TryAddScoped<IInteractionTaskContextMaterializer>(provider =>
            new InteractionTaskContextMaterializer(
                provider.GetRequiredService<IInteractionAuthorizationPolicy>(),
                provider.GetRequiredService<IInteractionFeatureRetriever>(),
                provider.GetRequiredService<IActiveCatalogFeatureSnapshotProvider>(),
                provider.GetRequiredService<IApplicationReadModelService>(),
                provider.GetService<IAuthorizedKnowledgeCandidateResolver>(),
                provider.GetService<IApplicationPlayRecordStore>(),
                provider.GetService<IInteractionRecentReceiptReader>()));
        services.TryAddScoped<IInteractionEnvelopeFactory, InteractionEnvelopeFactory>();
        services.TryAddScoped<IInteractionExecutionCoordinator, InteractionExecutionCoordinator>();
        services.TryAddScoped<IInteractionGateway, InteractionGateway>();
        services.TryAddSingleton<UnavailableInteractionOuterProvider>();
        services.TryAddSingleton<IInteractionOuterTurnProvider>(provider =>
            provider.GetRequiredService<UnavailableInteractionOuterProvider>());
        services.TryAddSingleton<IInteractionNarrationProvider>(provider =>
            provider.GetRequiredService<UnavailableInteractionOuterProvider>());
        services.TryAddSingleton<IInteractionTaskAgendaProvider>(provider =>
            provider.GetRequiredService<UnavailableInteractionOuterProvider>());
        services.TryAddScoped<IInteractionPlanner>(provider => new InteractionPlanner(
            provider.GetRequiredService<IInteractionAuthorizationPolicy>(),
            provider.GetRequiredService<IInteractionFeatureRetriever>(),
            provider.GetRequiredService<IActiveCatalogFeatureSnapshotProvider>(),
            provider.GetRequiredService<IInteractionProposalVerifier>(),
            provider.GetRequiredService<IVerifiedInteractionRecipeResolver>(),
            provider.GetRequiredService<IInteractionReceiptStore>(),
            [
                new LocalInteractionPlanningProvider(
                    provider.GetService<ILocalStructuredCompletionProvider>(),
                    provider.GetService<IInteractionOuterLocalCompletionProvider>()),
                (IInteractionPlanningCompletionProvider?)provider.GetService<OpenAiResponsesInteractionPlanningProvider>()
                    ?? new UnavailableRemoteInteractionPlanningProvider()
            ],
            provider.GetRequiredService<IInteractionTaskContextMaterializer>()));
        return services;
    }

    private sealed class UnconfiguredInteractionAuthorizationPolicy : IInteractionAuthorizationPolicy
    {
        public InteractionAuthorizationDecision Evaluate(InteractionAuthorizationRequest request) =>
            InteractionAuthorizationDecision.Deny(
                request,
                "INTERACTION_AUTHORIZATION_NOT_CONFIGURED",
                "interaction.authorization.unconfigured");
    }
}
