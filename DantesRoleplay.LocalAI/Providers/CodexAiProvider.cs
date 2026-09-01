namespace DantesRoleplay.AI.Codex;

/// <summary>
/// Provider-neutral seam implemented by the host's Codex app-server adapter. Keeping process and
/// repository concerns behind this interface lets the AI project offer Codex without depending on
/// persistence, ASP.NET, or the MCP server.
/// </summary>
public interface ICodexAiClient
{
    Task<IReadOnlyList<AiModel>> ListModelsAsync(
        CancellationToken cancellationToken = default);

    Task<AiProviderResponse> SendAsync(
        AiProviderRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class CodexAiProvider(ICodexAiClient client) : IAiProvider
{
    public AiProviderInfo Info { get; } = new("codex", "Codex");

    public Task<IReadOnlyList<AiModel>> ListModelsAsync(
        CancellationToken cancellationToken = default) =>
        client.ListModelsAsync(cancellationToken);

    public Task<AiProviderResponse> SendAsync(
        AiProviderRequest request,
        CancellationToken cancellationToken = default) =>
        client.SendAsync(request, cancellationToken);
}
