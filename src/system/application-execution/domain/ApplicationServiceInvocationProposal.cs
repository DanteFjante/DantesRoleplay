using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.ApplicationExecution;

/// <summary>
/// Review proposal for the first read-only service declaration. This contract is unaccepted and
/// unavailable in production unless the coordinator explicitly integrates it. Its authored JSON
/// shape is closed and uses these exact fields:
/// <code>
/// {
///   "inputSchemaHash": "99334726611CCF58A148B0814696BFA6FE08C1B2D027E946BECCF5A74331C9AA",
///   "inputSchemaJson": "{\"additionalProperties\":false,\"properties\":{},\"type\":\"object\"}",
///   "outputSchemaHash": "F000094D3398C2F64FE53862AAE7E5F40BA2EEEF3FFD6E999345B7B0E252203E",
///   "outputSchemaJson": "{\"additionalProperties\":false,\"properties\":{\"count\":{\"type\":\"integer\"}},\"required\":[\"count\"],\"type\":\"object\"}",
///   "reads": [{
///     "alias": "inventory", "qualifiedQueryId": "example.query.inventory",
///     "contract": {
///       "executor": "projection", "projectionQualifiedId": "example.projection.inventory",
///       "projectionVersion": 2,
///       "projectionContentHash": "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD",
///       "outputSchemaHash": "99334726611CCF58A148B0814696BFA6FE08C1B2D027E946BECCF5A74331C9AA",
///       "outputSchemaJson": "{\"additionalProperties\":false,\"properties\":{},\"type\":\"object\"}",
///       "exposure": 0, "roles": ["subject"], "collectionId": null
///     },
///     "roleMappings": { "subject": "viewer" }
///   }]
/// }
/// </code>
/// Exposure uses the numeric value of the existing <c>ApplicationQueryExposure</c> enum; the strict
/// reader constructs <see cref="InteractionQueryContractReference"/> explicitly rather than
/// relying on default serializer constructor binding. This declaration lives inside the retained
/// mechanic requirements under the <c>service</c> property and is covered by that mechanic's
/// content fingerprint. The whole
/// declaration and each embedded schema are bounded to 64 KiB/depth 32. This record carries no
/// source or independently selected service identity.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApplicationReadOnlyServiceDefinitionProposal
{
    public ApplicationReadOnlyServiceDefinitionProposal(
        string inputSchemaHash,
        string inputSchemaJson,
        string outputSchemaHash,
        string outputSchemaJson,
        IReadOnlyList<ApplicationServiceReadDeclarationProposal> reads)
    {
        InputSchemaHash = InteractionGuard.UpperSha256(inputSchemaHash, nameof(inputSchemaHash));
        InputSchemaJson = InteractionCanonicalJson.CanonicalizeObject(inputSchemaJson);
        OutputSchemaHash = InteractionGuard.UpperSha256(outputSchemaHash, nameof(outputSchemaHash));
        OutputSchemaJson = InteractionCanonicalJson.CanonicalizeObject(outputSchemaJson);
        ArgumentNullException.ThrowIfNull(reads);
        var copied = reads.ToArray();
        if (copied.Length > ApplicationReadOnlyServiceLimitsProposal.MaximumReads
            || copied.Any(read => read is null)
            || copied.Select(read => read.Alias).Distinct(StringComparer.Ordinal).Count() != copied.Length)
            throw new InteractionContractException(
                "INVALID_SERVICE_READS",
                "The service read declarations are invalid, duplicated, or outside the closed limit.");
        Reads = Array.AsReadOnly(copied);
    }

    [JsonPropertyName("inputSchemaHash")]
    public string InputSchemaHash { get; }

    [JsonPropertyName("inputSchemaJson")]
    public string InputSchemaJson { get; }

    [JsonPropertyName("outputSchemaHash")]
    public string OutputSchemaHash { get; }

    [JsonPropertyName("outputSchemaJson")]
    public string OutputSchemaJson { get; }

    [JsonPropertyName("reads")]
    public IReadOnlyList<ApplicationServiceReadDeclarationProposal> Reads { get; }
}

