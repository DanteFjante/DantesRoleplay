using System.Text.Json;
using DantesRoleplay.Information;
using Json.Schema;

namespace DantesRoleplay.DataAccess;

/// <summary>Authorizes, schema-validates, and routes one declared generic action contract.</summary>
public sealed class InformationActionCoordinator(
    IInformationScopePolicy policy,
    IInformationStore store,
    IEnumerable<IInformationActionExecutor> executors) : IInformationActionCoordinator
{
    public async Task<IReadOnlyList<InformationActionContract>> ListAsync(string scopeSelector, CancellationToken cancellationToken = default)
    {
        if (!InformationScopes.IsSelector(scopeSelector)) return [];
        var access = await policy.ResolveAsync(scopeSelector, cancellationToken);
        if (!access.Granted || !InformationScopes.Contains(access.ScopeId, scopeSelector)) return [];
        return (await store.FindActionContractsAsync(scopeSelector, cancellationToken))
            .Where(contract => InformationScopes.Contains(access.ScopeId, contract.ScopeId))
            .ToArray();
    }

    public async Task<InformationActionExecutionResult> ExecuteAsync(InformationActionExecutionRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || !InformationScopes.IsSelector(request.ScopeSelector) || !Id(request.ContractId) || !Object(request.InputJson, 16_000))
            return InformationActionExecutionResult.Rejected("INVALID_INFORMATION_ACTION", "The scope selector, contract id, or action input is invalid.");
        var access = await policy.ResolveAsync(request.ScopeSelector, cancellationToken);
        if (!access.Granted || !InformationScopes.Contains(access.ScopeId, request.ScopeSelector))
            return InformationActionExecutionResult.Rejected("INFORMATION_SCOPE_DENIED", "Information action access was denied.");
        var contract = await store.GetActionContractAsync(request.ScopeSelector, request.ContractId, cancellationToken);
        if (contract is null) return InformationActionExecutionResult.Rejected("INFORMATION_ACTION_CONTRACT_NOT_FOUND", "No action contract with that id is available in the authorized namespace.");
        if (!InformationScopes.Contains(access.ScopeId, contract.ScopeId)) return InformationActionExecutionResult.Rejected("INFORMATION_SCOPE_DENIED", "The action contract exceeds the authorized information namespace.");
        if (!Matches(contract.InputSchemaJson, request.InputJson)) return InformationActionExecutionResult.Rejected("INFORMATION_ACTION_INPUT_INVALID", "The supplied input does not satisfy the action contract.");
        var executor = executors.SingleOrDefault(x => string.Equals(x.Id, contract.ExecutorId, StringComparison.Ordinal));
        if (executor is null) return InformationActionExecutionResult.Rejected("INFORMATION_ACTION_EXECUTOR_UNAVAILABLE", "The action contract names an executor that this host has not enabled.");
        return await executor.ExecuteAsync(contract, request.InputJson, cancellationToken);
    }

    private static bool Matches(string schemaJson, string inputJson)
    {
        try
        {
            var schema = JsonSchema.FromText(schemaJson,
                new BuildOptions { SchemaRegistry = new SchemaRegistry() });
            using var input = JsonDocument.Parse(inputJson);
            return schema.Evaluate(input.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid;
        }
        catch (Exception exception) when (exception is JsonException or JsonSchemaException)
        {
            return false;
        }
    }

    private static bool Id(string? value) => !string.IsNullOrWhiteSpace(value) && value == value.Trim() && value.Length <= 200 && !value.Any(char.IsWhiteSpace);
    private static bool Object(string? value, int maximum) { try { using var json = JsonDocument.Parse(value ?? ""); return value!.Length <= maximum && json.RootElement.ValueKind == JsonValueKind.Object; } catch { return false; } }
}
