using DantesRoleplay.SystemTasks;

namespace DantesRoleplay.Web.Hosting;

internal sealed class UnavailableSystemTaskService : ISystemTaskService
{
    private static SystemTaskException Error() => new("SYSTEM_TASK_UNAVAILABLE", "System tasks are unavailable in this host.");
    public Task<SystemTaskDocument> PrepareAsync(SystemTaskRequestContext context, string conversationId, SystemTaskPrepareRequest request, CancellationToken cancellationToken = default) => throw Error();
    public Task<SystemTaskDocument?> GetAsync(SystemTaskRequestContext context, string taskId, CancellationToken cancellationToken = default) => throw Error();
    public Task<IReadOnlyList<SystemTaskSummary>> ListAsync(SystemTaskRequestContext context, string conversationId, DateTime? beforeCreatedAtUtc, string? beforeId, int limit, CancellationToken cancellationToken = default) => throw Error();
    public Task<SystemTaskConfirmationDocument> ConfirmAsync(SystemTaskRequestContext context, string taskId, SystemTaskConfirmationRequest request, CancellationToken cancellationToken = default) => throw Error();
    public Task<SystemTaskExecutionDocument> ExecuteAsync(SystemTaskRequestContext context, string taskId, SystemTaskExecutionRequest request, CancellationToken cancellationToken = default) => throw Error();
}
