using DantesRoleplay.Retrieval;

namespace DantesRoleplay.Information;

/// <summary>A neutral, user-defined collection of information records.</summary>
public sealed class InformationSource
{
    public string Id { get; set; } = string.Empty;
    public string ScopeId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string MetadataSchemaJson { get; set; } = "{}";
    public string ContentHash { get; set; } = string.Empty;
    public int Revision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public ICollection<InformationRecord> Records { get; } = new List<InformationRecord>();
}

/// <summary>One bounded text record. Its metadata is source-defined and opaque to the kernel.</summary>
public sealed class InformationRecord
{
    public string Id { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
    public string ContentHash { get; set; } = string.Empty;
    public int Revision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public InformationSource Source { get; set; } = null!;
}

/// <summary>A scoped declaration of an action the host is explicitly willing to execute.</summary>
public sealed class InformationActionContract
{
    public string Id { get; set; } = string.Empty;
    public string ScopeId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExecutorId { get; set; } = string.Empty;
    public string InputSchemaJson { get; set; } = "{}";
    public string RuleRecordIdsJson { get; set; } = "[]";
    public string ContentHash { get; set; } = string.Empty;
    public int Revision { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed record InformationSourceWriteRequest(string Id, string ScopeId, string Name, string Description = "", string MetadataSchemaJson = "{}");
public sealed record InformationRecordWriteRequest(string Id, string SourceId, string Title, string Content, string MetadataJson = "{}");
public sealed record InformationActionContractWriteRequest(string Id, string ScopeId, string Name, string Description, string ExecutorId, string InputSchemaJson, string RuleRecordIdsJson);
public sealed record InformationSourceWriteResult(string Status, InformationSource? Source, string ErrorCode = "", string ErrorMessage = "");
public sealed record InformationRecordWriteResult(string Status, InformationRecord? Record, string ErrorCode = "", string ErrorMessage = "");
public sealed record InformationActionContractWriteResult(string Status, InformationActionContract? Contract, string ErrorCode = "", string ErrorMessage = "");
public sealed record InformationCandidate(string Id, string SourceId, string Title, string Content, string ContentHash, int Revision, int Rank);

public interface IInformationStore
{
    Task<InformationSourceWriteResult> WriteSourceAsync(InformationSourceWriteRequest request, CancellationToken cancellationToken = default);
    Task<InformationRecordWriteResult> WriteRecordAsync(InformationRecordWriteRequest request, CancellationToken cancellationToken = default);
    Task<InformationActionContractWriteResult> WriteActionContractAsync(InformationActionContractWriteRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InformationCandidate>> SearchAsync(string scopeId, string question, IReadOnlyList<string>? sourceIds, int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InformationActionContract>> FindActionContractsAsync(string scopeSelector, CancellationToken cancellationToken = default);
    Task<InformationActionContract?> GetActionContractAsync(string scopeSelector, string contractId, CancellationToken cancellationToken = default);
}

/// <summary>Host-owned access decision for a generic information scope.</summary>
public interface IInformationScopePolicy
{
    Task<InformationScopeResolution> ResolveAsync(string scopeId, CancellationToken cancellationToken = default);
}

public sealed record InformationScopeResolution(bool Granted, string ScopeId, string PolicyRevision)
{
    public static InformationScopeResolution Denied(string scopeId) => new(false, scopeId, "");
}

public sealed record InformationAnswerRequest(string ScopeId, string Question, IReadOnlyList<string>? SourceIds = null, int CandidateLimit = 12);
public sealed record InformationAnswerStatement(string Text, IReadOnlyList<string> Citations);
public sealed record InformationAnswerResult(string Status, IReadOnlyList<InformationAnswerStatement> Statements, IReadOnlyList<string> Unresolved, string ErrorCode = "", string ErrorMessage = "", LocalModelIdentity? Model = null)
{
    public static InformationAnswerResult Denied() => new("denied", [], ["You do not have access to this information scope."], "INFORMATION_SCOPE_DENIED", "Information access was denied.");
    public static InformationAnswerResult Unknown(string code = "INFORMATION_NOT_FOUND", string message = "No supplied information supports the question.") => new("unknown", [], [message], code, message);
}

public interface IInformationAnswerCoordinator
{
    Task<InformationAnswerResult> AnswerAsync(InformationAnswerRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Host adapter for one explicit action-contract executor. It never receives an unvalidated contract input.</summary>
public interface IInformationActionExecutor
{
    string Id { get; }
    Task<InformationActionExecutionResult> ExecuteAsync(InformationActionContract contract, string inputJson, CancellationToken cancellationToken = default);
}

public sealed record InformationActionExecutionRequest(string ScopeSelector, string ContractId, string InputJson);
public sealed record InformationActionExecutionResult(string Status, object? Result = null, string ErrorCode = "", string ErrorMessage = "")
{
    public static InformationActionExecutionResult Rejected(string code, string message) => new("rejected", null, code, message);
}

public interface IInformationActionCoordinator
{
    Task<IReadOnlyList<InformationActionContract>> ListAsync(string scopeSelector, CancellationToken cancellationToken = default);
    Task<InformationActionExecutionResult> ExecuteAsync(InformationActionExecutionRequest request, CancellationToken cancellationToken = default);
}
