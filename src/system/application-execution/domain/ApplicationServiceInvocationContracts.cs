using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Interactions;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.ApplicationExecution;

/// <summary>Closed read-only declaration retained in mechanic requirements.service. No source or authority is accepted here.</summary>
[JsonConverter(typeof(ApplicationReadOnlyServiceDefinitionJsonConverter))]
public sealed record ApplicationReadOnlyServiceDefinition
{
    public ApplicationReadOnlyServiceDefinition(
        string inputSchemaHash,
        string inputSchemaJson,
        string outputSchemaHash,
        string outputSchemaJson,
        IReadOnlyList<ApplicationServiceReadDeclaration> reads,
        IBoundedJsonSchemaValidator schemas)
    {
        InputSchemaHash = InteractionGuard.UpperSha256(inputSchemaHash, nameof(inputSchemaHash));
        InputSchemaJson = ApplicationServiceSchema.Validate(schemas, inputSchemaJson, InputSchemaHash);
        OutputSchemaHash = InteractionGuard.UpperSha256(outputSchemaHash, nameof(outputSchemaHash));
        OutputSchemaJson = ApplicationServiceSchema.Validate(schemas, outputSchemaJson, OutputSchemaHash);
        ArgumentNullException.ThrowIfNull(reads);
        var copied = reads.ToArray();
        if (copied.Length > ApplicationReadOnlyServiceLimits.MaximumReads
            || copied.Any(read => read is null)
            || copied.Select(read => read.Alias).Distinct(StringComparer.Ordinal).Count() != copied.Length)
            throw new InteractionContractException(
                "INVALID_SERVICE_READS",
                "The service read declarations are invalid, duplicated, or outside the closed limit.");
        Reads = Array.AsReadOnly(copied);
        _ = ToJson(); // Apply the aggregate bound to host construction as well as retained JSON.
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
    public IReadOnlyList<ApplicationServiceReadDeclaration> Reads { get; }

    public string ToJson() => InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
    {
        InputSchemaHash, InputSchemaJson, OutputSchemaHash, OutputSchemaJson,
        Reads = Reads.Select(read => new
        {
            read.Alias, read.QualifiedQueryId,
            Contract = new
            {
                read.Contract.Executor, read.Contract.ProjectionQualifiedId, read.Contract.ProjectionVersion,
                read.Contract.ProjectionContentHash, read.Contract.OutputSchemaHash,
                OutputSchemaJson = read.DeclaredOutputSchemaJson,
                read.Contract.Exposure, read.Contract.Roles, read.Contract.CollectionId
            },
            read.RoleMappings
        })
    },
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
}

/// <summary>
/// One declared read capability. <see cref="RoleMappings"/> maps each query role to a role whose
/// entity is supplied by the trusted service host; invocation input cannot provide entity IDs.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApplicationServiceReadDeclaration
{
    public ApplicationServiceReadDeclaration(
        string alias,
        string qualifiedQueryId,
        InteractionQueryContractReference contract,
        IReadOnlyDictionary<string, string> roleMappings,
        IBoundedJsonSchemaValidator schemas,
        string declaredOutputSchemaJson)
    {
        Alias = InteractionGuard.Identifier(alias, nameof(alias));
        QualifiedQueryId = InteractionGuard.Identifier(qualifiedQueryId, nameof(qualifiedQueryId));
        Contract = contract ?? throw new ArgumentNullException(nameof(contract));
        DeclaredOutputSchemaJson = ApplicationServiceSchema.Validate(schemas, declaredOutputSchemaJson, contract.OutputSchemaHash);
        if (InteractionCanonicalJson.CanonicalizeObject(DeclaredOutputSchemaJson) != contract.OutputSchemaJson)
            throw new InteractionContractException("SERVICE_SCHEMA_MISMATCH", "The declared query schema differs from its pinned reference.");
        if (contract.Executor is not (ApplicationQueryContract.ProjectionExecutor
            or ApplicationQueryContract.MechanicProjectionExecutor or ApplicationQueryContract.ObjectProjectionExecutor))
            throw new InteractionContractException("INVALID_SERVICE_QUERY_EXECUTOR", "The query executor is unsupported.");
        RoleMappings = CopyRoleMappings(roleMappings, contract.Roles);
    }

    [JsonPropertyName("alias")]
    public string Alias { get; }

    [JsonPropertyName("qualifiedQueryId")]
    public string QualifiedQueryId { get; }

    [JsonPropertyName("contract")]
    public InteractionQueryContractReference Contract { get; }

    // The existing schema owner hashes its own normalization, which preserves property order.
    // Query references canonicalize their copy, so retain the owner-normalized bytes separately.
    [JsonIgnore]
    public string DeclaredOutputSchemaJson { get; }

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
/// Trusted host request for the read-only runtime. Authored JSON cannot create the
/// <see cref="InteractionInvocationHost"/> authority and cannot supply source. The runtime must
/// re-resolve <see cref="SelectedDefinition"/>, compare <see cref="Definition"/> to that exact
/// retained mechanic's requirements, resolve its read dependencies, validate
/// <see cref="InputJson"/> and the final output against the declared schemas, and reject any
/// mismatch rather than accepting a caller whitelist or substituting another version.
/// </summary>
[JsonConverter(typeof(RejectApplicationServiceInvocationRequestJsonConverter))]
public sealed record ApplicationReadOnlyServiceInvocationRequest
{
    public ApplicationReadOnlyServiceInvocationRequest(
        InteractionInvocationHost host,
        SystemTaskSelectedDefinition selectedDefinition,
        ApplicationReadOnlyServiceDefinition definition,
        IReadOnlyDictionary<string, string> hostRoleBindings,
        string inputJson,
        ExecutionLimits computationLimits,
        ApplicationServiceProgressChannel? progress = null)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        if (host.Profile != InteractionExecutionProfile.ReadOnly)
            throw new InteractionContractException(
                "SERVICE_PROFILE_UNAVAILABLE",
                "The service surface supports only the read-only profile.");
        SelectedDefinition = selectedDefinition ?? throw new ArgumentNullException(nameof(selectedDefinition));
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        var normalizedRoles = InteractionInvocationRoles.Normalize(hostRoleBindings);
        HostRoleBindings = new ReadOnlyDictionary<string, string>(new SortedDictionary<string, string>(
            normalizedRoles.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            StringComparer.Ordinal));
        InputJson = InteractionCanonicalJson.CanonicalizeObject(inputJson);
        ComputationLimits = computationLimits ?? throw new ArgumentNullException(nameof(computationLimits));
        Progress = progress;
    }

    public InteractionInvocationHost Host { get; }
    public SystemTaskSelectedDefinition SelectedDefinition { get; }
    public ApplicationReadOnlyServiceDefinition Definition { get; }
    public IReadOnlyDictionary<string, string> HostRoleBindings { get; }
    public string InputJson { get; }
    /// <summary>
    /// Root mechanic limits only. Child reads retain the existing host-owned ReadModel interpreter
    /// limits; all calls share the invocation operation ledger/deadline, and callback waits are
    /// additionally bounded by the root's remaining wall time. This is not aggregate memory,
    /// statement or recursion accounting across child engines.
    /// </summary>
    public ExecutionLimits ComputationLimits { get; }
    public ApplicationServiceProgressChannel? Progress { get; }
}

/// <summary>
/// The only host calls exposed to a v1 service engine. Implementations bind this capability to one
/// trusted request and route reads through the existing real
/// <see cref="IApplicationReadModelInvocationAdapter"/>. Each call consumes the shared root budget,
/// reauthorizes the current principal/application/state scope rather than trusting a grant string,
/// resolves the declared alias, derives query roles from trusted host bindings, canonicalizes and
/// bounds input/output to 64 KiB and depth 32, and counts all exchanged data toward the 1 MiB root
/// ceiling. Cancellation and the host deadline are checked before a call and after every wait.
/// The JavaScript surface is <c>ctx.services.read(alias, inputObject)</c> and
/// <c>ctx.services.progress(dataObject)</c>. JSON alone crosses the callback boundary; the CLR
/// capability object is never exposed. Reads and progress are synchronous on the sole owning
/// engine thread. Host waits use a linked deadline bounded by both the invocation deadline and
/// remaining computation timeout. These waits are not durable. Progress sequence
/// numbers are assigned by the host. Action, workflow, wait/job and AI callbacks return the
/// existing canonical <c>unavailable</c> shape and never dispatch in v1.
/// </summary>
public interface IApplicationReadOnlyServiceCapabilities
{
    Task<InteractionInvocationResult> ReadAsync(
        string alias,
        string inputJson,
        CancellationToken cancellationToken = default);

