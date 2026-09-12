using System.Text.RegularExpressions;
using DantesRoleplay.Interactions;

namespace DantesRoleplay.SystemCapabilities;

internal enum SystemInnerWorkerGovernedReferenceKind { Action, Query, SystemCapability }
internal sealed record SystemInnerWorkerGovernedReference(
    SystemInnerWorkerGovernedReferenceKind Kind, string QualifiedId);

/// <summary>Extracts only complete literal application references from a procedure governs clause.</summary>
internal static partial class SystemInnerWorkerGovernedReferences
{
    internal static IReadOnlyList<SystemInnerWorkerGovernedReference> Parse(string governs)
    {
        if (string.IsNullOrWhiteSpace(governs) || governs.Length > InteractionContractLimits.SafeEvidenceText)
            return [];
        var values = ActionReference().Matches(governs).Select(match =>
                new SystemInnerWorkerGovernedReference(SystemInnerWorkerGovernedReferenceKind.Action,
                    match.Groups[1].Value))
            .Concat(QueryReference().Matches(governs).Select(match =>
                new SystemInnerWorkerGovernedReference(SystemInnerWorkerGovernedReferenceKind.Query,
                    match.Groups[1].Value)))
            .Concat(SystemCapabilityReference().Matches(governs).Select(match =>
                new SystemInnerWorkerGovernedReference(SystemInnerWorkerGovernedReferenceKind.SystemCapability,
                    match.Groups[1].Value)))
            .Where(value => value.QualifiedId.Contains('.', StringComparison.Ordinal))
            .Distinct().OrderBy(value => value.Kind).ThenBy(value => value.QualifiedId, StringComparer.Ordinal)
            .ToArray();
        if (values.Length > 16)
            throw new InteractionContractException("INNER_WORKER_GOVERNED_REFERENCES_EXCEEDED",
                "The procedure governs more explicit application tools than the focused worker can admit.");
        return Array.AsReadOnly(values);
    }

    [GeneratedRegex(@"(?:(?<=^)|(?<=[;,]))\s*execute\s+([a-z0-9][a-z0-9._-]{2,159})\s*(?=$|[;,])",
        RegexOptions.CultureInvariant)]
    private static partial Regex ActionReference();

    [GeneratedRegex("query\\(kind:\\s*\"([a-z0-9][a-z0-9._-]{2,159})\"\\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex QueryReference();

    [GeneratedRegex(@"(?:(?<=^)|(?<=[;,]))\s*system capability\s+([a-z0-9][a-z0-9._-]{2,159})\s*(?=$|[;,])",
        RegexOptions.CultureInvariant)]
    private static partial Regex SystemCapabilityReference();
}
