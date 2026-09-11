using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.Web.Data;

/// <summary>Per-render values and original shared outcomes. Never cache this audience-specific object.</summary>
public sealed record CompositionQueryValues(
    IReadOnlyDictionary<string, JsonElement> Values,
    IReadOnlyDictionary<string, InteractionInvocationResult> Results);

/// <summary>
/// Materializes host-selected query requests only. Publication/transport integration must resolve
/// declarations to exact contracts and trusted authority before calling this presentation adapter.
/// No authored JSON is converted into a host, grant, role binding, or execution request here.
/// </summary>
public sealed class CompositionQueryMaterializer(IApplicationReadModelInvocationAdapter reads)
{
    public async Task<CompositionQueryValues> ReadAsync(
        InteractionInvocationHost host,
        IReadOnlyDictionary<string, ApplicationReadModelInvocationRequest> bindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(bindings);
        if (host.Profile != InteractionExecutionProfile.ReadOnly)
            throw new ArgumentException("Composition rendering requires a read-only host.", nameof(host));
        if (bindings.Count > 16)
            throw new ArgumentException("A render supports at most 16 query bindings.", nameof(bindings));

        // Freeze and validate the entire selection before the first read. The one host also
        // ensures all bindings share the selected application, audience scope, and root budget.
        var selected = bindings.Select(pair =>
        {
            var request = pair.Value;
            if (pair.Key.Length is < 1 or > 80 ||
                pair.Key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-') ||
                request is null || !ReferenceEquals(host, request.Host) || request.PageSize is < 1 or > 100 ||
                request.Cursor?.Length > 1024)
                throw new ArgumentException("A query binding has an invalid name, scope, or page bound.", nameof(bindings));
            return new KeyValuePair<string, ApplicationReadModelInvocationRequest>(pair.Key, request with
            {
                RoleBindings = new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(InteractionInvocationRoles.Normalize(request.RoleBindings), StringComparer.Ordinal)),
                InputJson = InteractionCanonicalJson.CanonicalizeObject(request.InputJson),
                PageSize = request.PageSize ?? 100
            });
        }).ToArray();

        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var results = new Dictionary<string, InteractionInvocationResult>(StringComparer.Ordinal);
        var bytes = 0;
        foreach (var (name, request) in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InteractionInvocationResult result;
            try { result = await reads.ReadAsync(request, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch
            {
                result = InteractionInvocationResult.Unavailable("READ_ADAPTER_UNAVAILABLE", "The registered read is unavailable.");
            }
            if (result.Tag == InteractionInvocationResultTag.Completed && result.ReadEvidence is not null)
            {
                bytes += Encoding.UTF8.GetByteCount(result.DataJson!);
                if (bytes > 1024 * 1024)
                    result = InteractionInvocationResult.Unavailable("COMPOSITION_DATA_LIMIT", "The page data exceeds the render limit.");
                else
                {
                    using var data = JsonDocument.Parse(result.DataJson!);
                    values.Add(name, data.RootElement.Clone());
                }
            }
            else if (result.Tag is InteractionInvocationResultTag.Completed or InteractionInvocationResultTag.Committed
                     or InteractionInvocationResultTag.Pending or InteractionInvocationResultTag.Proposed)
                result = InteractionInvocationResult.Unavailable("COMPOSITION_READ_REQUIRED", "The page requires a completed registered read with read evidence.");
            results.Add(name, result);
        }
        // A denied/incomplete generation must not accidentally render partial values as complete.
        if (results.Values.Any(result => result.Tag != InteractionInvocationResultTag.Completed)) values.Clear();
        return new(new ReadOnlyDictionary<string, JsonElement>(values),
            new ReadOnlyDictionary<string, InteractionInvocationResult>(results));
    }
}
