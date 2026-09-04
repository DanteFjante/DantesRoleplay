using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Applications;

namespace DantesRoleplay.Interactions;

public static class InteractionRecipeProtocol
{
    public const string IdFingerprintDomain = "dantes-roleplay/interaction-recipe-id/v1";
    public const string TemplateFingerprintDomain = "dantes-roleplay/interaction-recipe-template/v1";
    public const int MaximumStoredIntentText = 500;
    public const string AutoVerifierPrincipal = "system.interaction.recipe-auto-verifier";
    public const string AutoVerificationReason = "Verified by the deterministic outer-fallback policy.";
}

public enum InteractionRecipeStatus
{
    Candidate,
    Verified,
    Stale,
    Retired
}

public static class InteractionRecipeStatusNames
{
    public static string Get(InteractionRecipeStatus value) => value switch
    {
        InteractionRecipeStatus.Candidate => "candidate",
        InteractionRecipeStatus.Verified => "verified",
        InteractionRecipeStatus.Stale => "stale",
        InteractionRecipeStatus.Retired => "retired",
        _ => throw new InteractionContractException("INVALID_RECIPE_STATUS", "The recipe status is not supported.")
    };

    public static InteractionRecipeStatus Parse(string value) => value switch
    {
        "candidate" => InteractionRecipeStatus.Candidate,
        "verified" => InteractionRecipeStatus.Verified,
        "stale" => InteractionRecipeStatus.Stale,
        "retired" => InteractionRecipeStatus.Retired,
        _ => throw new InteractionContractException("INVALID_RECIPE_STATUS", "The recipe status is not supported.")
    };
}

public sealed record InteractionRecipeReference
{
    public InteractionRecipeReference(string id, int version, string templateFingerprint)
    {
        Id = InteractionRecipeIds.Require(id);
        if (version < 1)
            throw new InteractionContractException("INVALID_RECIPE_VERSION", "The recipe version must be positive.");
        Version = version;
        TemplateFingerprint = InteractionGuard.UpperSha256(templateFingerprint, nameof(templateFingerprint));
    }

    public string Id { get; }
    public int Version { get; }
    public string TemplateFingerprint { get; }
}

public static class InteractionRecipeIds
{
    public static string Create(ApplicationIdentifier applicationId, string templateFingerprint)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        templateFingerprint = InteractionGuard.UpperSha256(templateFingerprint, nameof(templateFingerprint));
        var canonical = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            applicationId = applicationId.Value,
            templateFingerprint
        }));
        var hash = InteractionCanonicalJson.Fingerprint(InteractionRecipeProtocol.IdFingerprintDomain, canonical);
        return $"{applicationId.Value}.recipe.{hash[..32].ToLowerInvariant()}";
    }

    public static string Require(string value)
    {
        value = InteractionGuard.Identifier(value, nameof(value));
        var separator = value.LastIndexOf(".recipe.", StringComparison.Ordinal);
        if (separator < 1 || value.Length != separator + 8 + 32)
            throw new InteractionContractException("INVALID_RECIPE_ID", "The recipe ID is invalid.");
        var suffix = value[(separator + 8)..];
        if (suffix.Any(character => !(char.IsAsciiDigit(character) || character is >= 'a' and <= 'f')))
            throw new InteractionContractException("INVALID_RECIPE_ID", "The recipe ID is invalid.");
        var application = ApplicationIdentifier.Parse(value[..separator]);
        if (application.Value == "system")
            throw new InteractionContractException("INVALID_RECIPE_ID", "A recipe must belong to an application.");
        return value;
    }
}

