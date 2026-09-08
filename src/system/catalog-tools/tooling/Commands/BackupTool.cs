namespace DantesRoleplay.Tools.Commands;

public sealed class BackupTool : ITool
{
    public string Name => "backup";

    public string Summary => "Create and verify a consistent read-only backup of the runtime database.";

    public string Usage => """
        roleplay backup [--database <path>] [--output <path>]

        Uses SQLite's online backup API to create a transactionally consistent copy without
        migrating, importing, or otherwise changing the source database. The backup must not
        already exist and is verified with SQLite integrity_check before success is reported.
        """;

    public Task<int> RunAsync(ToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = DatabaseBackup.Create(context.DatabasePath, context.Option("output"));
        context.Out.WriteLine($"Backup: {path}");
        context.Out.WriteLine("Verified: SQLite integrity_check passed. The source database was not changed.");
        return Task.FromResult(0);
    }
}
