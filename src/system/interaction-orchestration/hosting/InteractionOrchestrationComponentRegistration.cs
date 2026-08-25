using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Interactions;
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
        services.TryAddScoped<IInteractionRecipeStore, InteractionRecipeStore>();
        services.TryAddScoped<IInteractionRecipeLearner, InteractionRecipeLearner>();
        services.TryAddScoped<IInteractionRecipeProvenanceReader, InteractionRecipeProvenanceReader>();
        services.TryAddScoped<IInteractionRecipeReviewService, InteractionRecipeReviewService>();
        services.TryAddScoped<IInteractionFeatureRetriever>(provider => new InteractionFeatureRetriever(
            provider.GetRequiredService<IActiveCatalogFeatureSnapshotProvider>(),
            provider.GetService<ITextEmbeddingProvider>(),
            provider.GetService<IInteractionDerivedVectorIndex>()));
        services.TryAddScoped<IVerifiedInteractionRecipeResolver>(provider => new VerifiedInteractionRecipeResolver(
            provider.GetRequiredService<IInteractionRecipeStore>(),
            provider.GetRequiredService<IActiveCatalogFeatureSnapshotProvider>(),
            provider.GetRequiredService<IInteractionProposalVerifier>(),
            provider.GetService<ITextEmbeddingProvider>(),
            provider.GetService<IInteractionDerivedVectorIndex>()));
        services.TryAddScoped<IInteractionProposalVerifier, InteractionProposalVerifier>();
        services.TryAddScoped<IInteractionEnvelopeFactory, InteractionEnvelopeFactory>();
        services.TryAddScoped<IInteractionExecutionCoordinator, InteractionExecutionCoordinator>();
        services.TryAddScoped<IInteractionGateway, InteractionGateway>();
        services.TryAddSingleton<UnavailableInteractionOuterProvider>();
        services.TryAddSingleton<IInteractionOuterTurnProvider>(provider =>
            provider.GetRequiredService<UnavailableInteractionOuterProvider>());
        services.TryAddSingleton<IInteractionNarrationProvider>(provider =>
            provider.GetRequiredService<UnavailableInteractionOuterProvider>());
        services.TryAddScoped<IInteractionPlanner>(provider => new InteractionPlanner(
            provider.GetRequiredService<IInteractionAuthorizationPolicy>(),
            provider.GetRequiredService<IInteractionFeatureRetriever>(),
            provider.GetRequiredService<IActiveCatalogFeatureSnapshotProvider>(),
            provider.GetRequiredService<IInteractionProposalVerifier>(),
            provider.GetRequiredService<IVerifiedInteractionRecipeResolver>(),
            provider.GetRequiredService<IInteractionReceiptStore>(),
            [
                new LocalInteractionPlanningProvider(provider.GetService<ILocalStructuredCompletionProvider>()),
                (IInteractionPlanningCompletionProvider?)provider.GetService<OpenAiResponsesInteractionPlanningProvider>()
                    ?? new UnavailableRemoteInteractionPlanningProvider()
            ]));
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
