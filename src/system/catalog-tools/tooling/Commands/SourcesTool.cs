using DantesRoleplay.Applications;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Tools.Commands;

/// <summary>
/// Lists and retires an application's registered sources.
///
/// A source ID is permanent and its path specification immutable, which is right: resolution
/// order has to be stable. The consequence is that when a migration relocates catalog content,
/// the registrations pointing at the old paths cannot be corrected — they sit there reporting
/// SCAN_PATH_NOT_FOUND, which makes the default application preview invalid, which forces every
/// activation to hand-assemble the list of sources that still resolve. Seven of those accumulated
/// in this repository before anyone noticed the cost, because each one is individually harmless.
///
/// Retiring withdraws a registration from resolution and records when and why. It is not deletion:
/// the row stays, and `list --retired` is how an operator answers "where did that source go".
/// </summary>
public sealed class SourcesTool : ITool
{
    public string Name => "sources";

    public string Summary => "List an application's registered sources, or retire one that no longer resolves.";

    public string Usage => """
        roleplay sources list --application <id> [--retired] [--database <path>]
        roleplay sources retire <sourceId> --application <id> --reason "<why>" [--database <path>]

        list      Shows every source that still takes part in resolution, with its allowed root and
                  relative path or glob. --retired shows the withdrawn ones instead, with the date
                  and the reason recorded for each.

        retire    Withdraws one registration from resolution. Use it when a source's files have
                  moved and a replacement registration already covers them — retiring the old one
                  is what makes `query(kind: "system.application-preview")` valid again, and an
                  invalid preview cannot be activated.

                  A retired ID is not reusable. Registering over it is refused rather than
                  silently resurrecting a registration somebody withdrew on purpose.

        Options:
          --application  Required. The registered application the source belongs to.
          --reason       Required for retire. Up to 500 characters, kept with the row.
        """;

    public async Task<int> RunAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var verb = context.Arguments.FirstOrDefault();
        if (verb is not ("list" or "retire"))
        {
            context.Error.WriteLine("Expected `roleplay sources list` or `roleplay sources retire <sourceId>`.");
            return 2;
        }

        var applicationId = context.Option("application");
        if (applicationId is null)
        {
            context.Error.WriteLine("--application is required.");
            return 2;
        }

        await using var db = context.OpenDatabase();
        var registry = new SqliteSourceRegistry(db);
        ApplicationIdentifier application;
        try
        {
            application = ApplicationIdentifier.Parse(applicationId);
        }
        catch (Exception error)
        {
            context.Error.WriteLine(error.Message);
            return 2;
        }

        return verb == "list"
            ? List(context, registry, application)
            : Retire(context, registry, application);
    }

    private static int List(ToolContext context, ISourceRegistry registry, ApplicationIdentifier application)
    {
        if (context.HasFlag("retired"))
        {
            var retired = registry.Retired(application);
            if (retired.Count == 0)
            {
                context.Out.WriteLine($"No retired sources for '{application.Value}'.");
                return 0;
            }

            foreach (var entry in retired)
            {
                context.Out.WriteLine($"  {entry.Source.SourceId}");
                context.Out.WriteLine($"      {entry.Source.AllowedRootId}: {entry.Source.RelativePathOrGlob}");
                context.Out.WriteLine($"      retired {entry.RetiredAtUtc:yyyy-MM-dd} — {entry.Reason}");
            }

            context.Out.WriteLine();
            context.Out.WriteLine($"{retired.Count} retired source(s).");
            return 0;
        }

        var active = registry.For(application);
        foreach (var source in active)
            context.Out.WriteLine($"  {source.SourceId,-38} {source.AllowedRootId}: {source.RelativePathOrGlob}");

        context.Out.WriteLine();
        context.Out.WriteLine($"{active.Count} source(s) taking part in resolution.");
        return 0;
    }

    private static int Retire(ToolContext context, ISourceRegistry registry, ApplicationIdentifier application)
    {
        var sourceId = context.Arguments.Skip(1).FirstOrDefault();
        if (sourceId is null)
        {
            context.Error.WriteLine("Expected a source ID: `roleplay sources retire <sourceId> --application <id> --reason \"...\"`.");
            return 2;
        }

        var reason = context.Option("reason");
        if (reason is null)
        {
            context.Error.WriteLine("--reason is required. A withdrawn registration with no recorded reason is a mystery for the next person.");
            return 2;
        }

        try
        {
            var retired = registry.Retire(application, sourceId, reason);
            context.Out.WriteLine($"Retired '{retired.Source.SourceId}' ({retired.Source.RelativePathOrGlob}).");
            context.Out.WriteLine($"Reason: {retired.Reason}");
            context.Out.WriteLine("It no longer takes part in resolution. Re-run the application preview to confirm it is valid.");
            return 0;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            context.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