public sealed record InteractionRecipeTemplateStep
{
    public InteractionRecipeTemplateStep(
        string stepId,
        string qualifiedId,
        int contractVersion,
        string contractFingerprint,
        IEnumerable<string> dependsOn,
        IEnumerable<string> roleSlots,
        InteractionPlanStepKind kind = InteractionPlanStepKind.Action,
        IEnumerable<InteractionRecipeInputBinding>? inputBindings = null,
        IEnumerable<InteractionResultBinding>? resultBindings = null)
    {
        StepId = InteractionGuard.Identifier(stepId, nameof(stepId));
        QualifiedId = InteractionGuard.Identifier(qualifiedId, nameof(qualifiedId));
        if (!SafeToken(StepId, allowDots: true) || !QualifiedId.Split('.').All(SafeSegment))
            throw new InteractionContractException("RECIPE_TEMPLATE_UNSAFE", "A recipe contains an unsafe identifier.");
        if (contractVersion < 1)
            throw new InteractionContractException("INVALID_RECIPE_CONTRACT_VERSION", "A recipe contract version must be positive.");
        ContractVersion = contractVersion;
        ContractFingerprint = InteractionGuard.UpperSha256(contractFingerprint, nameof(contractFingerprint));
        if (!Enum.IsDefined(kind))
            throw new InteractionContractException("INVALID_RECIPE_STEP_KIND", "A recipe step kind is not supported.");
        Kind = kind;
        DependsOn = InteractionGuard.CopyDistinctList(dependsOn, InteractionContractLimits.DependenciesPerStep,
            "INVALID_RECIPE_DEPENDENCIES", sort: true);
        RoleSlots = InteractionGuard.CopyDistinctList(roleSlots, InteractionContractLimits.RoleHints,
            "INVALID_RECIPE_ROLE_SLOTS", sort: true);
        if (RoleSlots.Any(value => !SafeToken(value, allowDots: false)))
            throw new InteractionContractException("RECIPE_TEMPLATE_UNSAFE", "A recipe contains an unsafe role-slot name.");
        var copiedInputs = inputBindings?.ToArray() ?? [];
        if (copiedInputs.Length > InteractionContractLimits.ResultBindingsPerStep
            || copiedInputs.Any(value => value is null)
            || copiedInputs.Select(value => value.Parameter).Distinct(StringComparer.Ordinal).Count() != copiedInputs.Length
            || copiedInputs.Select(value => value.ToInputPointer).Distinct(StringComparer.Ordinal).Count() != copiedInputs.Length)
            throw new InteractionContractException("INVALID_RECIPE_INPUT_BINDINGS",
                "Recipe input bindings must be bounded with distinct parameters and targets.");
        InputBindings = Array.AsReadOnly(copiedInputs);
        var copiedResults = resultBindings?.ToArray() ?? [];
        if (copiedResults.Length > InteractionContractLimits.ResultBindingsPerStep
            || copiedResults.Any(value => value is null)
            || copiedResults.Select(value => value.TargetKey).Distinct(StringComparer.Ordinal).Count() != copiedResults.Length)
            throw new InteractionContractException("INVALID_RECIPE_RESULT_BINDINGS",
                "Recipe result bindings must be bounded with distinct targets.");
        ResultBindings = Array.AsReadOnly(copiedResults);
    }

    public string StepId { get; }
    public string QualifiedId { get; }
    public int ContractVersion { get; }
    public string ContractFingerprint { get; }
    public InteractionPlanStepKind Kind { get; }
    public IReadOnlyList<string> DependsOn { get; }
    public IReadOnlyList<string> RoleSlots { get; }
    public IReadOnlyList<InteractionRecipeInputBinding> InputBindings { get; }
    public IReadOnlyList<InteractionResultBinding> ResultBindings { get; }

    private static bool SafeToken(string value, bool allowDots) => value.Length > 0
        && char.IsAsciiLetter(value[0])
        && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '_' or '-' || allowDots && character == '.');

    private static bool SafeSegment(string value) => value.Length > 0
        && char.IsAsciiLetterLower(value[0])
        && value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-');
}

public sealed record InteractionRecipeInputBinding
{
    public InteractionRecipeInputBinding(string parameter, string toInputPointer)
    {
        Parameter = InteractionGuard.Identifier(parameter, nameof(parameter));
        ToInputPointer = new InteractionResultBinding("source", "", toInputPointer: toInputPointer)
            .ToInputPointer!;
        if (ToInputPointer.Length == 0)
            throw new InteractionContractException("INVALID_RECIPE_INPUT_BINDINGS",
                "A recipe input binding must target a property below the input root.");
    }