/// <summary>
/// One declared read capability. <see cref="RoleMappings"/> maps each query role to a role whose
/// entity is supplied by the trusted service host; invocation input cannot provide entity IDs.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApplicationServiceReadDeclarationProposal
{
    public ApplicationServiceReadDeclarationProposal(
        string alias,
        string qualifiedQueryId,
        InteractionQueryContractReference contract,
        IReadOnlyDictionary<string, string> roleMappings)
    {
        Alias = InteractionGuard.Identifier(alias, nameof(alias));
        QualifiedQueryId = InteractionGuard.Identifier(qualifiedQueryId, nameof(qualifiedQueryId));
        Contract = contract ?? throw new ArgumentNullException(nameof(contract));
        RoleMappings = CopyRoleMappings(roleMappings, contract.Roles);
    }

    [JsonPropertyName("alias")]
    public string Alias { get; }

    [JsonPropertyName("qualifiedQueryId")]
    public string QualifiedQueryId { get; }

    [JsonPropertyName("contract")]
    public InteractionQueryContractReference Contract { get; }

    [JsonPropertyName("roleMappings")]
    public IReadOnlyDictionary<string, string> RoleMappings { get; }

    private static IReadOnlyDictionary<string, string> CopyRoleMappings(
        IReadOnlyDictionary<string, string> mappings,
        IReadOnlyList<string> queryRoles)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        if (mappings.Count > InteractionContractLimits.RoleHints)
            throw new InteractionContractException(
                "INVALID_SERVICE_ROLE_MAPPINGS",
                "The service role mappings exceed the closed limit.");

        var copied = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (queryRole, hostRole) in mappings)
        {
            if (!copied.TryAdd(
                    InteractionGuard.Identifier(queryRole, "queryRole"),
                    InteractionGuard.Identifier(hostRole, "hostRole")))
                throw new InteractionContractException(
                    "INVALID_SERVICE_ROLE_MAPPINGS",
                    "The service role mappings contain a duplicate query role.");
        }

        if (!copied.Keys.SequenceEqual(queryRoles.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InteractionContractException(
                "INVALID_SERVICE_ROLE_MAPPINGS",
                "Every exact query role must map to one trusted host role.");
        return new ReadOnlyDictionary<string, string>(copied);
    }
}

/// <summary>
/// Trusted host request for the proposed runtime. Authored JSON cannot create the
/// <see cref="InteractionInvocationHost"/> authority and cannot supply source. The runtime must
/// re-resolve <see cref="SelectedDefinition"/>, compare <see cref="Definition"/> to that exact
/// retained mechanic's requirements, resolve its read dependencies, validate
/// <see cref="InputJson"/> and the final output against the declared schemas, and reject any
/// mismatch rather than accepting a caller whitelist or substituting another version.
/// </summary>
public sealed record ApplicationReadOnlyServiceInvocationRequestProposal
{
    public ApplicationReadOnlyServiceInvocationRequestProposal(
        InteractionInvocationHost host,
        SystemTaskSelectedDefinition selectedDefinition,
        ApplicationReadOnlyServiceDefinitionProposal definition,
        IReadOnlyDictionary<string, string> hostRoleBindings,
        string inputJson,
        ExecutionLimits computationLimits)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        if (host.Profile != InteractionExecutionProfile.ReadOnly)
            throw new InteractionContractException(
                "SERVICE_PROFILE_UNAVAILABLE",
                "The proposed service surface supports only the read-only profile.");
        SelectedDefinition = selectedDefinition ?? throw new ArgumentNullException(nameof(selectedDefinition));
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        var normalizedRoles = InteractionInvocationRoles.Normalize(hostRoleBindings);
        HostRoleBindings = new ReadOnlyDictionary<string, string>(new SortedDictionary<string, string>(
            normalizedRoles.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            StringComparer.Ordinal));
        InputJson = InteractionCanonicalJson.CanonicalizeObject(inputJson);
        ComputationLimits = computationLimits ?? throw new ArgumentNullException(nameof(computationLimits));
    }

    public InteractionInvocationHost Host { get; }
    public SystemTaskSelectedDefinition SelectedDefinition { get; }
    public ApplicationReadOnlyServiceDefinitionProposal Definition { get; }
    public IReadOnlyDictionary<string, string> HostRoleBindings { get; }
    public string InputJson { get; }
    public ExecutionLimits ComputationLimits { get; }
}

/// <summary>
/// The only host calls exposed to a v1 service engine. Implementations bind this capability to one
/// trusted request and route reads through the existing real
/// <see cref="IApplicationReadModelInvocationAdapter"/>. Each call consumes the shared root budget,
/// reauthorizes the current principal/application/state scope rather than trusting a grant string,
/// resolves the declared alias, derives query roles from trusted host bindings, canonicalizes and
/// bounds input/output to 64 KiB and depth 32, and counts all exchanged data toward the 1 MiB root
/// ceiling. Cancellation and the host deadline are checked before a call and after every wait.
/// The proposed JavaScript surface is <c>ctx.services.read(alias, inputObject)</c> and
/// <c>ctx.services.progress(dataObject)</c>. JSON alone crosses the callback boundary; the CLR
/// capability object is never exposed. The engine adapter must freeze whether reads are
/// synchronous or Promise-based before implementation acceptance; either model must serialize
/// engine access and must not reinterpret process-local waiting as durability. Progress sequence
/// numbers are assigned by the host. Action, workflow, wait/job and AI callbacks return the
/// existing canonical <c>unavailable</c> shape and never dispatch in v1.
/// </summary>
public interface IApplicationReadOnlyServiceCapabilitiesProposal
{
    Task<InteractionInvocationResult> ReadAsync(
        string alias,
        string inputJson,
        CancellationToken cancellationToken = default);

    ApplicationServiceProgressDispositionProposal TryWriteProgress(ApplicationServiceProgressFrameProposal frame);
}

