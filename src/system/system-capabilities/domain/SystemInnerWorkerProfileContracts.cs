using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DantesRoleplay.AI;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.SystemCapabilities;

/// <summary>One host-bound direct tool definition and its exact selected capability revision.</summary>
public sealed record SystemInnerWorkerToolBinding
{
    public SystemInnerWorkerToolBinding(AiToolDefinition definition,
        SystemTaskSelectedDefinition capabilityVersion, SystemCapabilityMode mode)
    {
        ArgumentNullException.ThrowIfNull(definition);
        CapabilityVersion = capabilityVersion ?? throw new ArgumentNullException(nameof(capabilityVersion));
        if (!Enum.IsDefined(mode))
            throw Failure("INVALID_WORKER_TOOL", "A focused worker tool needs a closed capability mode.");
        if (!ToolName.IsMatch(definition.Name))
            throw Failure("INVALID_WORKER_TOOL", "A focused worker tool needs a valid exact tool name.");
        Definition = definition with
        {
            Description = InteractionGuard.Bounded(definition.Description, InteractionContractLimits.SafeEvidenceText,
                "INVALID_WORKER_TOOL", nameof(definition.Description)),
            InputSchemaJson = InteractionCanonicalJson.CanonicalizeObject(definition.InputSchemaJson)
        };
        Mode = mode;
    }

    public AiToolDefinition Definition { get; }
    public SystemTaskSelectedDefinition CapabilityVersion { get; }
    public SystemCapabilityMode Mode { get; }

    private static readonly Regex ToolName = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);
    private static InteractionContractException Failure(string code, string message) => new(code, message);
}

/// <summary>
/// Reference-only evidence for the one accepted manual-context packet. The packet is owned by the
/// manual-context service and is deliberately not copied into a focused worker profile.
/// </summary>
public sealed record SystemInnerWorkerManualContextEvidence(string Reference, string Fingerprint)
{
    public SystemInnerWorkerManualContextEvidence Validate() => new(
        InteractionGuard.Bounded(Reference, 1_024, "INVALID_WORKER_MANUAL_CONTEXT", nameof(Reference)),
        InteractionGuard.UpperSha256(Fingerprint, nameof(Fingerprint)));
}

/// <summary>
/// Opaque authority provenance retained for a later policy recheck. Its strings are evidence only;
/// constructing this record neither validates a grant nor authorizes a worker.
/// </summary>
public sealed record SystemInnerWorkerAuthorityProvenance(string Reference, string GrantRevision, string GrantFingerprint)
{
    public SystemInnerWorkerAuthorityProvenance Validate() => new(
        InteractionGuard.Bounded(Reference, 1_024, "INVALID_WORKER_AUTHORITY_PROVENANCE", nameof(Reference)),
        InteractionGuard.Bounded(GrantRevision, 1_024, "INVALID_WORKER_AUTHORITY_PROVENANCE", nameof(GrantRevision)),
        InteractionGuard.UpperSha256(GrantFingerprint, nameof(GrantFingerprint)));
}