    ApplicationServiceProgressDisposition TryWriteProgress(string dataJson);
}

/// <summary>
/// Exact progress wire shape: <c>{"sequence":1,"dataJson":"{\"phase\":\"reading\"}"}</c>.
/// The payload is a canonical JSON object; the entire serialized frame is no larger than 2 KiB.
/// Sequence is assigned by the host, one-based and bounded by the per-root frame limit.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ApplicationServiceProgressFrame
{
    public ApplicationServiceProgressFrame(int sequence, string dataJson)
    {
        if (sequence is < 1 or > ApplicationReadOnlyServiceLimits.MaximumProgressFrames)
            throw new InteractionContractException(
                "INVALID_SERVICE_PROGRESS_SEQUENCE",
                "The service progress sequence is outside the closed limit.");
        var canonical = InteractionCanonicalJson.CanonicalizeObject(dataJson);
        var wire = JsonSerializer.Serialize(new { sequence, dataJson = canonical });
        if (Encoding.UTF8.GetByteCount(wire) > ApplicationReadOnlyServiceLimits.MaximumProgressFrameBytes)
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
public enum ApplicationServiceProgressDisposition
{
    Accepted,
    Backpressured,
    Closed
}

/// <summary>
/// Read-only v1 runtime contract. Until coordinator integration, production callers must return a
/// truthful <c>unavailable</c> result. Action, atomic service execution, workflow, durable waits,
/// jobs, and AI callbacks remain unavailable and cannot be represented as pending work.
///
/// A conforming implementation owns one serialized Jint execution per invocation. Producers may
/// enqueue progress or complete awaited reads, but never enter a busy engine concurrently. It uses
/// the host-selected root limits, permits at most 16 shared
/// operations including retries, and bounds progress to 32 frames, 2 KiB per frame, 16 KiB total,
/// and channel capacity 8; <c>TryWriteProgress</c> reports backpressure instead of growing a queue.
/// Attempts, including backpressured retries, consume the 32-attempt root allowance; accepted
/// frames alone receive consecutive host sequence numbers. Producers cannot retry indefinitely.
/// JavaScript continuation state is process-local and is never described as durable.
/// The root computation consumes one operation from <see cref="InteractionInvocationHost.Budget"/>;
/// every read consumes exactly one more through the existing adapter. The service wrapper must not
/// pre-consume the adapter's read allowance. The canonical root input, every callback input/output,
/// and the final output are each limited to 64 KiB/depth 32; together they may exchange at most
/// 1 MiB per root. Progress additionally observes its smaller aggregate limit.
/// Child queries use the existing host-owned ReadModel computation caps, sharing the root operation
/// ledger and deadline. Root memory, statement and recursion caps are not aggregate child limits.
///
/// Real-read conformance requires: the declared exact query succeeds through the production read
/// adapter and returns its read evidence; an undeclared alias, stale dependency, schema mismatch,
/// expired/cancelled host, exhausted budget, or excess exchanged data fails without data leakage;
/// missing current authority restoration returns <c>unavailable</c>. Concurrent producers never
/// enter Jint, and progress never becomes success evidence. A successful computation result must
/// use actual validated runner evidence. No test double alone establishes production availability.
/// </summary>
public interface IApplicationReadOnlyServiceInvocationAdapter
{
    Task<InteractionInvocationResult> InvokeAsync(
        ApplicationReadOnlyServiceInvocationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Owner-local declaration boundary. It accepts only retained authored
/// bytes already covered by the supplied <c>selectedDefinition</c> fingerprint; an invocation
/// caller cannot submit or replace the capability list. Its implementation must
/// canonicalize with duplicate-key rejection, enforce the 64 KiB/depth-32 aggregate bound, require
/// the exact camel-case fields shown above, reject unknown fields recursively, validate closed
/// input/output schemas and hashes, and explicitly construct every existing
/// <see cref="InteractionQueryContractReference"/>. No existing shared JSON converter is changed.
/// </summary>
public interface IApplicationReadOnlyServiceDefinitionReader
{
    ApplicationReadOnlyServiceDefinition ReadRetained(
        SystemTaskSelectedDefinition selectedDefinition,
        CatalogRecordView retainedMechanic);
}

public static class ApplicationReadOnlyServiceLimits
{
    public const int MaximumReads = InteractionContractLimits.ProposalSteps;
    public const int MaximumExchangedBytesPerRoot = 1024 * 1024;
    public const int MaximumProgressFrames = 32;
    public const int MaximumProgressFrameBytes = 2 * 1024;
    public const int MaximumProgressBytesPerRoot = 16 * 1024;
    public const int ProgressChannelCapacity = 8;
}

internal static class ApplicationServiceSchema
{
    internal static string Validate(IBoundedJsonSchemaValidator schemas, string json, string expectedHash)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        _ = InteractionCanonicalJson.CanonicalizeObject(json); // Bounds and duplicates, without changing schema-owner hash semantics.
        var compiled = schemas.Compile(json);
        if (!compiled.IsAccepted || compiled.SchemaHash != expectedHash)
            throw new InteractionContractException("SERVICE_SCHEMA_MISMATCH",
                "The service schema is invalid or does not match its declared hash.");
        return compiled.NormalizedSchema;
    }
}

public sealed class RejectApplicationServiceInvocationRequestJsonConverter
    : JsonConverter<ApplicationReadOnlyServiceInvocationRequest>
{
    public override ApplicationReadOnlyServiceInvocationRequest Read(ref Utf8JsonReader reader,
        Type typeToConvert, JsonSerializerOptions options) =>
        throw new JsonException("Service invocation requests are host-only and cannot be deserialized.");

    public override void Write(Utf8JsonWriter writer, ApplicationReadOnlyServiceInvocationRequest value,
        JsonSerializerOptions options) =>
        throw new JsonException("Service invocation authority is not serializable.");
}

public sealed class ApplicationReadOnlyServiceDefinitionJsonConverter : JsonConverter<ApplicationReadOnlyServiceDefinition>
{
    public override ApplicationReadOnlyServiceDefinition Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) => throw new JsonException("Service declarations must be read from exact retained mechanics.");

    public override void Write(Utf8JsonWriter writer, ApplicationReadOnlyServiceDefinition value,
        JsonSerializerOptions options) => writer.WriteRawValue(value.ToJson());
}
