using DantesRoleplay.CodexBridge;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess.Composition;

public static class CodexBridgeComponentExtensions
{
    public static IServiceCollection AddCodexBridgeComponent(
        this IServiceCollection services, CodexBridgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ExecutablePath) ||
            string.IsNullOrWhiteSpace(options.RepositoryRoot) || !Path.IsPathFullyQualified(options.RepositoryRoot) ||
            !Directory.Exists(options.RepositoryRoot) || options.MaximumConcurrentTurns is < 1 or > 4 ||
            options.MaximumLineBytes is < 16_384 or > 1_048_576 ||
            options.EffectiveInitializationTimeout < TimeSpan.FromSeconds(1) ||
            options.EffectiveInitializationTimeout > TimeSpan.FromMinutes(1) ||
            options.EffectiveTurnTimeout < TimeSpan.FromSeconds(10) ||
            options.EffectiveTurnTimeout > TimeSpan.FromMinutes(30) ||
            options.EffectiveApprovalTimeout < TimeSpan.FromSeconds(15) ||
            options.EffectiveApprovalTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentException("The Codex bridge options are invalid.", nameof(options));

        services.AddSingleton(options);
        services.AddSingleton<ICodexAppServerFactory, CodexAppServerProcessFactory>();
        services.AddSingleton<CodexTurnRegistry>();
        services.AddScoped<ICodexConversationService, CodexConversationService>();
        return services;
    }
}
