using System.Text.RegularExpressions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
using DantesRoleplay.MCPServer.Mcp;
using DantesRoleplay.Procedures;

namespace DantesRoleplay.Tests;

public sealed class BootstrapContractTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Every_bootstrap_file_parses_and_states_what_it_governs()
    {
        var files = ProcedureSeeder.Load();

        Assert.NotEmpty(files);
        Assert.All(files, f =>
        {
            Assert.NotEmpty(f.Id);
            Assert.NotEmpty(f.Category);
            Assert.NotEmpty(f.Instructions);
            // The shipped manual has to model the standard it asks authors to meet.
            Assert.False(string.IsNullOrWhiteSpace(f.Governs), $"{f.Id} does not say what it governs.");
        });
    }

    /// <summary>
    /// The manual is retrieved and followed literally, so a call named in it that does not exist
    /// is not a documentation slip — it is an instruction a session will carry out and fail. The
    /// same sweep covers the seeded rules, whose JavaScript writes error messages that a player's
    /// GM reads back verbatim.
    /// </summary>
    [Fact]
    public void No_seeded_content_names_a_call_that_does_not_exist()
    {
        // One list, kept in GuardTests. Two copies of "what no longer exists" is the same defect
        // this whole test is about, one level up.
        var retired = GuardTests.RetiredVerbsInProse.ToArray();

        var offences = new List<string>();

        foreach (var file in ProcedureSeeder.Load())
        {
            Scan(
                $"{file.Id} (governs)", file.Governs,
                $"{file.Id} (description)", file.Description);
            Scan($"{file.Id} (instructions)", file.Instructions, $"{file.Id} (constraints)", file.Constraints);
        }

        foreach (var rule in MechanicSeeder.Load())
        {
            Scan($"{rule.Id} (source)", rule.Source, $"{rule.Id} (description)", rule.Description);
        }

        Assert.True(
            offences.Count == 0,
            "Seeded content names a call from the retired twelve-tool surface. A session that "
            + "follows it gets a protocol error (VERB_MIGRATION.md D8):\n  "
            + string.Join("\n  ", offences));

        void Scan(string firstLabel, string firstText, string secondLabel, string secondText)
        {
            foreach (var (label, text) in new[] { (firstLabel, firstText), (secondLabel, secondText) })
            {
                foreach (var verb in retired)
                {
                    if (Regex.IsMatch(text ?? string.Empty, $@"\b{verb}\b"))
                    {
                        offences.Add($"{label}: '{verb}'");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Orient tells every cold session to read this contract first. If it is not seeded, the very
    /// first instruction a session receives is one it cannot follow.
    /// </summary>
    [Fact]
    public void The_entry_contract_that_orient_points_at_is_seeded()
    {
        var ids = ProcedureSeeder.Load().Select(file => file.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("procedure.system.use", ids);
    }

    /// <summary>
    /// Every kind the surface offers names contracts that govern it, and a session is told to read
    /// them before committing. A named contract that is not seeded is a dead end at exactly the
    /// moment the session was doing the right thing.
    /// </summary>
    [Fact]
    public void Every_contract_the_surface_names_is_seeded()
    {
        var ids = ProcedureSeeder.Load().Select(file => file.Id).ToHashSet(StringComparer.Ordinal);

        var named = McpVerbCatalog.QueryKinds.SelectMany(k => k.Contracts)
            .Concat(McpVerbCatalog.CommitKinds.SelectMany(k => k.Contracts))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(named);

        var missing = named.Where(id => !ids.Contains(id)).ToList();

        Assert.True(
            missing.Count == 0,
            "query(kind: \"capabilities\") points at contracts that are not seeded:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>
    /// `procedure.system.use` is what orient sends every cold session to first, and it restates
    /// both kind lists in prose. A kind added to the surface but not to that contract is a
    /// capability the first thing a session reads says nothing about.
    /// </summary>
    [Fact]
    public void The_entry_contract_names_every_kind_the_surface_serves()
    {
        var entry = ProcedureSeeder.Load().Single(f => f.Id == "procedure.system.use");
        var text = $"{entry.Description} {entry.Instructions} {entry.Constraints}";

        var missing = McpVerbCatalog.QueryKindNames
            .Concat(McpVerbCatalog.CommitKindNames)
            .Distinct(StringComparer.Ordinal)
            .Where(kind => !text.Contains(kind, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "procedure.system.use does not mention these kinds:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>
    /// The dry run the file path never gets.
    ///
    /// `procedure.contract.create` step 7 says to call with `dryRun: true` and read every named
    /// check — id format, create-or-revise, unknown category, missing governs, near-duplicate.
    /// A contract authored as a bootstrap markdown file goes through the seeder, which calls
    /// `WriteAsync` directly and validates none of that. So the contracts that ship are the only
    /// ones in the system held to a lower standard than the ones an agent writes at runtime.
    /// This runs those checks at build time instead.
    ///
    /// `no-near-duplicate` is excluded: it is the anti-sprawl warning, deliberately crude, and the
    /// manual genuinely contains close neighbours — `system.use` beside `system.inspect`, and the
    /// three mechanic contracts, each pair split on purpose into a caller-facing and a
    /// kernel-facing half. Every other check is a real defect in a shipped contract.
    ///
    /// Note that <see cref="WriteCheck"/> carries no Blocking flag, unlike a mechanic check: the
    /// procedure store reports and writes regardless. So nothing but this test stands between a
    /// malformed id or an empty `governs` and the seeded manual.
    /// </summary>
    [Fact]
    public async Task Every_bootstrap_contract_passes_the_checks_an_agent_would_have_to_pass()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);
        await new ProcedureSeeder(store).SeedAsync();

        var offences = new List<string>();

        foreach (var file in ProcedureSeeder.Load())
        {
            var checks = await store.CheckAsync(new WriteProcedureRequest
            {
                Id = file.Id,
                Category = file.Category,
                Name = file.Name,
                Description = file.Description,
                Governs = file.Governs,
                Instructions = file.Instructions,
                Constraints = file.Constraints,
                CreatedBy = "test"
            });

            offences.AddRange(checks
                .Where(check => !check.Passed && check.Name != "no-near-duplicate")
                .Select(check => $"{file.Id}: [{check.Name}] {check.Detail}"));
        }

        Assert.True(
            offences.Count == 0,
            "Seeded contracts fail checks that an agent writing the same thing through "
            + "commit(kind: \"procedure\") would have been shown:\n  "
            + string.Join("\n  ", offences));
    }

    [Fact]
    public void Mechanic_discovery_and_authoring_contracts_are_embedded()
    {
        var ids = ProcedureSeeder.Load().Select(file => file.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("procedure.mechanic.find", ids);
        Assert.Contains("procedure.mechanic.create", ids);
    }

    [Fact]
    public async Task Seeding_writes_once_and_is_then_idempotent()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);
        var seeder = new ProcedureSeeder(store);

        var first = await seeder.SeedAsync();
        Assert.True(first > 0);

        // A restart with no edits must not manufacture a second identical version.
        Assert.Equal(0, await seeder.SeedAsync());

        var contracts = await store.FindAsync();
        Assert.Equal(first, contracts.Count);
        Assert.All(contracts, c => Assert.Equal(1, c.Version));
    }

    [Fact]
    public async Task An_edited_contract_is_restored_as_a_new_version_rather_than_overwritten()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);
        var seeder = new ProcedureSeeder(store);

        await seeder.SeedAsync();

        var target = (await store.FindAsync())[0];

        // Stand in for the LLM revising a seeded contract through MCP.
        await store.WriteAsync(new WriteProcedureRequest
        {
            Id = target.Id,
            Category = target.Category,
            Name = target.Name,
            Description = target.Description,
            Instructions = "Something different.",
            CreatedBy = "test"
        });

        // The file still says what it said, so the next seed restores it as a NEW version and
        // touches nothing else.
        Assert.Equal(1, await seeder.SeedAsync());
        Assert.Equal(3, (await store.GetVersionsAsync(target.Id)).Count);
    }

    [Fact]
    public void Front_matter_and_sections_are_parsed()
    {
        var parsed = ProcedureFile.Parse(
            """
            ---
            id: procedure.test.example
            category: test
            name: An example
            governs: some_tool, some operation
            status: draft
            ---

            ## Description
            A description.

            ## Instructions
            1. Do the thing.

            ## Constraints
            - Never do the other thing.
            """,
            "inline");

        Assert.Equal("procedure.test.example", parsed.Id);
        Assert.Equal("test", parsed.Category);
        Assert.Equal("some_tool, some operation", parsed.Governs);
        Assert.Equal(ProcedureStatus.Draft, parsed.Status);
        Assert.Equal("A description.", parsed.Description);
        Assert.Contains("Do the thing.", parsed.Instructions);
        Assert.Contains("Never do the other thing.", parsed.Constraints);
    }

    [Fact]
    public void A_file_without_instructions_is_rejected()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ProcedureFile.Parse(
            """
            ---
            id: procedure.test.broken
            category: test
            name: Broken
            ---

            ## Description
            No instructions here.
            """,
            "inline"));

        Assert.Contains("Instructions section is required", error.Message);
    }
}
