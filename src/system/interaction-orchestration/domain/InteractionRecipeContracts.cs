using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
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
        IEnumerable<string> roleSlots)
    {
        StepId = InteractionGuard.Identifier(stepId, nameof(stepId));
        QualifiedId = InteractionGuard.Identifier(qualifiedId, nameof(qualifiedId));
        if (!SafeToken(StepId, allowDots: true) || !QualifiedId.Split('.').All(SafeSegment))
            throw new InteractionContractException("RECIPE_TEMPLATE_UNSAFE", "A recipe contains an unsafe identifier.");
        if (contractVersion < 1)
            throw new InteractionContractException("INVALID_RECIPE_CONTRACT_VERSION", "A recipe contract version must be positive.");
        ContractVersion = contractVersion;
        ContractFingerprint = InteractionGuard.UpperSha256(contractFingerprint, nameof(contractFingerprint));
        DependsOn = InteractionGuard.CopyDistinctList(dependsOn, InteractionContractLimits.DependenciesPerStep,
            "INVALID_RECIPE_DEPENDENCIES", sort: true);
        RoleSlots = InteractionGuard.CopyDistinctList(roleSlots, InteractionContractLimits.RoleHints,
            "INVALID_RECIPE_ROLE_SLOTS", sort: true);
        if (RoleSlots.Any(value => !SafeToken(value, allowDots: false)))
            throw new InteractionContractException("RECIPE_TEMPLATE_UNSAFE", "A recipe contains an unsafe role-slot name.");
    }

    public string StepId { get; }
    public string QualifiedId { get; }
    public int ContractVersion { get; }
    public string ContractFingerprint { get; }
    public IReadOnlyList<string> DependsOn { get; }
    public IReadOnlyList<string> RoleSlots { get; }

    private static bool SafeToken(string value, bool allowDots) => value.Length > 0
        && char.IsAsciiLetter(value[0])
        && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '_' or '-' || allowDots && character == '.');

    private static bool SafeSegment(string value) => value.Length > 0
        && char.IsAsciiLetterLower(value[0])
        && value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-');
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
        if (proposal.Steps.Any(step => step.Kind != InteractionPlanStepKind.Action))
            throw new InteractionContractException("RECIPE_STEP_KIND_UNSUPPORTED", "The first recipe format supports action steps only.");
        if (proposal.Steps.Any(step => step.ResultBindings is { Count: > 0 }))
            throw new InteractionContractException("RECIPE_RESULT_BINDINGS_UNSUPPORTED", "A recipe cannot retain result bindings.");
        if (proposal.Steps.Any(step => InteractionCanonicalJson.CanonicalizeObject(step.InputJson) != "{}"))
            throw new InteractionContractException("RECIPE_INPUT_PARAMETERIZATION_UNSUPPORTED", "A recipe cannot retain mechanic input values.");

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
            return new InteractionRecipeTemplateStep($"step.{index + 1}", step.QualifiedId, step.Version,
                step.Fingerprint, step.DependsOn.Select(value => stepIds[value]), step.RoleBindings.Keys);
        }).ToArray();
        ValidateGraph(steps);
        var canonical = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            steps = steps.Select(step => new
            {
                stepId = step.StepId,
                qualifiedId = step.QualifiedId,
                version = step.ContractVersion,
                fingerprint = step.ContractFingerprint,
                dependsOn = step.DependsOn,
                roleSlots = step.RoleSlots,
                input = new { }
            })
        }));
        var fingerprint = InteractionCanonicalJson.Fingerprint(
            InteractionRecipeProtocol.TemplateFingerprintDomain, canonical);
        return new(Array.AsReadOnly(steps), canonical, fingerprint);
    }

    public static InteractionRecipeTemplate Parse(string json, ApplicationIdentifier applicationId)
    {
        ArgumentNullException.ThrowIfNull(applicationId);
        var canonical = InteractionCanonicalJson.CanonicalizeObject(json);
        using var document = JsonDocument.Parse(canonical);
        var root = document.RootElement;
        ExactProperties(root, "steps");
        if (!root.TryGetProperty("steps", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new InteractionContractException("INVALID_RECIPE_TEMPLATE", "Recipe steps must be an array.");
        var drafts = new List<InteractionPlannerDraftStep>();
        foreach (var item in items.EnumerateArray())
        {
            ExactProperties(item, "dependsOn", "fingerprint", "input", "qualifiedId", "roleSlots", "stepId", "version");
            if (!item.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object
                || InteractionCanonicalJson.CanonicalizeObject(input.GetRawText()) != "{}")
                throw new InteractionContractException("RECIPE_INPUT_PARAMETERIZATION_UNSUPPORTED", "A recipe cannot retain mechanic input values.");
            var roleSlots = RequiredStrings(item, "roleSlots");
            var bindings = roleSlots.ToDictionary(value => value, _ => "slot", StringComparer.Ordinal);
            drafts.Add(new(
                RequiredString(item, "stepId"), InteractionPlanStepKind.Action,
                RequiredString(item, "qualifiedId"), RequiredInteger(item, "version"),
                RequiredString(item, "fingerprint"), RequiredStrings(item, "dependsOn"),
                new ReadOnlyDictionary<string, string>(bindings), "{}"));
        }
        var result = FromProposal(applicationId, new(drafts.AsReadOnly()));
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
        foreach (var step in steps)
        {
            if (step.DependsOn.Any(value => !seen.Contains(value)))
                throw new InteractionContractException("INVALID_RECIPE_DEPENDENCIES", "Recipe dependencies must name earlier steps.");
            seen.Add(step.StepId);
        }
    }

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
    DateTime CreatedAtUtc);

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
    string RoleProfile);

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
    public DateTime CreatedAtUtc { get; set; }
    public InteractionRecipe? Recipe { get; set; }
}
