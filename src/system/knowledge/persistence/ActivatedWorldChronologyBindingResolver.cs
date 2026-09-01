using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;

namespace DantesRoleplay.Knowledge;

/// <summary>
/// Resolves chronology vocabulary only when its exact metadata document belongs to the current
/// activated application. Keeping this vocabulary separate lets existing audience bindings remain
/// valid until an explicit application synchronization boundary activates the new capability.
/// </summary>
public sealed class ActivatedWorldChronologyBindingResolver(
    WorldChronologyApplicationSelection selection,
    IActivatedApplicationDocumentReader documents) : IWorldChronologyBindingResolver
{
    public Task<WorldChronologyBinding?> ResolveAsync(
        KnowledgeApplicationBinding binding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            selection.Validate();
            binding.Validate();
            if (selection.ApplicationId != binding.ApplicationId) return Task.FromResult<WorldChronologyBinding?>(null);

            var applicationId = ApplicationIdentifier.Parse(selection.ApplicationId);
            var document = documents.ReadText(applicationId, selection.BindingDocumentPath);
            if (document is null || document.ApplicationId != applicationId ||
                !WorldChronologyBindingDocument.TryParse(
                    document.Text, applicationId.Value, out var chronology))
                return Task.FromResult<WorldChronologyBinding?>(null);

            var legacyPrefix = applicationId.Value + ".";
            if (binding.CampaignRootComponentTypeId.StartsWith(legacyPrefix, StringComparison.Ordinal))
            {
                string Prefix(string value) => value.StartsWith(legacyPrefix, StringComparison.Ordinal)
                    ? value
                    : legacyPrefix + value;
                chronology = chronology with
                {
                    ComponentTypeId = Prefix(chronology.ComponentTypeId),
                    InWorldRelationshipKind = Prefix(chronology.InWorldRelationshipKind),
                    AboutRelationshipKind = Prefix(chronology.AboutRelationshipKind),
                    SubjectWorldRelationshipKinds = chronology.SubjectWorldRelationshipKinds
                        .Select(Prefix).ToArray()
                };
            }

            return Task.FromResult<WorldChronologyBinding?>(chronology);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Task.FromResult<WorldChronologyBinding?>(null);
        }
    }
}

internal static class WorldChronologyBindingDocument
{
    private const string CurrentFormat = "system.world-chronology.binding.v1";
    private const int MaximumDocumentLength = 32 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static bool TryParse(
        string text,
        string expectedApplicationId,
        out WorldChronologyBinding binding)
    {
        binding = null!;
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumDocumentLength) return false;
        try
        {
            var document = JsonSerializer.Deserialize<DocumentDto>(text, Json);
            if (document is null || document.Format != CurrentFormat ||
                document.ApplicationId != expectedApplicationId || document.Binding is null)
                return false;
            document.Binding.Validate();
            binding = document.Binding;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private sealed record DocumentDto
    {
        public string Format { get; init; } = "";
        public string ApplicationId { get; init; } = "";
        public WorldChronologyBinding? Binding { get; init; }
    }
}
