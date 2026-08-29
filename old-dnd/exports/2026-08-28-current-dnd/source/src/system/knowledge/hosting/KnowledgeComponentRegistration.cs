using DantesRoleplay.Retrieval;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Knowledge;

/// <summary>
/// Opt-in registration only. A host must first register explicit audience, application-binding,
/// and actor-participation owners; the generic server deliberately does not call this in Slice 7D0.
/// </summary>
public static class KnowledgeComponentRegistration
{
    public static IServiceCollection AddAuthorizedKnowledgeCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IKnowledgeCanonicalSource, ApplicationKnowledgeCanonicalSource>();
        services.AddScoped<IKnowledgeEffectiveStateResolver, ApplicationKnowledgeEffectiveStateResolver>();
        services.AddSingleton<IKnowledgeLexicalRetriever, DeterministicKnowledgeLexicalRetriever>();
        services.AddScoped<IAuthorizedKnowledgeCandidateResolver, AuthorizedKnowledgeCandidateResolver>();
        services.AddScoped<IAuthorizedKnowledgeNotebookReader, AuthorizedKnowledgeNotebookReader>();
        services.AddScoped<IReviewedKnowledgeStateSynchronizer, ReviewedKnowledgeStateSynchronizer>();
        services.AddScoped<IAuthorizedKnowledgeCoordinator>(provider =>
        {
            var completion = provider.GetService<ILocalStructuredCompletionProvider>();
            return completion is null
                ? new UnavailableAuthorizedKnowledgeCoordinator()
                : new AuthorizedKnowledgeCoordinator(
                    provider.GetRequiredService<IAuthorizedKnowledgeCandidateResolver>(), completion);
        });
        return services;
    }

    private sealed class UnavailableAuthorizedKnowledgeCoordinator : IAuthorizedKnowledgeCoordinator
    {
        public Task<AuthorizedKnowledgeResult> AnswerAsync(
            AuthorizedKnowledgeRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthorizedKnowledgeResult.Unknown(
                "KNOWLEDGE_MODEL_UNAVAILABLE", "The optional local answer model is not configured."));
    }
}