    public string Parameter { get; }
    public string ToInputPointer { get; }
}

public sealed record InteractionRecipeTemplate
{
    private InteractionRecipeTemplate(
        IReadOnlyList<InteractionRecipeTemplateStep> steps,
        string canonicalJson,
        string fingerprint)
    {
        Steps = steps;
        CanonicalJson = canonicalJson;
        Fingerprint = fingerprint;
    }

    public IReadOnlyList<InteractionRecipeTemplateStep> Steps { get; }
    public string CanonicalJson { get; }
    public string Fingerprint { get; }

    public static InteractionRecipeTemplate FromProposal(ApplicationIdentifier applicationId, InteractionPlannerProposalCommand proposal)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        ArgumentNullException.ThrowIfNull(proposal);
        if (proposal.Steps.Select(step => step.StepId).Distinct(StringComparer.Ordinal).Count() != proposal.Steps.Count)
            throw new InteractionContractException("INVALID_RECIPE_TEMPLATE", "Recipe source step IDs must be unique.");
        var stepIds = proposal.Steps.Select((step, index) => (step.StepId, Normalized: $"step.{index + 1}"))
            .ToDictionary(value => value.StepId, value => value.Normalized, StringComparer.Ordinal);
        var steps = proposal.Steps.Select((step, index) =>
        {
            if (!step.QualifiedId.StartsWith(applicationId.Value + ".", StringComparison.Ordinal))
                throw new InteractionContractException("CROSS_APPLICATION_REFERENCE", "A recipe cannot reference another application.");
            if (step.DependsOn.Any(value => !stepIds.ContainsKey(value)))
                throw new InteractionContractException("INVALID_RECIPE_DEPENDENCIES", "A recipe dependency is unavailable.");
            if ((step.ResultBindings ?? []).Any(value => !stepIds.ContainsKey(value.FromStepId)))
                throw new InteractionContractException("INVALID_RECIPE_DEPENDENCIES",
                    "A recipe result-binding source is unavailable.");
            using var input = JsonDocument.Parse(InteractionCanonicalJson.CanonicalizeObject(step.InputJson));
            var inputBindings = input.RootElement.EnumerateObject()
                .OrderBy(value => value.Name, StringComparer.Ordinal)
                .Select((value, inputIndex) => new InteractionRecipeInputBinding(
                    $"step.{index + 1}.input.{inputIndex + 1}", "/" + EscapePointer(value.Name)))
                .ToArray();
            var resultBindings = (step.ResultBindings ?? []).Select(binding => new InteractionResultBinding(
                stepIds[binding.FromStepId], binding.FromPointer, binding.ToRole, binding.ToInputPointer));
            return new InteractionRecipeTemplateStep($"step.{index + 1}", step.QualifiedId, step.Version,
                step.Fingerprint, step.DependsOn.Select(value => stepIds[value]), step.RoleBindings.Keys,
                step.Kind, inputBindings, resultBindings);
        }).ToArray();
        return Create(steps);
    }

    private static InteractionRecipeTemplate Create(IReadOnlyList<InteractionRecipeTemplateStep> steps)
    {
        ValidateGraph(steps);
        var extended = steps.Any(step => step.Kind != InteractionPlanStepKind.Action
            || step.InputBindings.Count > 0 || step.ResultBindings.Count > 0);
        var canonical = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            steps = extended
                ? steps.Select(ExtendedStep)
                : steps.Select(LegacyStep)
        }));
        var fingerprint = InteractionCanonicalJson.Fingerprint(
            InteractionRecipeProtocol.TemplateFingerprintDomain, canonical);
        return new(Array.AsReadOnly(steps.ToArray()), canonical, fingerprint);
    }

    private static object LegacyStep(InteractionRecipeTemplateStep step) => new
    {
        stepId = step.StepId,
        qualifiedId = step.QualifiedId,
        version = step.ContractVersion,
        fingerprint = step.ContractFingerprint,
        dependsOn = step.DependsOn,
        roleSlots = step.RoleSlots,
        input = new { }
    };

    private static object ExtendedStep(InteractionRecipeTemplateStep step) => new
    {
        stepId = step.StepId,
        kind = step.Kind == InteractionPlanStepKind.Query ? "query" : "action",
        qualifiedId = step.QualifiedId,
        version = step.ContractVersion,
        fingerprint = step.ContractFingerprint,
        dependsOn = step.DependsOn,
        roleSlots = step.RoleSlots,
        input = new { },
        inputBindings = step.InputBindings.Select(binding => new
        {
            parameter = binding.Parameter,
            toInputPointer = binding.ToInputPointer
        }),
        resultBindings = step.ResultBindings.Select(binding => new
        {
            fromStepId = binding.FromStepId,
            fromPointer = binding.FromPointer,
            toRole = binding.ToRole,
            toInputPointer = binding.ToInputPointer
        })
    };

    public static InteractionRecipeTemplate Parse(string json, ApplicationIdentifier applicationId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        var canonical = InteractionCanonicalJson.CanonicalizeObject(json);
        using var document = JsonDocument.Parse(canonical);
        var root = document.RootElement;
        ExactProperties(root, "steps");
        if (!root.TryGetProperty("steps", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new InteractionContractException("INVALID_RECIPE_TEMPLATE", "Recipe steps must be an array.");
        var drafts = new List<InteractionRecipeTemplateStep>();
        foreach (var item in items.EnumerateArray())
        {
            ExactProperties(item, "dependsOn", "fingerprint", "input", "inputBindings", "kind", "qualifiedId",
                "resultBindings", "roleSlots", "stepId", "version");
            if (!item.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object
                || InteractionCanonicalJson.CanonicalizeObject(input.GetRawText()) != "{}")
                throw new InteractionContractException("RECIPE_INPUT_PARAMETERIZATION_UNSUPPORTED", "A recipe cannot retain mechanic input values.");
            var qualifiedId = RequiredString(item, "qualifiedId");
            if (!qualifiedId.StartsWith(applicationId.Value + ".", StringComparison.Ordinal))
                throw new InteractionContractException("CROSS_APPLICATION_REFERENCE",
                    "A recipe cannot reference another application.");
            var kind = item.TryGetProperty("kind", out var kindValue)
                ? kindValue.GetString() switch
                {
                    "action" => InteractionPlanStepKind.Action,
                    "query" => InteractionPlanStepKind.Query,
                    _ => throw new InteractionContractException("INVALID_RECIPE_STEP_KIND", "A recipe step kind is invalid.")
                }
                : InteractionPlanStepKind.Action;
            var inputBindings = OptionalArray(item, "inputBindings").Select(value =>
            {
                ExactProperties(value, "parameter", "toInputPointer");
                return new InteractionRecipeInputBinding(RequiredString(value, "parameter"),
                    RequiredString(value, "toInputPointer"));
            }).ToArray();
            var resultBindings = OptionalArray(item, "resultBindings").Select(value =>
            {
                ExactProperties(value, "fromPointer", "fromStepId", "toInputPointer", "toRole");
                return new InteractionResultBinding(RequiredString(value, "fromStepId"),
                    RequiredString(value, "fromPointer"), OptionalString(value, "toRole"),
                    OptionalString(value, "toInputPointer"));
            }).ToArray();
            drafts.Add(new InteractionRecipeTemplateStep(RequiredString(item, "stepId"),
                qualifiedId, RequiredInteger(item, "version"),
                RequiredString(item, "fingerprint"), RequiredStrings(item, "dependsOn"),
                RequiredStrings(item, "roleSlots"), kind, inputBindings, resultBindings));
        }
        var result = Create(drafts.ToArray());
        if (!string.Equals(canonical, result.CanonicalJson, StringComparison.Ordinal))
            throw new InteractionContractException("INVALID_RECIPE_TEMPLATE", "The recipe template is not canonical.");
        return result;
    }

    private static void ValidateGraph(IReadOnlyList<InteractionRecipeTemplateStep> steps)
    {
        if (steps.Count is < 1 or > InteractionContractLimits.ProposalSteps)
            throw new InteractionContractException("INVALID_RECIPE_TEMPLATE", "The recipe step count is outside the closed limit.");
        if (steps.Select(step => step.StepId).Distinct(StringComparer.Ordinal).Count() != steps.Count)
            throw new InteractionContractException("INVALID_RECIPE_TEMPLATE", "Recipe step IDs must be unique.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var parameters = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in steps)
        {
            if (step.DependsOn.Any(value => !seen.Contains(value)))
                throw new InteractionContractException("INVALID_RECIPE_DEPENDENCIES", "Recipe dependencies must name earlier steps.");
            if (step.ResultBindings.Any(binding => !seen.Contains(binding.FromStepId)
                    || !step.DependsOn.Contains(binding.FromStepId, StringComparer.Ordinal)))
                throw new InteractionContractException("INVALID_RECIPE_RESULT_BINDINGS",
                    "Recipe result bindings must name earlier explicit dependencies.");
            if (step.InputBindings.Any(binding => !parameters.Add(binding.Parameter)))
                throw new InteractionContractException("INVALID_RECIPE_INPUT_BINDINGS",
                    "Recipe input parameters must be unique across the template.");
            seen.Add(step.StepId);
        }
    }

    private static string EscapePointer(string value) => value.Replace("~", "~0", StringComparison.Ordinal)
        .Replace("/", "~1", StringComparison.Ordinal);

    private static void ExactProperties(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InteractionContractException("INVALID_RECIPE_TEMPLATE", "A recipe value must be an object.");
        var allowed = names.ToHashSet(StringComparer.Ordinal);
        if (value.EnumerateObject().Any(property => !allowed.Contains(property.Name)))
            throw new InteractionContractException("INVALID_RECIPE_TEMPLATE", "A recipe template contains an unsupported property.");
    }

    private static string RequiredString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()!
            : throw new InteractionContractException("INVALID_RECIPE_TEMPLATE", $"Recipe property '{name}' must be a string.");

    private static int RequiredInteger(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.TryGetInt32(out var result) && result > 0
            ? result
            : throw new InteractionContractException("INVALID_RECIPE_TEMPLATE", $"Recipe property '{name}' must be positive.");

    private static IReadOnlyList<string> RequiredStrings(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array
            || property.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
            throw new InteractionContractException("INVALID_RECIPE_TEMPLATE", $"Recipe property '{name}' must be a string array.");
        return property.EnumerateArray().Select(item => item.GetString()!).ToArray();
    }

    private static IReadOnlyList<JsonElement> OptionalArray(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property)) return [];
        if (property.ValueKind != JsonValueKind.Array)
            throw new InteractionContractException("INVALID_RECIPE_TEMPLATE", $"Recipe property '{name}' must be an array.");
        return property.EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    private static string? OptionalString(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null) return null;
        if (property.ValueKind != JsonValueKind.String)
            throw new InteractionContractException("INVALID_RECIPE_TEMPLATE", $"Recipe property '{name}' must be a string.");
        return property.GetString();
    }
}

public enum InteractionRecipeLearningDisposition
{
    NotRequested,
    Created,
    Replayed,
    NotCreated,
    Conflict
}

public sealed record InteractionRecipeLearningResult(
    InteractionRecipeLearningDisposition Disposition,
    string Code,
    string SafeSummary,
    InteractionRecipeReference? Recipe = null)
{
    public static InteractionRecipeLearningResult NotRequested() => new(
        InteractionRecipeLearningDisposition.NotRequested, "LEARNING_NOT_REQUESTED", "Route learning was not requested.");
}

public sealed record InteractionRecipeCandidateDraft(
    ApplicationRevision ApplicationRevision,
    string EffectiveSetFingerprint,
    InteractionRecipeTemplate Template,
    string ResolutionReceiptId,
    string ExecutionReceiptId,
    string IntentText,
    string IntentFingerprint,
    string RoleProfile,
    string ResolutionFingerprint = "");

public enum InteractionRecipeWriteDisposition { Created, Replayed, Conflict }

public sealed record InteractionRecipeWriteResult(
    InteractionRecipeWriteDisposition Disposition,
    InteractionRecipeReference? Recipe,
    string Code);

public sealed record InteractionRecipeReviewRequest(
    string RequestToken,
    ApplicationIdentifier ApplicationId,
    string RecipeId,
    int ExpectedVersion,
    string Decision,
    string Reason,
    string ReviewerPrincipalReference);

public sealed record InteractionRecipeProjection(
    InteractionRecipeReference Reference,
    ApplicationIdentifier ApplicationId,
    InteractionRecipeStatus Status,
    InteractionRecipeTemplate Template,
    int EvidenceCount,
    IReadOnlyList<string> EvidenceFingerprints,
    DateTime CreatedAtUtc,
    DateTime RevisedAtUtc,
    int ApplicationRevision = 0,
    string ApplicationFingerprint = "",
    string EffectiveSetFingerprint = "",
    IReadOnlyList<InteractionRecipeEvidenceReference>? Provenance = null,
    string ResolutionFingerprint = "");

public sealed record InteractionRecipeEvidenceReference(
    string ResolutionReceiptId,
    string ExecutionReceiptId,
    string Kind,
    string IntentFingerprint,
    DateTime CreatedAtUtc,
    [property: JsonIgnore] string IntentText = "",
    InteractionRecipeReplayPerformance? ReplayPerformance = null);

public sealed record InteractionRecipeReplayPerformance(
    int BaselineAiCalls,
    int ActualAiCalls,
    int SavedAiCalls,
    int ElapsedMilliseconds,
    int ChoiceResolutionMilliseconds,
    int ProposalMilliseconds,
    int ExecutionMilliseconds,
    int PromptTokens,
    int OutputTokens,
    string FallbackReason = "none");

public sealed record InteractionRecipeProvenanceValidation(bool Valid, string Code, string SafeSummary);

public interface IInteractionRecipeProvenanceReader
{
    Task<InteractionRecipeProvenanceValidation> ValidateAsync(
        InteractionRecipeProjection recipe,
        CancellationToken cancellationToken = default);
}

public interface IInteractionRecipeReviewService
{
    Task<InteractionRecipeWriteResult> ReviewAsync(
        InteractionRecipeReviewRequest request,
        CancellationToken cancellationToken = default);
}

public interface IInteractionRecipeStore
{
    Task<InteractionRecipeWriteResult> AppendCandidateAsync(InteractionRecipeCandidateDraft draft, CancellationToken cancellationToken = default);
    Task<InteractionRecipeProjection?> GetAsync(ApplicationIdentifier applicationId, string recipeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InteractionRecipeProjection>> SearchAsync(ApplicationIdentifier applicationId, string query, InteractionRecipeStatus? status = null, int limit = 20, CancellationToken cancellationToken = default);
    Task<InteractionRecipeWriteResult> ReviewAsync(InteractionRecipeReviewRequest request, CancellationToken cancellationToken = default);
    Task<InteractionRecipeWriteResult> AppendUseEvidenceAsync(InteractionRecipeUseEvidenceDraft draft, CancellationToken cancellationToken = default);
    Task<InteractionRecipeWriteResult> MarkStaleAsync(InteractionRecipeStaleDraft draft, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InteractionRecipeProjection>> ListAsync(ApplicationIdentifier applicationId, InteractionRecipeStatus status, int limit = 50, CancellationToken cancellationToken = default);
    Task<InteractionRecipeWriteResult?> GetReviewReplayAsync(InteractionRecipeReviewRequest request, CancellationToken cancellationToken = default);
    Task<InteractionRecipeSearchPage> SearchPageAsync(ApplicationIdentifier applicationId, string query,
        InteractionRecipeStatus? status, int offset, int limit, CancellationToken cancellationToken = default);
}

public sealed record InteractionRecipeSearchPage(
    IReadOnlyList<InteractionRecipeProjection> Items,
    int Total);

public sealed record InteractionRecipeUseEvidenceDraft(
    InteractionRecipeReference Recipe,
    string ResolutionReceiptId,
    string ExecutionReceiptId,
    bool Successful,
    string IntentFingerprint,
    string RoleProfile,
    string IntentText = "",
    InteractionRecipeReplayPerformance? ReplayPerformance = null);

public sealed record InteractionRecipeStaleDraft(
    InteractionRecipeReference Recipe,
    ApplicationRevision CurrentApplicationRevision,
    string CurrentEffectiveSetFingerprint,
    string Reason,
    string CurrentResolutionFingerprint = "");

public sealed record InteractionRecipeLearningRequest(
    AuthorizedInteractionEnvelope Envelope,
    InteractionPlannerProposalCommand Proposal,
    InteractionReceiptProjection ExecutionReceipt);

public sealed record InteractionRecipeAutoVerificationRequest(
    InteractionRecipeReference Candidate,
    InteractionReceiptProjection ExecutionReceipt);

public sealed record InteractionRecipeAutoVerificationEligibility(
    bool Eligible,
    string Code,
    string SafeSummary);

public interface IInteractionRecipeAutoVerificationEvidenceReader
{
    Task<InteractionRecipeAutoVerificationEligibility> ValidateAsync(
        InteractionRecipeAutoVerificationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IInteractionRecipeAutoVerifier
{
    Task<InteractionRecipeLearningResult> VerifyAsync(
        InteractionRecipeAutoVerificationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IInteractionRecipeLearner
{
    Task<InteractionRecipeLearningResult> LearnAsync(
        InteractionRecipeLearningRequest request,
        CancellationToken cancellationToken = default);

    Task RecordUseAsync(InteractionRecipeUseEvidenceDraft draft, CancellationToken cancellationToken = default);
}

public sealed class UnavailableInteractionRecipeLearner : IInteractionRecipeLearner
{
    public Task<InteractionRecipeLearningResult> LearnAsync(
        InteractionRecipeLearningRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(new InteractionRecipeLearningResult(
            InteractionRecipeLearningDisposition.NotCreated,
            "RECIPE_LEARNING_UNAVAILABLE",
            "The completed interaction was not learned because recipe storage is unavailable."));

    public Task RecordUseAsync(InteractionRecipeUseEvidenceDraft draft, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class InteractionRecipe
{
    public string Id { get; set; } = "";
    public string ApplicationId { get; set; } = "";
    public string TemplateFingerprint { get; set; } = "";
    public string TemplateJson { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public ICollection<InteractionRecipeRevision> Revisions { get; } = new List<InteractionRecipeRevision>();
    public ICollection<InteractionRecipeEvidence> Evidence { get; } = new List<InteractionRecipeEvidence>();
    public InteractionMechanicOpportunity? MechanicOpportunity { get; set; }
}

public sealed class InteractionRecipeRevision
{
    public string RecipeId { get; set; } = "";
    public int Version { get; set; }
    public string Status { get; set; } = "";
    public int ApplicationRevision { get; set; }
    public string ApplicationFingerprint { get; set; } = "";
    public string EffectiveSetFingerprint { get; set; } = "";
    public string ResolutionFingerprint { get; set; } = new('0', 64);
    public string ReviewerPrincipalReference { get; set; } = "";
    public string Reason { get; set; } = "";
    public string RequestToken { get; set; } = "";
    public string RequestFingerprint { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public InteractionRecipe? Recipe { get; set; }
}

public sealed class InteractionRecipeEvidence
{
    public string RecipeId { get; set; } = "";
    public string ExecutionReceiptId { get; set; } = "";
    public string ResolutionReceiptId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string IntentText { get; set; } = "";
    public string IntentFingerprint { get; set; } = "";
    public string RoleProfile { get; set; } = "";
    public int ReplayBaselineAiCalls { get; set; }
    public int ReplayActualAiCalls { get; set; }
    public int ReplaySavedAiCalls { get; set; }
    public int ReplayElapsedMilliseconds { get; set; }
    public int ReplayChoiceResolutionMilliseconds { get; set; }
    public int ReplayProposalMilliseconds { get; set; }
    public int ReplayExecutionMilliseconds { get; set; }
    public int ReplayPromptTokens { get; set; }
    public int ReplayOutputTokens { get; set; }
    public string ReplayFallbackReason { get; set; } = "none";
    public DateTime CreatedAtUtc { get; set; }
    public InteractionRecipe? Recipe { get; set; }
}
