using DantesRoleplay.Events;
using DantesRoleplay.Operations;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>Registry handlers only. Slice 2 intentionally does not route middleware.</summary>
public sealed class SubscriptionTools
{
    public async Task<ToolEnvelope> FindAsync(ISubscriptionStore store, IOperationLog log, string? id, int? version, string? query, string? category, string? scope, bool includeInactive, int? limit, CancellationToken cancellationToken) =>
        await ToolRunner.RunAsync(log, "find_subscriptions", async () =>
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                var item = await store.GetAsync(id, version, cancellationToken);
                return item is null
                    ? ToolOutcome.Fail("UNKNOWN_SUBSCRIPTION", $"There is no subscription '{id}' at the requested version.", "query(kind: \"subscriptions\")", $"Subscription '{id}' not found.")
                    : ToolOutcome.OkAbout(id, item, $"Read subscription '{id}' v{item.Version}.", "Subscriptions are registrations only until guard/reaction execution lands.");
            }
            var items = await store.FindAsync(query, category, scope, includeInactive, limit ?? 50, cancellationToken);
            return ToolOutcome.Ok(new { Subscriptions = items }, $"Found {items.Count} subscription(s).", items.Count == 0 ? VerbSurface.CommitCall("subscription", true) : $"query(kind: \"subscriptions\", id: \"{items[0].Id}\")");
        });

    public async Task<ToolEnvelope> WriteAsync(ISubscriptionStore store, IOperationLog log, WriteSubscriptionRequest request, string intent, string[]? proceduresUsed, bool dryRun, CancellationToken cancellationToken) =>
        await ToolRunner.RunAsync(log, "write_subscription", intent, request.Id, proceduresUsed, async () =>
        {
            var checks = await store.CheckAsync(request, cancellationToken);
            var failed = checks.FirstOrDefault(x => x.Blocking && !x.Passed);
            if (failed?.Name == "mode-immutable") return ToolOutcome.Fail("MODE_IMMUTABLE", failed.Detail, VerbSurface.CommitCall("subscription", dryRun: true), $"Rejected mode revision for '{request.Id}'.");
            if (dryRun) return ToolOutcome.Ok(new { Checks = checks, CanWrite = failed is null }, $"Dry run for subscription '{request.Id}'.", VerbSurface.CommitCall("subscription", request.Id));
            if (failed is not null) return ToolOutcome.Fail("INVALID_SUBSCRIPTION", failed.Detail, VerbSurface.CommitCall("subscription", request.Id, true), $"Rejected subscription '{request.Id}'.");
            var result = await store.WriteAsync(request, cancellationToken);
            return ToolOutcome.OkAbout(request.Id, result.Subscription, result.Created ? $"Created subscription '{request.Id}'." : $"Created version {result.Subscription.Version} of subscription '{request.Id}'.", "The registration is stored but middleware does not execute yet.");
        }, consumesReadEvidence: !dryRun);
}
