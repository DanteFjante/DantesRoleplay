using System.ComponentModel;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using ModelContextProtocol.Server;

namespace DantesRoleplay.MCPServer.Tools;

/// <summary>
/// The procedure-contract surface: three tools covering list/search, read, and write.
///
/// List and search are deliberately one tool. §7.5 prefers progressive disclosure over
/// enumeration, and with a few dozen contracts an omitted query returning everything is the
/// common, correct case.
/// </summary>
[McpServerToolType]
public sealed class ProcedureTools
{
    [McpServerTool(Name = "find_procedures")]
    [Description(
        "List or search procedure contracts — the operating manual for this system. Call with " +
        "no arguments to see everything; there are few enough that reading the list is usually " +
        "better than guessing a search term. Each result states what it GOVERNS, so you can " +
        "match a contract to the operation you are about to perform instead of inferring it " +
        "from the title. Returns summaries only; use get_procedure for the full instructions.")]
    public async Task<ToolEnvelope> FindProceduresAsync(
        IProcedureStore procedures,
        IOperationLog log,
        [Description("Substring matched against id, name, description and governs. Omit to list everything.")]
        string? query = null,
        [Description("Restrict to one category. Call orient() for the categories that exist.")]
        string? category = null,
        [Description("Include deprecated and archived contracts. Default false.")]
        bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        await ToolRunner.RunAsync(log, "find_procedures", async () =>
        {
            var results = await procedures.FindAsync(
                query, category, includeInactive, cancellationToken: cancellationToken);

            if (results.Count == 0)
            {
                var all = await procedures.FindAsync(cancellationToken: cancellationToken);

                // §7.4: never leave the model at a dead end. An empty filtered result is the
                // classic point where an agent wrongly concludes something does not exist.
                return ToolOutcome.Ok(
                    new { Procedures = results, TotalWithoutFilters = all.Count },
                    $"No procedures matched (query: '{query}', category: '{category}').",
                    all.Count > 0
                        ? "find_procedures() — clear the filters; there are procedures, just not matching this."
                        : "history(failuresOnly: true) — no procedures exist at all, which is unexpected; check whether seeding failed.");
            }

            return ToolOutcome.Ok(
                new { Procedures = results },
                $"Found {results.Count} procedure(s).",
                $"get_procedure(id: \"{results[0].Id}\") — read one in full before acting on it.");
        });

    [McpServerTool(Name = "get_procedure")]
    [Description(
        "Read one procedure contract in full: what it governs, its instructions and its " +
        "constraints. Follow it before performing the operation it governs. Reads are recorded, " +
        "and a later write reports which procedures you actually opened — so reading the " +
        "relevant contract is both the right thing and the visible thing. Pass a version number " +
        "to read a historical revision.")]
    public async Task<ToolEnvelope> GetProcedureAsync(
        IProcedureStore procedures,
        IOperationLog log,
        [Description("Contract id, e.g. \"procedure.system.modify\".")] string id,
        [Description("Version to read. Omit for the current one.")] int? version = null,
        CancellationToken cancellationToken = default) =>
        await ToolRunner.RunAsync(log, "get_procedure", async () =>
        {
            var procedure = await procedures.GetAsync(id, version, cancellationToken);

            if (procedure is null)
            {
                var exists = await procedures.ExistsAsync(id, cancellationToken);

                return exists
                    ? ToolOutcome.Fail(
                        "UNKNOWN_VERSION",
                        $"Procedure '{id}' exists but has no version {version}.",
                        $"get_procedure(id: \"{id}\") — omit the version to read the current one.",
                        $"Version {version} of '{id}' not found.")
                    : ToolOutcome.Fail(
                        "UNKNOWN_PROCEDURE",
                        $"There is no procedure with id '{id}'.",
                        "find_procedures() — list what does exist, then retry with a real id.",
                        $"Procedure '{id}' not found.");
            }

            var nextSteps = new List<string>();

            if (procedure.Version < procedure.LatestVersion)
            {
                nextSteps.Add(
                    $"You are reading version {procedure.Version} of {procedure.LatestVersion}. " +
                    $"get_procedure(id: \"{id}\") returns the current one.");
            }

            nextSteps.Add("Follow the instructions, then perform the operation they govern.");
            nextSteps.Add(
                $"write_procedure(id: \"{id}\", ..., dryRun: true) — only if following it revealed that it is wrong or incomplete.");

            // Recording the subject is what lets a later write report what was demonstrably read.
            return ToolOutcome.OkAbout(
                id,
                procedure,
                $"Read {id} v{procedure.Version}.",
                [.. nextSteps]);
        });

    [McpServerTool(Name = "write_procedure")]
    [Description(
        "Create a procedure contract, or revise an existing one. Writing an id that already " +
        "exists APPENDS a new version and never overwrites. Read procedure.contract.create " +
        "first — it governs this tool. ALWAYS call with dryRun: true first: it returns a list " +
        "of named checks (id format, duplicate detection, unknown category, missing governs) so " +
        "you can see exactly what was validated before committing. Note that ids are permanent: " +
        "there is no rename and no delete, only status deprecated or archived.")]
    public async Task<ToolEnvelope> WriteProcedureAsync(
        IProcedureStore procedures,
        IOperationLog log,
        [Description("Dotted id, e.g. \"procedure.world.model\". PERMANENT once chosen — no rename, no delete.")]
        string id,
        [Description("Category for browsing. Reuse an existing one where you can; orient() lists them.")]
        string category,
        [Description("Short human title.")] string name,
        [Description("One or two sentences. This is what appears in listings, so keep it tight.")]
        string description,
        [Description("The procedure itself, usually numbered steps. Markdown.")]
        string instructions,
        [Description(
            "Which tools or operations this contract applies to, e.g. \"write_procedure\" or " +
            "\"adding a component definition\". Strongly recommended: without it, a later agent " +
            "cannot tell whether this contract is the relevant one.")]
        string governs = "",
        [Description("Things that must NOT happen. Keep separate from instructions.")]
        string constraints = "",
        [Description("One of: draft, active, deprecated, archived. Defaults to active on create.")]
        string? status = null,
        [Description("Why this revision exists. Expected when revising.")]
        string changeNote = "",
        [Description("What you were trying to achieve, in your own words. Goes in the audit log.")]
        string intent = "",
        [Description(
            "Ids of procedures you consulted. The system also records which procedures you " +
            "demonstrably read, and stores both, so cite honestly rather than exhaustively.")]
        string[]? proceduresUsed = null,
        [Description("Validate and report named checks without writing. Do this first.")]
        bool dryRun = false,
        CancellationToken cancellationToken = default) =>
        // A dry run validates; it does not act. Letting it consume read evidence meant the dry
        // run reported the procedures read and the real commit that followed reported none,
        // which then showed up as CitedWithoutReading — the audit accusing the agent of exactly
        // the thing it had just done correctly.
        await ToolRunner.RunAsync(log, "write_procedure", intent, id, proceduresUsed, async () =>
        {
            ProcedureStatus? parsedStatus = null;

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<ProcedureStatus>(status, ignoreCase: true, out var value))
                {
                    return ToolOutcome.Fail(
                        "INVALID_STATUS",
                        $"'{status}' is not a status.",
                        "Retry with status omitted, or one of: draft, active, deprecated, archived.",
                        $"Rejected write to '{id}': bad status.");
                }

                parsedStatus = value;
            }

            var request = new WriteProcedureRequest
            {
                Id = id,
                Category = category,
                Name = name,
                Description = description,
                Governs = governs,
                Instructions = instructions,
                Constraints = constraints,
                Status = parsedStatus,
                CreatedBy = "llm",
                ChangeNote = changeNote
            };

            var checks = await procedures.CheckAsync(request, cancellationToken);
            var read = await log.RecentlyReadProceduresAsync(cancellationToken);

            if (dryRun)
            {
                return ToolOutcome.Ok(
                    new
                    {
                        Checks = checks,
                        ProceduresYouDemonstrablyRead = read,
                        ProceduresYouCited = proceduresUsed ?? [],
                        Committed = false
                    },
                    $"Dry run for '{id}': {checks.Count(c => c.Passed)}/{checks.Count} checks passed, nothing written.",
                    checks.Any(c => !c.Passed)
                        ? "Address the failing checks above, then call again with dryRun omitted."
                        : $"write_procedure(id: \"{id}\", ...) with dryRun omitted to commit this.");
            }

            var result = await procedures.WriteAsync(request, cancellationToken);
            var verb = result.Created ? "Created" : "Revised";

            return ToolOutcome.Ok(
                new { result.Procedure, result.Created, Checks = checks, ProceduresYouDemonstrablyRead = read },
                $"{verb} {id} v{result.Procedure.Version}.",
                $"get_procedure(id: \"{id}\") — confirm it reads the way you intended.",
                "find_procedures() — check you have not created a near-duplicate.");
        }, consumesReadEvidence: !dryRun);
}
