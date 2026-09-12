using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Ecs;
using DantesRoleplay.Interactions;
using DantesRoleplay.SystemTasks;
using DantesRoleplay.SystemTasks.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.SystemCapabilities;

/// <summary>
/// Builds fresh state-scoped hosts for the selected-application gateway, then delegates every
/// procedure and task decision to the existing durable worker owner.
/// </summary>
internal sealed class SelectedApplicationInnerWorkerOwner(
    DantesRoleplayDbContext db,
    IStateSpaceRegistry stateSpaces,
    Func<SystemInnerWorkerService> workerFactory,
    TimeProvider timeProvider) : ISelectedApplicationInnerWorkerOwner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<InteractionInvocationResult> SubmitAsync(
        SystemCapabilityInvocationContext context,
        string commandId,
        SelectedApplicationInnerWorkerSubmission request,
        CancellationToken cancellationToken = default)
    {
        var selection = await HostsAsync(context, request.ApplicationId, request.StateSpaceId,
            StandingGrantCapability.Execute, "inner-worker." + commandId, commandId,
            cancellationToken);
        if (selection.Hosts.Count == 0) return Failure(selection.Code);
        var workers = workerFactory();
        InteractionInvocationResult? result = null;
        foreach (var host in selection.Hosts)
        {
            result = await workers.SubmitEphemeralRootAsync(new(host, request.Procedure,
                request.AssignmentJson, request.ResultSchemaJson, request.DependencyHandles),
                cancellationToken);
            if (result.Tag == InteractionInvocationResultTag.Pending || !Retryable(result.Code))
                return result;
        }
        return result ?? Failure(selection.Code);
    }

    public async Task<InteractionInvocationResult> ReadAsync(
        SystemCapabilityInvocationContext context,
        SelectedApplicationInnerWorkerHandle request,
        CancellationToken cancellationToken = default)
    {
        if (context.ApplicationId is not { IsSystem: false } applicationId)
            return Failure("APPLICATION_CONTEXT_REQUIRED");
        var selection = await HostsAsync(context, applicationId, request.StateSpaceId,
            StandingGrantCapability.ReadTask, ReadCommand(context, request), null,
            cancellationToken);
        if (selection.Hosts.Count == 0) return Failure(selection.Code);
        var workers = workerFactory();
        InteractionInvocationResult? result = null;
        foreach (var host in selection.Hosts)
        {
            result = await workers.GetAsync(host, request.Handle, cancellationToken);
            if (!Retryable(result.Code)) return result;
        }
        return result ?? Failure(selection.Code);
    }

    public async Task<InteractionInvocationResult> CancelAsync(
        SystemCapabilityInvocationContext context,
        string commandId,
        SelectedApplicationInnerWorkerHandle request,
        CancellationToken cancellationToken = default)
    {
        if (context.ApplicationId is not { IsSystem: false } applicationId)
            return Failure("APPLICATION_CONTEXT_REQUIRED");
        var selection = await HostsAsync(context, applicationId, request.StateSpaceId,
            StandingGrantCapability.CancelTask, "inner-worker-cancel." + commandId, null,
            cancellationToken);
        if (selection.Hosts.Count == 0) return Failure(selection.Code);
        var workers = workerFactory();
        InteractionInvocationResult? result = null;
        foreach (var host in selection.Hosts)
        {
            result = await workers.CancelAsync(host, request.Handle, cancellationToken);
            if (!Retryable(result.Code)) return result;
        }
        return result ?? Failure(selection.Code);
    }

    public async Task<InteractionInvocationResult> ListAsync(
        SystemCapabilityInvocationContext context,
        SelectedApplicationInnerWorkerList request,
        CancellationToken cancellationToken = default)
    {
        if (context.ApplicationId is not { IsSystem: false } applicationId)
            return Failure("APPLICATION_CONTEXT_REQUIRED");
        var selection = await HostsAsync(context, applicationId, request.StateSpaceId,
            StandingGrantCapability.ReadTask, ListCommand(context, request), null,
            cancellationToken);
        if (selection.Hosts.Count == 0) return Failure(selection.Code);
        InnerWorkerListCursor? cursor;
        try { cursor = DecodeCursor(request.Cursor); }
        catch (Exception error) when (error is FormatException or JsonException or ArgumentException)
        { return Failure("SYSTEM_INNER_WORKER_CURSOR_INVALID"); }

        var query = db.Set<SystemTaskLifecycleRecord>().AsNoTracking()
            .Where(value => value.Purpose == "procedure-workflow"
                && value.PrincipalReference == context.Principal.PrincipalId
                && value.ApplicationId == applicationId.Value
                && value.StateSpaceId == request.StateSpaceId);
        if (cursor is not null)
            query = query.Where(value => string.Compare(value.CreatedAtUtc, cursor.CreatedAtUtc) < 0
                || value.CreatedAtUtc == cursor.CreatedAtUtc
                    && string.Compare(value.TaskId, cursor.TaskId) < 0);
        var candidates = await query.OrderByDescending(value => value.CreatedAtUtc)
            .ThenByDescending(value => value.TaskId)
            .Select(value => new InnerWorkerListCandidate(value.TaskId, value.CommandId, value.CreatedAtUtc))
            .Take(65).ToArrayAsync(cancellationToken);

        var workers = workerFactory();
        var items = new List<InnerWorkerListItem>();
        InnerWorkerListCandidate? last = null;
        var lastIndex = -1;
        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            var handle = new SystemTaskDurableHandle(candidate.TaskId, candidate.CommandId);
            var result = await ReadWithHostsAsync(workers, selection.Hosts, handle, cancellationToken);
            if (result.Tag == InteractionInvocationResultTag.Unavailable) return result;
            if (result.Tag == InteractionInvocationResultTag.Failed && Retryable(result.Code)) continue;
            last = candidate;
            lastIndex = index;
            items.Add(new(handle, ResultJson(result)));
            if (items.Count == request.PageSize) break;
        }
        if (items.Count < request.PageSize && candidates.Length == 65)
            return InteractionInvocationResult.Unavailable("SYSTEM_INNER_WORKER_LIST_SCAN_LIMIT",
                "The authorized worker page cannot be resolved within its bounded scan.");
        var next = items.Count == request.PageSize && last is not null
            && (lastIndex < candidates.Length - 1 || candidates.Length == 65)
            ? EncodeCursor(new(last.CreatedAtUtc, last.TaskId)) : null;
        var json = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            items,
            nextCursor = next
        }, Json));
        return InteractionInvocationResult.CompletedComputation(json,
            "inner-worker-list." + Hash(json));
    }

    public async Task<InteractionInvocationResult> WaitAsync(
        SystemCapabilityInvocationContext context,
        SelectedApplicationInnerWorkerWait request,
        CancellationToken cancellationToken = default)
    {
        if (context.ApplicationId is not { IsSystem: false } applicationId)
            return Failure("APPLICATION_CONTEXT_REQUIRED");
        var selection = await HostsAsync(context, applicationId, request.Target.StateSpaceId,
            StandingGrantCapability.ReadTask, ReadCommand(context, request.Target), null,
            cancellationToken);
        if (selection.Hosts.Count == 0) return Failure(selection.Code);
        var workers = workerFactory();
        var started = timeProvider.GetUtcNow();
        var requestedEnd = started.AddMilliseconds(request.WaitMilliseconds);
        var deadline = selection.Hosts.Min(value => new DateTimeOffset(value.Budget.DeadlineUtc));
        var end = requestedEnd < deadline ? requestedEnd : deadline;
        InteractionInvocationResult? latest = null;
        var firstRead = true;
        while (true)
        {
            if (!firstRead && timeProvider.GetUtcNow() >= end) return latest!;
            var refreshed = RefreshStateHosts(stateSpaces, selection.Hosts, request.Target.StateSpaceId, applicationId);
            if (refreshed.Count == 0) return Failure("STATE_SPACE_UNAVAILABLE");
            latest = await ReadWithHostsAsync(workers, refreshed, request.Target.Handle, cancellationToken);
            firstRead = false;
            if (latest.Tag != InteractionInvocationResultTag.Pending) return latest;
            var now = timeProvider.GetUtcNow();
            if (now >= end) return latest;
            var delay = end - now;
            if (delay > TimeSpan.FromMilliseconds(100)) delay = TimeSpan.FromMilliseconds(100);
            await Task.Delay(delay, timeProvider, cancellationToken);
        }
    }

    private static async Task<InteractionInvocationResult> ReadWithHostsAsync(
        SystemInnerWorkerService workers,
        IReadOnlyList<InteractionInvocationHost> hosts,
        SystemTaskDurableHandle handle,
        CancellationToken cancellationToken)
    {
        InteractionInvocationResult? result = null;
        foreach (var host in hosts)
        {
            result = await workers.GetAsync(host, handle, cancellationToken);
            if (!Retryable(result.Code)) return result;
        }
        return result ?? Failure("STANDING_GRANT_DENIED");
    }

    internal static IReadOnlyList<InteractionInvocationHost> RefreshStateHosts(
        IStateSpaceRegistry stateSpaces, IReadOnlyList<InteractionInvocationHost> hosts,
        string stateSpaceId, ApplicationIdentifier applicationId)
    {
        StateSpaceView? state;
        try { state = stateSpaces.Get(stateSpaceId); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return []; }
        if (state is null || state.ApplicationRevision.ApplicationId != applicationId) return [];
        return hosts.Select(host => new InteractionInvocationHost(host.Principal, state.ApplicationRevision,
            state.StateSpaceId, host.GrantReference, host.CommandId, InteractionStateRevision.From(state),
            host.Profile, host.Budget, host.ParentCommandId)).ToArray();
    }

    private async Task<(IReadOnlyList<InteractionInvocationHost> Hosts, string Code)> HostsAsync(
        SystemCapabilityInvocationContext context,
        ApplicationIdentifier applicationId,
        string stateSpaceId,
        StandingGrantCapability capability,
        string commandId,
        string? parentCommandId,
        CancellationToken cancellationToken)
    {
        if (!context.Principal.Verified || context.ApplicationId != applicationId)
            return ([], "APPLICATION_CONTEXT_REQUIRED");
        StateSpaceView? state;
        try { state = stateSpaces.Get(stateSpaceId); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { return ([], "STATE_SPACE_UNAVAILABLE"); }
        if (state is null || state.ApplicationRevision.ApplicationId != applicationId)
            return ([], "STATE_SPACE_NOT_AUTHORIZED");

        try
        {
            await using var read = await StandingGrantReadScope.EnterAsync(db, cancellationToken);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var rows = await (from record in db.Set<StandingGrantRevisionRecord>().AsNoTracking()
                              join current in db.Set<StandingGrantCurrentRecord>().AsNoTracking()
                                  on new { record.GrantId, record.Revision }
                                  equals new { current.GrantId, current.Revision }
                              where record.PrincipalReference == context.Principal.PrincipalId
                                  && record.ApplicationId == applicationId.Value
                                  && record.Scope == "stateSpace"
                                  && record.StateSpaceId == stateSpaceId
                                  && !record.Revoked && record.ExpiresAtUtc > now
                              orderby record.GrantId, record.Revision
                              select record).Take(33).ToArrayAsync(cancellationToken);
            if (rows.Length > 32) return ([], "STANDING_GRANT_CANDIDATES_UNAVAILABLE");
            var grants = rows
                .Where(value => Encoding.UTF8.GetByteCount(value.PermissionsJson) <= 16_000)
                .Select(SqliteStandingGrantPolicy.Parse)
                .Where(value => value.Capabilities.Contains(capability) && value.MaximumOperations >= 1)
                .OrderBy(value => value.Definitions.Mode == StandingGrantDefinitionMode.ExactIds ? 0 : 1)
                .ThenBy(value => value.GrantId, StringComparer.Ordinal)
                .ThenBy(value => value.Revision)
                .ToArray();
            if (grants.Length == 0) return ([], "STANDING_GRANT_DENIED");
            var hosts = grants.Select(grant =>
            {
                var deadline = grant.ExpiresAtUtc;
                var maximum = now.AddMinutes(10);
                if (deadline > maximum) deadline = maximum;
                return new InteractionInvocationHost(context.Principal, state.ApplicationRevision,
                    state.StateSpaceId, grant.GrantReference, commandId,
                    InteractionStateRevision.From(state), InteractionExecutionProfile.Workflow,
                    new InteractionInvocationBudget(Math.Min(grant.MaximumOperations,
                        StandingGrantLimits.MaximumOperations), deadline), parentCommandId);
            }).ToArray();
            return (Array.AsReadOnly(hosts), "STANDING_GRANT_SELECTED");
        }
        catch (OperationCanceledException) { throw; }
        catch { return ([], "STANDING_GRANT_CANDIDATES_UNAVAILABLE"); }
    }

    private static bool Retryable(string code) => code is
        "SYSTEM_TASK_NOT_AUTHORIZED" or
        "STANDING_GRANT_DENIED" or
        "STANDING_GRANT_TARGET_DENIED" or
        "STANDING_GRANT_NOT_CURRENT" or
        "STANDING_GRANT_INVALID";

    private static InteractionInvocationResult Failure(string code) =>
        code.Contains("UNAVAILABLE", StringComparison.Ordinal)
            ? InteractionInvocationResult.Unavailable(code,
                "The selected-application worker owner is temporarily unavailable.")
            : InteractionInvocationResult.Failed(code,
                "The selected-application worker request is not authorized in this scope.");

    private static string ReadCommand(SystemCapabilityInvocationContext context,
        SelectedApplicationInnerWorkerHandle request) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(
            "dantes-roleplay/inner-worker-read/v1\n" + context.Principal.PrincipalId + "\n"
            + context.ApplicationId!.Value + "\n" + request.StateSpaceId + "\n"
            + request.Handle.TaskId + "\n" + request.Handle.CommandId + "\n"
            + context.CorrelationId)))[..32];

    private static string ListCommand(SystemCapabilityInvocationContext context,
        SelectedApplicationInnerWorkerList request) => Hash(
        "dantes-roleplay/inner-worker-list/v1\n" + context.Principal.PrincipalId + "\n"
        + context.ApplicationId!.Value + "\n" + request.StateSpaceId + "\n" + context.CorrelationId)[..32];

    private static string Hash(string value) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static JsonElement ResultJson(InteractionInvocationResult result)
    {
        using var document = JsonDocument.Parse(result.ToJson());
        return document.RootElement.Clone();
    }

    private static string EncodeCursor(InnerWorkerListCursor cursor) => Convert.ToBase64String(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cursor, Json)))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static InnerWorkerListCursor? DecodeCursor(string? value)
    {
        if (value is null) return null;
        if (value.Length is 0 or > 1_024) throw new FormatException();
        var encoded = value.Replace('-', '+').Replace('_', '/');
        encoded = encoded.PadRight(encoded.Length + ((4 - encoded.Length % 4) % 4), '=');
        var cursor = JsonSerializer.Deserialize<InnerWorkerListCursor>(Convert.FromBase64String(encoded), Json)
            ?? throw new JsonException();
        if (string.IsNullOrWhiteSpace(cursor.CreatedAtUtc) || cursor.CreatedAtUtc.Length > 64
            || string.IsNullOrWhiteSpace(cursor.TaskId) || cursor.TaskId.Length > InteractionContractLimits.Identifier)
            throw new ArgumentException("The worker list cursor is invalid.");
        return cursor;
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record InnerWorkerListCursor(string CreatedAtUtc, string TaskId);
    private sealed record InnerWorkerListCandidate(string TaskId, string CommandId, string CreatedAtUtc);
    private sealed record InnerWorkerListItem(SystemTaskDurableHandle Handle, JsonElement Result);
}
