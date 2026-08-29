using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Assistants;
using DantesRoleplay.Procedures;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.SystemConversations;

public sealed class SystemConversationContextMaterializer(
    ISystemCapabilityCatalog capabilities,
    IProcedureStore procedures) : ISystemConversationContextMaterializer
{
    private static readonly JsonSerializerOptions ContextJson = new(JsonSerializerDefaults.Web);
    public const int MaximumContextBytes = 48 * 1024;
    private const int MaximumCapabilities = 16;
    private const int MaximumProcedures = 8;
    private const int MaximumApplications = 25;

    public async Task<SystemConversationContextSnapshot> MaterializeAsync(
        string query,
        SystemConversationRequestContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var invocation = new SystemCapabilityInvocationContext(
            context.Principal, context.Scope, context.CorrelationId);
        var discovery = capabilities.Discover(invocation);
        if (!discovery.Ok)
            throw Error("SYSTEM_CHAT_CONTEXT_UNAVAILABLE",
                "Authorized system capability context is unavailable.");

        var tokens = Tokens(query);
        var rankedCapabilities = discovery.Capabilities
            .Where(descriptor => descriptor.Sensitivity != SystemCapabilitySensitivity.Secret)
            .Select(descriptor => new Ranked<CapabilityView>(
                Capability(descriptor), Score(tokens,
                    descriptor.Id, descriptor.Owner, descriptor.Description,
                    string.Join(' ', descriptor.ProcedureIds))))
            .OrderByDescending(value => value.Score)
            .ThenBy(value => value.Value.Id, StringComparer.Ordinal)
            .ToArray();
        var relevantCapabilities = rankedCapabilities.Where(value => value.Score > 0).ToArray();
        var capabilityViews = (relevantCapabilities.Length > 0
                ? relevantCapabilities
                : rankedCapabilities)
            .Take(MaximumCapabilities)
            .Select(value => value.Value)
            .ToList();

        var summaries = await procedures.FindAsync(
            query, "system", includeInactive: false, limit: 64, cancellationToken);
        var procedureIds = capabilityViews.SelectMany(value => value.ProcedureIds)
            .Concat(summaries.Where(value => value.Status == ProcedureStatus.Active)
                .Select(value => value.Id))
            .Distinct(StringComparer.Ordinal)
            .Take(MaximumProcedures)
            .ToArray();
        var procedureViews = new List<ProcedureView>();
        foreach (var id in procedureIds)
        {
            var detail = await procedures.GetAsync(id, cancellationToken: cancellationToken);
            if (detail is null || detail.Status != ProcedureStatus.Active ||
                !IsSystemCategory(detail.Category)) continue;
            procedureViews.Add(Procedure(detail));
        }

        var applicationsResult = await capabilities.ReadAsync(
            SystemCapabilityIds.Applications,
            JsonSerializer.Serialize(new { limit = MaximumApplications }),
            invocation,
            cancellationToken);
        if (!applicationsResult.Ok || applicationsResult.Data is null)
            throw Error("SYSTEM_CHAT_CONTEXT_UNAVAILABLE",
                "Registered application metadata is unavailable.");
        var applicationViews = applicationsResult.Data.Value.GetProperty("applications")
            .EnumerateArray().Take(MaximumApplications).Select(Application).ToList();

        while (true)
        {
            var (json, references) = Serialize(capabilityViews, procedureViews, applicationViews);
            if (Encoding.UTF8.GetByteCount(json) <= MaximumContextBytes)
            {
                var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
                return new(AssistantTurnContextProfiles.SystemReadV1, json, fingerprint, references);
            }
            if (applicationViews.Count > 0) applicationViews.RemoveAt(applicationViews.Count - 1);
            else if (procedureViews.Count > 0) procedureViews.RemoveAt(procedureViews.Count - 1);
            else if (capabilityViews.Count > 1) capabilityViews.RemoveAt(capabilityViews.Count - 1);
            else throw Error("SYSTEM_CHAT_CONTEXT_TOO_LARGE",
                "The authorized system context exceeds the safe local-model limit.");
        }
    }

    private static (string Json, IReadOnlyList<string> References) Serialize(
        IReadOnlyList<CapabilityView> capabilityViews,
        IReadOnlyList<ProcedureView> procedureViews,
        IReadOnlyList<ApplicationView> applicationViews)
    {
        var references = capabilityViews.Select(value => value.Reference)
            .Concat(procedureViews.Select(value => value.Reference))
            .Concat(applicationViews.Select(value => value.Reference))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var json = JsonSerializer.Serialize(new
        {
            profile = AssistantTurnContextProfiles.SystemReadV1,
            applications = applicationViews,
            evidenceReferences = references,
            capabilities = capabilityViews,
            procedures = procedureViews,
            limitations = new[]
            {
                "No application ECS values or private application catalog content.",
                "No source files, filesystem content, secrets, settings values, or unrestricted history.",
                "Read-only: no proposals, confirmations, actions, or system changes."
            }
        }, ContextJson);
        return (json, Array.AsReadOnly(references));
    }

    private static CapabilityView Capability(SystemCapabilityDescriptor value)
    {
        var reference = $"capability:{value.Id}@{value.Version}#{value.Fingerprint}";
        return new(
            reference, value.Id, value.Version, value.Fingerprint, value.Owner, value.Description,
            value.ModeName, value.InputSchemaProfile, value.InputSchemaJson, value.InputSchemaHash,
            value.OutputSchemaProfile, value.OutputSchemaJson, value.OutputSchemaHash,
            value.ProcedureIds.ToArray(), value.RequiredCapabilityName, value.SensitivityName,
            value.RequiresConfirmation, value.RequiresIdempotencyKey);
    }

    private static ProcedureView Procedure(ProcedureDetail value)
    {
        var reference = $"procedure:{value.Id}@{value.Version}#{value.SourceHash}";
        return new(reference, value.Id, value.Version, value.SourceHash, value.Category, value.Name,
            value.Description, value.Governs, value.Instructions, value.Constraints);
    }

    private static ApplicationView Application(JsonElement value)
    {
        var id = value.GetProperty("id").GetString()!;
        var revision = value.GetProperty("revision").GetInt32();
        var fingerprint = value.GetProperty("fingerprint").GetString()!;
        return new(
            $"application:{id}@{revision}#{fingerprint}",
            id,
            value.GetProperty("displayName").GetString()!,
            value.GetProperty("description").GetString()!,
            revision,
            fingerprint,
            value.GetProperty("baseApplications").EnumerateArray()
                .Select(item => item.GetString()!).ToArray());
    }

    private static int Score(IReadOnlyList<string> tokens, params string[] values)
    {
        if (tokens.Count == 0) return 0;
        var text = string.Join(' ', values);
        return tokens.Count(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> Tokens(string query) =>
        (query ?? string.Empty)
            .Split([' ', ',', ';', ':', '/', '\\', '(', ')', '"', '\'', '.', '?', '!'],
                StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length >= 3 && !QueryStopWords.Contains(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(64)
            .ToArray();

    private static readonly IReadOnlySet<string> QueryStopWords = new HashSet<string>(
        ["and", "answer", "are", "context", "from", "how", "only", "supplied", "system", "the", "what", "which", "with"],
        StringComparer.OrdinalIgnoreCase);

    private static bool IsSystemCategory(string value) =>
        value == "system" || value.StartsWith("system.", StringComparison.Ordinal);

    private static SystemConversationException Error(string code, string message) => new(code, message);

    private sealed record Ranked<T>(T Value, int Score);
    private sealed record CapabilityView(
        string Reference, string Id, int Version, string Fingerprint, string Owner, string Description,
        string Mode, string InputSchemaProfile, string InputSchema, string InputSchemaHash,
        string OutputSchemaProfile, string OutputSchema, string OutputSchemaHash,
        IReadOnlyList<string> ProcedureIds, string RequiredAuthorization, string Sensitivity,
        bool RequiresConfirmation, bool RequiresIdempotencyKey);
    private sealed record ProcedureView(
        string Reference, string Id, int Version, string SourceHash, string Category, string Name,
        string Description, string Governs, string Instructions, string Constraints);
    private sealed record ApplicationView(
        string Reference, string Id, string DisplayName, string Description, int Revision,
        string Fingerprint, IReadOnlyList<string> BaseApplications);
}
