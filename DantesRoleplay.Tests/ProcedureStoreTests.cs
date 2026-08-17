using DantesRoleplay.DataAccess;
using DantesRoleplay.Procedures;

namespace DantesRoleplay.Tests;

public sealed class ProcedureStoreTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static WriteProcedureRequest Request(
        string id = "procedure.system.modify",
        string name = "Modify the application",
        string description = "How the AI should modify the application.",
        string instructions = "1. Inspect the relevant subsystem.",
        string category = "system",
        string governs = "changing C# code") =>
        new()
        {
            Id = id,
            Category = category,
            Name = name,
            Description = description,
            Instructions = instructions,
            Governs = governs,
            CreatedBy = "test"
        };

    [Fact]
    public async Task Write_creates_a_contract_at_version_1()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);

        var result = await store.WriteAsync(Request());

        Assert.True(result.Created);
        Assert.Equal(1, result.Procedure.Version);
        Assert.Equal("procedure.system.modify", result.Procedure.Id);
        Assert.Equal(ProcedureStatus.Active, result.Procedure.Status);
        Assert.Equal("changing C# code", result.Procedure.Governs);
    }

    [Fact]
    public async Task Writing_an_existing_id_appends_a_version_rather_than_overwriting()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);

        await store.WriteAsync(Request(instructions: "original"));
        var second = await store.WriteAsync(Request(instructions: "revised"));

        Assert.False(second.Created);
        Assert.Equal(2, second.Procedure.Version);

        var original = await store.GetAsync("procedure.system.modify", version: 1);
        Assert.NotNull(original);
        Assert.Equal("original", original!.Instructions);

        var live = await store.GetAsync("procedure.system.modify");
        Assert.Equal("revised", live!.Instructions);
        Assert.Equal(2, live.LatestVersion);
    }

    [Fact]
    public async Task Get_returns_null_for_an_unknown_contract_or_version()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);

        await store.WriteAsync(Request());

        Assert.Null(await store.GetAsync("procedure.nope"));
        Assert.Null(await store.GetAsync("procedure.system.modify", version: 7));
    }

    [Fact]
    public async Task Find_with_no_query_lists_everything()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);

        await store.WriteAsync(Request(id: "procedure.system.modify"));
        await store.WriteAsync(Request(id: "procedure.contract.create", category: "contracts"));

        Assert.Equal(2, (await store.FindAsync()).Count);
    }

    [Fact]
    public async Task Find_matches_on_id_name_description_and_governs()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);

        await store.WriteAsync(Request(id: "procedure.system.modify", name: "Modify the application"));
        await store.WriteAsync(Request(
            id: "procedure.contract.create",
            name: "Create a contract",
            description: "How to author a new procedure contract.",
            category: "contracts",
            governs: "write_procedure"));

        Assert.Single(await store.FindAsync("Modify"));
        Assert.Single(await store.FindAsync("author"));
        Assert.Single(await store.FindAsync("contract.create"));

        // Searching by the operation you are about to perform is the point of the governs field.
        Assert.Single(await store.FindAsync("write_procedure"));
        Assert.Empty(await store.FindAsync("nothing matches this"));
    }

    [Fact]
    public async Task Find_filters_by_category()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);

        await store.WriteAsync(Request(id: "procedure.system.modify"));
        await store.WriteAsync(Request(id: "procedure.contract.create", category: "contracts"));

        var contracts = await store.FindAsync(category: "contracts");

        Assert.Single(contracts);
        Assert.Equal("procedure.contract.create", contracts[0].Id);
    }

    [Fact]
    public async Task Find_hides_archived_contracts_unless_asked()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);

        await store.WriteAsync(Request());
        await store.WriteAsync(Request() with { Status = ProcedureStatus.Archived, ChangeNote = "retired" });

        Assert.Empty(await store.FindAsync());
        Assert.Single(await store.FindAsync(includeInactive: true));
    }

    [Fact]
    public async Task Search_treats_underscore_literally_rather_than_as_a_wildcard()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);

        await store.WriteAsync(Request(id: "procedure.system.modify", name: "Modify", governs: "code"));

        // Without LIKE escaping, "sy_tem" would match "system" and this would wrongly return 1.
        Assert.Empty(await store.FindAsync("sy_tem"));
    }

    [Fact]
    public async Task Version_numbers_never_repeat_even_after_a_status_change()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);

        await store.WriteAsync(Request());
        await store.WriteAsync(Request() with { Status = ProcedureStatus.Deprecated });
        var third = await store.WriteAsync(Request() with { Status = ProcedureStatus.Active });

        Assert.Equal(3, third.Procedure.Version);
        Assert.Equal(3, (await store.GetVersionsAsync("procedure.system.modify")).Count);
    }

    [Fact]
    public async Task Categories_reports_counts_for_orientation()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);

        await store.WriteAsync(Request(id: "procedure.system.modify"));
        await store.WriteAsync(Request(id: "procedure.system.inspect", name: "Inspect"));
        await store.WriteAsync(Request(id: "procedure.contract.create", name: "Create", category: "contracts"));

        var categories = await store.GetCategoriesAsync();

        Assert.Equal(2, categories.Count);
        Assert.Equal("contracts", categories[0].Category);
        Assert.Equal(1, categories[0].Count);
        Assert.Equal("system", categories[1].Category);
        Assert.Equal(2, categories[1].Count);
    }

    [Fact]
    public async Task Find_matches_a_multi_word_query()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);

        await store.WriteAsync(Request(
            id: "procedure.contract.create",
            name: "Create or revise a procedure contract",
            description: "How to author the instructions that govern this system.",
            category: "contracts",
            governs: "write_procedure"));
        await store.WriteAsync(Request(id: "procedure.system.modify"));

        // Cold-walk finding: whole-phrase matching meant "create contract" found nothing while
        // "contract" alone worked. A natural query has to work.
        var results = await store.FindAsync("create contract");

        Assert.NotEmpty(results);
        Assert.Equal("procedure.contract.create", results[0].Id);
    }

    [Fact]
    public async Task Find_ranks_by_how_many_query_words_match()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);

        await store.WriteAsync(Request(
            id: "procedure.contract.create",
            name: "Create or revise a procedure contract",
            category: "contracts",
            governs: "write_procedure"));
        await store.WriteAsync(Request(
            id: "procedure.system.modify",
            name: "Modify the application",
            governs: "changing code"));

        // "contract" hits one; "procedure" hits both. The one matching more words comes first.
        var results = await store.FindAsync("procedure contract");

        Assert.Equal(2, results.Count);
        Assert.Equal("procedure.contract.create", results[0].Id);
    }

    [Fact]
    public async Task Find_tolerates_words_that_match_nothing()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);

        await store.WriteAsync(Request(id: "procedure.contract.create", name: "Create a contract",
            category: "contracts"));

        // Requiring every token would put a cliff right here. Any token qualifying avoids it.
        Assert.Single(await store.FindAsync("please show me the contract one"));
        Assert.Empty(await store.FindAsync("kobolds dragons treasure"));
    }

    // ---- dry-run checks -------------------------------------------------------------

    private static string Detail(IReadOnlyList<WriteCheck> checks, string name) =>
        checks.Single(c => c.Name == name).Detail;

    private static bool Passed(IReadOnlyList<WriteCheck> checks, string name) =>
        checks.Single(c => c.Name == name).Passed;

    [Fact]
    public async Task Check_reports_a_malformed_id()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);

        Assert.False(Passed(await store.CheckAsync(Request(id: "no dots here")), "id-format"));
        Assert.True(Passed(await store.CheckAsync(Request()), "id-format"));
    }

    [Fact]
    public async Task Check_distinguishes_a_create_from_a_revision()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);

        Assert.Contains("new contract", Detail(await store.CheckAsync(Request()), "create-or-revise"));

        await store.WriteAsync(Request());

        Assert.Contains("version 2", Detail(await store.CheckAsync(Request()), "create-or-revise"));
    }

    [Fact]
    public async Task Check_flags_an_unfamiliar_category_without_refusing_it()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);

        await store.WriteAsync(Request());

        var checks = await store.CheckAsync(Request(id: "procedure.tools.add", category: "tools"));

        // A new category is allowed — the vocabulary is open — but it must not happen silently.
        Assert.True(Passed(checks, "category-known"));
        Assert.Contains("NEW category", Detail(checks, "category-known"));
    }

    [Fact]
    public async Task Check_fails_when_governs_is_missing()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);

        Assert.False(Passed(await store.CheckAsync(Request(governs: "")), "governs-stated"));
        Assert.True(Passed(await store.CheckAsync(Request()), "governs-stated"));
    }

    [Fact]
    public async Task Check_warns_about_a_near_duplicate()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);

        await store.WriteAsync(Request(id: "procedure.system.modify", name: "Modify the application"));

        // Different id, same job. This is the anti-sprawl guard being structural rather than a
        // soft instruction the model may skip (ARCHITECTURE.md §P12).
        var checks = await store.CheckAsync(
            Request(id: "procedure.system.change", name: "Modify the application code"));

        Assert.False(Passed(checks, "no-near-duplicate"));
        Assert.Contains("procedure.system.modify", Detail(checks, "no-near-duplicate"));
    }

    [Fact]
    public async Task Check_does_not_flag_a_contract_against_itself()
    {
        await using var db = _fixture.CreateContext();
        var store = new ProcedureStore(db);

        await store.WriteAsync(Request());

        // Revising an existing contract must not warn that it duplicates itself.
        Assert.True(Passed(await store.CheckAsync(Request()), "no-near-duplicate"));
    }
}
