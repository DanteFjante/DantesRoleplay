using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.ApplicationActivation;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DantesRoleplay.SystemCapabilities;

/// <summary>Closed host-selected subject identity; constructing it grants no authority.</summary>
[JsonConverter(typeof(RejectSystemInnerWorkerSubjectJsonConverterFactory))]
public abstract record SystemInnerWorkerSubject
{
    private SystemInnerWorkerSubject() { }
    [JsonConverter(typeof(RejectSystemInnerWorkerSubjectJsonConverterFactory))]
    public sealed record ProcedureWorkflow : SystemInnerWorkerSubject
    {
        public ProcedureWorkflow(SystemTaskSelectedDefinition procedureVersion) => ProcedureVersion = procedureVersion ?? throw new ArgumentNullException(nameof(procedureVersion));
        public SystemTaskSelectedDefinition ProcedureVersion { get; }
    }
    [JsonConverter(typeof(RejectSystemInnerWorkerSubjectJsonConverterFactory))]
    public sealed record ApplicationCandidateValidation : SystemInnerWorkerSubject
    {
        public ApplicationCandidateValidation(ApplicationCandidateReference candidate)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            if (candidate.ApplicationId is null || candidate.CandidateId is not { Length: 32 }
                || candidate.CandidateId.Any(value => !(value is >= '0' and <= '9' or >= 'a' and <= 'f'))
                || candidate.Revision < 1)
                throw new InteractionContractException("INVALID_WORKER_CANDIDATE", "An exact candidate identity and positive revision are required.");
            _ = InteractionGuard.UpperSha256(candidate.ContentFingerprint, nameof(candidate.ContentFingerprint));
            Candidate = candidate with { };
        }
        public ApplicationCandidateReference Candidate { get; }
    }
}

public sealed class RejectSystemInnerWorkerSubjectJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeof(SystemInnerWorkerSubject).IsAssignableFrom(typeToConvert);
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(RejectSubject<>).MakeGenericType(typeToConvert))!;

    private sealed class RejectSubject<T> : JsonConverter<T> where T : SystemInnerWorkerSubject
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new JsonException("Worker subjects are host-only.");
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            throw new JsonException("Worker subjects are host-only.");
    }
}

/// <summary>Bounded host-selected request. It contains no provider or tool authority.</summary>
public sealed record SystemInnerWorkerRequest
{
    public SystemInnerWorkerRequest(InteractionInvocationHost invocationHost,
        SystemTaskSelectedDefinition procedureVersion, string inputJson, string resultSchemaJson,
        IReadOnlyList<SystemTaskDurableHandle>? dependencyHandles = null)
        : this(invocationHost, new SystemInnerWorkerSubject.ProcedureWorkflow(procedureVersion),
            inputJson, resultSchemaJson, dependencyHandles) { }

    public SystemInnerWorkerRequest(InteractionInvocationHost invocationHost, SystemInnerWorkerSubject subject,
        string inputJson, string resultSchemaJson, IReadOnlyList<SystemTaskDurableHandle>? dependencyHandles = null)
    {
        InvocationHost = invocationHost ?? throw new ArgumentNullException(nameof(invocationHost));
        Subject = subject ?? throw new ArgumentNullException(nameof(subject));
        switch (subject)
        {
            case SystemInnerWorkerSubject.ProcedureWorkflow:
                if (invocationHost.StateSpaceId is null || invocationHost.StateRevision is null)
                    throw new InteractionContractException("WORKER_STATE_SCOPE_REQUIRED", "A procedure worker requires an actual state scope.");
                break;
            case SystemInnerWorkerSubject.ApplicationCandidateValidation validation:
                if (invocationHost.StateSpaceId is not null || invocationHost.StateRevision is not null
                    || invocationHost.Profile != InteractionExecutionProfile.ReadOnly
                    || validation.Candidate.ApplicationId != invocationHost.ApplicationRevision.ApplicationId)
                    throw new InteractionContractException("WORKER_VALIDATION_SCOPE_INVALID", "Candidate validation requires its exact application and a read-only application host.");
                break;
            default:
                throw new InteractionContractException("INVALID_WORKER_SUBJECT", "The worker subject is unsupported.");
        }
        InputJson = InteractionCanonicalJson.CanonicalizeObject(inputJson);
        ResultSchemaJson = InteractionCanonicalJson.CanonicalizeObject(resultSchemaJson);
        var dependencies = dependencyHandles?.ToArray() ?? [];
        if (dependencies.Length > InteractionContractLimits.DependenciesPerStep)
            throw new InteractionContractException("TOO_MANY_WORKER_DEPENDENCIES", "The worker dependency collection exceeds its bound.");
        if (dependencies.Any(handle => handle is null) || dependencies.Select(handle => handle.TaskId).Distinct(StringComparer.Ordinal).Count() != dependencies.Length)
            throw new InteractionContractException("INVALID_WORKER_DEPENDENCIES", "Worker dependencies must be non-null and have distinct task IDs.");
        DependencyHandles = Array.AsReadOnly(dependencies);
    }

    public InteractionInvocationHost InvocationHost { get; }
    public SystemInnerWorkerSubject Subject { get; }
    public SystemTaskSelectedDefinition? ProcedureVersion => (Subject as SystemInnerWorkerSubject.ProcedureWorkflow)?.ProcedureVersion;
    public string InputJson { get; }
    public string ResultSchemaJson { get; }
    public IReadOnlyList<SystemTaskDurableHandle> DependencyHandles { get; }
}

public interface ISystemInnerWorkerService
{
    Task<InteractionInvocationResult> SubmitAsync(SystemInnerWorkerRequest request,
        CancellationToken cancellationToken = default);
}