/// <summary>
/// Exact progress wire shape: <c>{"sequence":1,"dataJson":"{\"phase\":\"reading\"}"}</c>.
/// The payload is a canonical JSON object; the entire serialized frame is no larger than 2 KiB.
/// Sequence is assigned by the host, one-based and bounded by the per-root frame limit.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApplicationServiceProgressFrameProposal
{
    public ApplicationServiceProgressFrameProposal(int sequence, string dataJson)
    {
        if (sequence is < 1 or > ApplicationReadOnlyServiceLimitsProposal.MaximumProgressFrames)
            throw new InteractionContractException(
                "INVALID_SERVICE_PROGRESS_SEQUENCE",
                "The service progress sequence is outside the closed limit.");
        var canonical = InteractionCanonicalJson.CanonicalizeObject(dataJson);
        var wire = JsonSerializer.Serialize(new { sequence, dataJson = canonical });
        if (Encoding.UTF8.GetByteCount(wire) > ApplicationReadOnlyServiceLimitsProposal.MaximumProgressFrameBytes)
            throw new InteractionContractException(
                "SERVICE_PROGRESS_TOO_LARGE",
                "The service progress frame exceeds its byte limit.");
        Sequence = sequence;
        DataJson = canonical;
    }

    [JsonPropertyName("sequence")]
    public int Sequence { get; }

    [JsonPropertyName("dataJson")]
    public string DataJson { get; }
}

/// <summary>
/// Host-only return from a transient progress attempt; this enum is never serialized. A progress
/// write is never an invocation result, completion evidence, durable checkpoint, pending handle,
/// or commit receipt.
/// </summary>
public enum ApplicationServiceProgressDispositionProposal
{
    Accepted,
    Backpressured,
    Closed
}

/// <summary>
/// Unaccepted read-only v1 runtime proposal. Until coordinator integration, callers must return a
/// truthful <c>unavailable</c> result. Action, atomic service execution, workflow, durable waits,
/// jobs, and AI callbacks remain unavailable and cannot be represented as pending work.
///
/// A conforming implementation owns one serialized Jint execution per invocation. Producers may
/// enqueue progress or complete awaited reads, but never enter a busy engine concurrently. It uses
/// the host-selected limits without increasing any parent limit, permits at most 16 shared
/// operations including retries, and bounds progress to 32 frames, 2 KiB per frame, 16 KiB total,
/// and channel capacity 8; <c>TryWriteProgress</c> reports backpressure instead of growing a queue.
/// JavaScript continuation state is process-local and is never described as durable.
/// The root computation consumes one operation from <see cref="InteractionInvocationHost.Budget"/>;
/// every read consumes exactly one more through the existing adapter. The service wrapper must not
/// pre-consume the adapter's read allowance. The canonical root input, every callback input/output,
/// and the final output are each limited to 64 KiB/depth 32; together they may exchange at most
/// 1 MiB per root. Progress additionally observes its smaller aggregate limit.
///
/// Real-read conformance requires: the declared exact query succeeds through the production read
/// adapter and returns its read evidence; an undeclared alias, stale dependency, schema mismatch,
/// expired/cancelled host, exhausted budget, or excess exchanged data fails without data leakage;
/// missing current authority restoration returns <c>unavailable</c>. Concurrent producers never
/// enter Jint, and progress never becomes success evidence. A successful computation result must
/// use actual validated runner evidence. No test double alone establishes production availability.
/// </summary>
public interface IApplicationReadOnlyServiceInvocationProposal
{
    Task<InteractionInvocationResult> InvokeAsync(
        ApplicationReadOnlyServiceInvocationRequestProposal request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Required owner-local declaration boundary for this proposal. It accepts only retained authored
/// bytes already covered by the supplied <c>selectedDefinition</c> fingerprint; an invocation
/// caller cannot submit or replace the capability list. Before acceptance, its implementation must
/// canonicalize with duplicate-key rejection, enforce the 64 KiB/depth-32 aggregate bound, require
/// the exact camel-case fields shown above, reject unknown fields recursively, validate closed
/// input/output schemas and hashes, and explicitly construct every existing
/// <see cref="InteractionQueryContractReference"/>. No existing shared JSON converter is changed.
/// </summary>
public interface IApplicationReadOnlyServiceDefinitionReaderProposal
{
    ApplicationReadOnlyServiceDefinitionProposal ReadRetained(
        SystemTaskSelectedDefinition selectedDefinition,
        string retainedMechanicRequirementsJson);
}

public static class ApplicationReadOnlyServiceLimitsProposal
{
    public const int MaximumReads = InteractionContractLimits.ProposalSteps;
    public const int MaximumExchangedBytesPerRoot = 1024 * 1024;
    public const int MaximumProgressFrames = 32;
    public const int MaximumProgressFrameBytes = 2 * 1024;
    public const int MaximumProgressBytesPerRoot = 16 * 1024;
    public const int ProgressChannelCapacity = 8;
}
