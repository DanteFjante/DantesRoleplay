using System.Text;
using System.Text.Json;

namespace DantesRoleplay.Interactions;

public static class InteractionTaskAgendaLimits
{
    public const int Tasks = 8;
    public const int BatchesPerTask = 4;
    public const int TotalBatches = 16;
    public const int DependenciesPerTask = 4;
    public const int IntentBytes = 2_000;
    public const int JsonBytes = 32 * 1024;
    public const int JsonDepth = 8;
    public const string FingerprintDomain = "dantes-roleplay/interaction-task-agenda/v1";
}

public sealed record InteractionTaskBatch(int Ordinal, string IntentText);

public sealed record InteractionTaskItem(
    int Ordinal,
    string IntentText,
    IReadOnlyList<int> DependsOn,
    IReadOnlyList<InteractionTaskBatch> Batches);

public sealed record InteractionTaskAgenda(
    IReadOnlyList<InteractionTaskItem> Tasks,
    string Fingerprint)
{
    public static InteractionTaskAgenda Single(string intentText) => Parse(JsonSerializer.Serialize(new
    {
        tasks = new[]
        {
            new
            {
                intentText,
                dependsOn = Array.Empty<int>(),
                batches = new[] { new { intentText } }
            }
        }
    }));

    public static InteractionTaskAgenda Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Encoding.UTF8.GetByteCount(json) > InteractionTaskAgendaLimits.JsonBytes)
            throw Invalid("Task agenda JSON exceeds its closed byte limit.");

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = InteractionTaskAgendaLimits.JsonDepth,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
            var root = document.RootElement;
            Exact(root, ["tasks"]);
            var tasksElement = root.GetProperty("tasks");
            if (tasksElement.ValueKind != JsonValueKind.Array)
                throw Invalid("Task agenda tasks must be an array.");
            var taskElements = tasksElement.EnumerateArray().ToArray();
            if (taskElements.Length is < 1 or > InteractionTaskAgendaLimits.Tasks)
                throw Invalid("Task agenda task count is outside the closed range.");

            var tasks = new List<InteractionTaskItem>(taskElements.Length);
            var totalBatches = 0;
            for (var taskIndex = 0; taskIndex < taskElements.Length; taskIndex++)
            {
                var task = taskElements[taskIndex];
                Exact(task, ["intentText", "dependsOn", "batches"]);
                var ordinal = taskIndex + 1;
                var intent = Intent(task.GetProperty("intentText"));

                var dependenciesElement = task.GetProperty("dependsOn");
                if (dependenciesElement.ValueKind != JsonValueKind.Array)
                    throw Invalid("Task dependencies must be an array.");
                var dependencies = dependenciesElement.EnumerateArray().Select(value =>
                {
                    if (!value.TryGetInt32(out var dependency) || dependency < 1 || dependency >= ordinal)
                        throw Invalid("Task dependencies must name earlier one-based task ordinals.");
                    return dependency;
                }).ToArray();
                if (dependencies.Length > InteractionTaskAgendaLimits.DependenciesPerTask
                    || dependencies.Distinct().Count() != dependencies.Length)
                    throw Invalid("Task dependencies exceed their limit or contain duplicates.");

                var batchesElement = task.GetProperty("batches");
                if (batchesElement.ValueKind != JsonValueKind.Array)
                    throw Invalid("Task batches must be an array.");
                var batchElements = batchesElement.EnumerateArray().ToArray();
                if (batchElements.Length is < 1 or > InteractionTaskAgendaLimits.BatchesPerTask)
                    throw Invalid("Task batch count is outside the closed range.");
                totalBatches += batchElements.Length;
                if (totalBatches > InteractionTaskAgendaLimits.TotalBatches)
                    throw Invalid("Task agenda exceeds its total batch limit.");
                var batches = batchElements.Select((batch, batchIndex) =>
                {
                    Exact(batch, ["intentText"]);
                    return new InteractionTaskBatch(batchIndex + 1, Intent(batch.GetProperty("intentText")));
                }).ToArray();
                tasks.Add(new(ordinal, intent, Array.AsReadOnly(dependencies), Array.AsReadOnly(batches)));
            }

            var canonical = InteractionCanonicalJson.CanonicalizeObject(json);
            return new(Array.AsReadOnly(tasks.ToArray()),
                InteractionCanonicalJson.Fingerprint(InteractionTaskAgendaLimits.FingerprintDomain, canonical));
        }
        catch (InteractionContractException) { throw; }
        catch (JsonException)
        {
            throw Invalid("Task agenda JSON is malformed or incomplete.");
        }
    }

    private static string Intent(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) throw Invalid("Task and batch intents must be strings.");
        var text = value.GetString()!;
        if (string.IsNullOrWhiteSpace(text) || text != text.Trim() || text.Any(char.IsControl)
            || Encoding.UTF8.GetByteCount(text) > InteractionTaskAgendaLimits.IntentBytes)
            throw Invalid("Task and batch intents must be bounded trimmed plain text.");
        return text;
    }

    private static void Exact(JsonElement value, IReadOnlyList<string> properties)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid("Task agenda values must be objects.");
        var actual = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != properties.Count || actual.Distinct(StringComparer.Ordinal).Count() != actual.Length
            || actual.Any(name => !properties.Contains(name, StringComparer.Ordinal)))
            throw Invalid("Task agenda objects contain missing, duplicate, or unknown properties.");
    }

    private static InteractionContractException Invalid(string message) =>
        new("TASK_AGENDA_INVALID", message);
}

public sealed record InteractionTaskAgendaRequest(string GoalText);

public sealed record InteractionTaskAgendaResult(
    bool Available,
    InteractionTaskAgenda? Agenda,
    string Code)
{
    public static InteractionTaskAgendaResult Unavailable(string code) => new(false, null, code);
}

public interface IInteractionTaskAgendaProvider
{
    Task<InteractionTaskAgendaResult> CreateAgendaAsync(
        InteractionTaskAgendaRequest request,
        CancellationToken cancellationToken = default);
}

public enum InteractionTaskAgendaStatus
{
    Planning,
    AwaitingConfirmation,
    NeedsAttention,
    Completed,
    Cancelled
}

public enum InteractionTaskStatus
{
    Pending,
    Active,
    Completed,
    Blocked,
    Cancelled
}

public enum InteractionTaskBatchStatus
{
    Pending,
    Planning,
    AwaitingConfirmation,
    Completed,
    Unresolved,
    Failed,
    Cancelled
}

public sealed record InteractionTaskBatchProgressProjection(
    int Ordinal,
    string Id,
    string IntentText,
    string Status,
    string? Code,
    string? ResolutionReceiptId,
    string? ExecutionReceiptId);

public sealed record InteractionTaskProgressProjection(
    int Ordinal,
    string Id,
    string IntentText,
    IReadOnlyList<int> DependsOn,
    string Status,
    IReadOnlyList<InteractionTaskBatchProgressProjection> Batches);

public sealed record InteractionTaskAgendaProgressProjection(
    string Id,
    string Fingerprint,
    string Status,
    int? CurrentTask,
    int? CurrentBatch,
    IReadOnlyList<InteractionTaskProgressProjection> Tasks);