/// <summary>
/// Host-resolved profile for a future focused worker invocation. It preserves the existing worker
/// host scope and contains only exact references to context/manual/grant evidence; it is not an
/// authored input shape, a manual packet, an execution history, or an authorization decision.
/// Selected fingerprints pin source definitions; the future resolver must still verify their
/// freshness and mapping to the materialized AI profile/tool payload before invocation.
/// </summary>
[JsonConverter(typeof(RejectSystemInnerWorkerResolvedProfileJsonConverter))]
public sealed record SystemInnerWorkerResolvedProfile
{
    public SystemInnerWorkerResolvedProfile(
        SystemInnerWorkerRequest worker,
        SystemTaskSelectedDefinition profileVersion,
        AiAgentProfile profile,
        string outputSchemaFingerprint,
        IReadOnlyList<SystemInnerWorkerToolBinding>? toolBindings,
        IReadOnlyList<string>? requiredContextReferences,
        SystemInnerWorkerManualContextEvidence manualContext,
        SystemInnerWorkerAuthorityProvenance authorityProvenance,
        SystemInnerWorkerAiBudget? aiBudget = null,
        SystemInnerWorkerAuthorityProvenance? readAuthorityProvenance = null)
    {
        Worker = worker ?? throw new ArgumentNullException(nameof(worker));
        ProfileVersion = profileVersion ?? throw new ArgumentNullException(nameof(profileVersion));
        ArgumentNullException.ThrowIfNull(profile);
        ValidateProfile(profile);
        if (!StringComparer.Ordinal.Equals(ProfileVersion.ExactDefinitionId, profile.Id))
            throw Failure("WORKER_PROFILE_IDENTITY_CONFLICT", "The selected profile reference does not match the host-resolved AI profile.");
        Profile = profile with { };
        OutputSchemaFingerprint = InteractionGuard.UpperSha256(outputSchemaFingerprint, nameof(outputSchemaFingerprint));
        if (!StringComparer.Ordinal.Equals(OutputSchemaFingerprint, Hash(Worker.ResultSchemaJson)))
            throw Failure("WORKER_OUTPUT_SCHEMA_STALE", "The output schema fingerprint does not match the worker's exact schema.");

        var tools = toolBindings?.ToArray() ?? [];
        if (tools.Length > 16 || tools.Any(value => value is null)
            || tools.Select(value => value.Definition.Name).Distinct(StringComparer.Ordinal).Count() != tools.Length
            || tools.GroupBy(value => value.CapabilityVersion.ExactDefinitionId, StringComparer.Ordinal)
                .Any(group => group.Select(value => (value.CapabilityVersion.Version, value.CapabilityVersion.Fingerprint))
                    .Distinct().Skip(1).Any()))
            throw Failure("INVALID_WORKER_TOOL_BINDINGS", "Focused worker tools must be distinct and within the closed bound.");
        ToolBindings = Array.AsReadOnly(tools.Select(value => new SystemInnerWorkerToolBinding(
            value.Definition, value.CapabilityVersion, value.Mode)).ToArray());

        var references = requiredContextReferences?.ToArray() ?? [];
        if (references.Length > 64 || references.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 1_024)
            || references.Distinct(StringComparer.Ordinal).Count() != references.Length)
            throw Failure("INVALID_WORKER_CONTEXT_REFERENCES", "Required worker context references must be distinct and within the closed bound.");
        Array.Sort(references, StringComparer.Ordinal);
        RequiredContextReferences = Array.AsReadOnly(references);
        ManualContext = (manualContext ?? throw new ArgumentNullException(nameof(manualContext))).Validate();
        AuthorityProvenance = (authorityProvenance ?? throw new ArgumentNullException(nameof(authorityProvenance))).Validate();
        ReadAuthorityProvenance = readAuthorityProvenance?.Validate();
        AiBudget = aiBudget ?? new SystemInnerWorkerAiBudget();
        if (Worker.Subject is SystemInnerWorkerSubject.ApplicationCandidateValidation
            && (ProfileVersion != SystemInnerWorkerCandidateReviewer.ProfileVersion
                || Profile != SystemInnerWorkerCandidateReviewer.Profile))
            throw Failure("WORKER_VALIDATION_REVIEWER_INVALID", "Candidate validation requires the exact immutable host reviewer definition and version.");
        if (Worker.Subject is SystemInnerWorkerSubject.ApplicationCandidateValidation
            && (ReadAuthorityProvenance is null || ToolBindings.Count != 0 || AiBudget.ToolCalls != 0))
            throw Failure("WORKER_VALIDATION_AUTHORITY_INVALID", "Candidate validation requires independent Read evidence, no tools, and a zero tool-call budget.");
    }

    public SystemInnerWorkerRequest Worker { get; }
    public SystemTaskSelectedDefinition ProfileVersion { get; }
    public AiAgentProfile Profile { get; }
    public string OutputSchemaFingerprint { get; }
    public IReadOnlyList<SystemInnerWorkerToolBinding> ToolBindings { get; }
    public IReadOnlyList<string> RequiredContextReferences { get; }
    public SystemInnerWorkerManualContextEvidence ManualContext { get; }
    /// <summary>Workflow Execute or candidate Validate provenance; not an authorization decision.</summary>
    public SystemInnerWorkerAuthorityProvenance AuthorityProvenance { get; }
    /// <summary>Independent context/candidate Read evidence. Required for candidate validation.</summary>
    public SystemInnerWorkerAuthorityProvenance? ReadAuthorityProvenance { get; }
    /// <summary>Host-selected ceiling only; it is not a reservation, debit, or execution grant.</summary>
    public SystemInnerWorkerAiBudget AiBudget { get; }

    private static void ValidateProfile(AiAgentProfile profile)
    {
        if (!AgentId.IsMatch(profile.Id) || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 120
            || string.IsNullOrWhiteSpace(profile.Identity) || profile.Identity.Length > 2_000
            || profile.Instructions is null || profile.Instructions.Length > 8_000)
            throw Failure("INVALID_WORKER_PROFILE", "The host-resolved AI profile does not satisfy the existing agent limits.");
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static InteractionContractException Failure(string code, string message) => new(code, message);
    private static readonly Regex AgentId = new("^[a-z0-9][a-z0-9._-]{0,79}$", RegexOptions.CultureInvariant);
}

public sealed class RejectSystemInnerWorkerResolvedProfileJsonConverter : JsonConverter<SystemInnerWorkerResolvedProfile>
{
    public override SystemInnerWorkerResolvedProfile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new JsonException("Focused worker profiles are resolved by the host and cannot be deserialized.");

    public override void Write(Utf8JsonWriter writer, SystemInnerWorkerResolvedProfile value, JsonSerializerOptions options) =>
        throw new JsonException("Focused worker profiles are host-only authority and cannot be serialized.");
}
