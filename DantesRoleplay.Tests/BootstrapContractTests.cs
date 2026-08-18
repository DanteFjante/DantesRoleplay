using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Bootstrap;
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
