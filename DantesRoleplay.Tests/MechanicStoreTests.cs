using DantesRoleplay.DataAccess;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.Tests;

public sealed class MechanicStoreTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static WriteMechanicRequest Request(
        string id = "mechanic.check.ability",
        string name = "Ability check",
        string matches = "check\ntest an attribute",
        string source = "return { narration: 'ok', effects: [] };",
        string requirements = """{"roles":{"subject":{"components":["stats"]}}}""",
        string scope = "") =>
        new()
        {
            Id = id,
            Category = "check",
            Name = name,
            Description = "Resolves whether someone succeeds at something.",
            Matches = matches,
            Requirements = requirements,
            Source = source,
            Scope = scope
        };

    // ---- versioning: the same guarantee as procedure contracts --------------------------

    [Fact]
    public async Task New_mechanics_default_to_draft_until_the_author_explicitly_activates_them()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        var result = await store.WriteAsync(Request());

        Assert.Equal(MechanicStatus.Draft, result.Mechanic.Status);
    }

    [Fact]
    public async Task Revising_a_mechanic_preserves_its_status_when_status_is_omitted()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        await store.WriteAsync(Request() with { Status = MechanicStatus.Active });
        var revised = await store.WriteAsync(Request(source: "return { narration: 'v2', effects: [] };"));

        Assert.Equal(MechanicStatus.Active, revised.Mechanic.Status);
        Assert.Equal(2, revised.Mechanic.Version);
    }

    [Fact]
    public async Task Revising_appends_a_version_and_the_old_source_stays_readable()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        var first = await store.WriteAsync(Request(source: "return { narration: 'v1', effects: [] };"));
        var second = await store.WriteAsync(Request(source: "return { narration: 'v2', effects: [] };"));

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(2, second.Mechanic.Version);

        // The whole point of append-only storage here: an operation recorded last week ran against
        // v1, and that stays explainable only while v1's source is still there.
        var original = await store.GetAsync("mechanic.check.ability", version: 1);
        Assert.NotNull(original);
        Assert.Contains("v1", original.Source);

        var live = await store.GetAsync("mechanic.check.ability");
        Assert.NotNull(live);
        Assert.Contains("v2", live.Source);
        Assert.Equal(2, live.LatestVersion);
    }

    // ---- retrieval: how run_action finds a rule from free text --------------------------

    [Fact]
    public async Task A_mechanic_is_found_by_the_words_a_player_would_use()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        await store.WriteAsync(Request(matches: "shove\npush someone back"));

        // Not the mechanic's name, not its id — the author's phrases are what make free-text
        // intent matching work at all.
        var found = await store.FindAsync("I try to shove the guard");

        Assert.Single(found);
        Assert.Equal("mechanic.check.ability", found[0].Id);
    }

    [Fact]
    public async Task A_query_that_matches_nothing_relevant_returns_nothing_rather_than_everything()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        await store.WriteAsync(Request());

        Assert.Empty(await store.FindAsync("negotiate a treaty"));
    }

    [Fact]
    public async Task Search_matches_id_name_description_and_author_phrases()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        await store.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.travel.path",
            Category = "movement",
            Name = "Path selection",
            Description = "Chooses a route through the valley.",
            Matches = "find a route",
            Source = "return { effects: [] };"
        });

        Assert.Single(await store.FindAsync("mechanic.travel.path"));
        Assert.Single(await store.FindAsync("path selection"));
        Assert.Single(await store.FindAsync("route through valley"));
        Assert.Single(await store.FindAsync("find a route"));
    }

    [Fact]
    public async Task Player_match_phrases_exclude_rules_that_only_share_generic_description_words()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        await store.WriteAsync(Request(
            id: "mechanic.lock.pick",
            name: "Pick a lock",
            matches: "pick a lock\npick the lock",
            source: "return { narration: 'picked', effects: [] };"));

        await store.WriteAsync(Request(
            id: "mechanic.combat.resolve",
            name: "Resolve an attack",
            matches: "make an attack",
            source: "return { narration: 'attacked', effects: [] };"));

        // "resolve" appears in the second mechanic's name and description. It is not a player
        // phrase for that rule, so it must not make the search response noisy once "pick a lock"
        // has a direct match.
        var found = await store.FindAsync("Orban is trying to pick another lock. Resolve it.");

        var match = Assert.Single(found);
        Assert.Equal("mechanic.lock.pick", match.Id);
    }

    [Fact]
    public async Task Description_search_remains_available_when_no_phrase_or_identity_matches()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        await store.WriteAsync(new WriteMechanicRequest
        {
            Id = "mechanic.diplomacy.petition",
            Category = "social",
            Name = "Make a petition",
            Description = "Negotiates the terms of a treaty with a court.",
            Matches = "petition the court",
            Source = "return { narration: 'petitioned', effects: [] };"
        });

        var found = await store.FindAsync("treaty");

        Assert.Equal("mechanic.diplomacy.petition", Assert.Single(found).Id);
    }

    // ---- scope: the answer to "campaign or shared ruleset?" -----------------------------

    [Fact]
    public async Task A_campaign_sees_its_own_rules_and_the_shared_ones()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        await store.WriteAsync(Request(id: "mechanic.check.ability", name: "Ability check"));
        await store.WriteAsync(Request(id: "mechanic.travel.move", name: "Move", scope: "campaign.one"));
        await store.WriteAsync(Request(id: "mechanic.trade.barter", name: "Barter", scope: "campaign.two"));

        var ids = (await store.FindAsync(scope: "campaign.one")).Select(m => m.Id).ToList();

        Assert.Contains("mechanic.travel.move", ids);

        // Shared rules are always visible. A campaign that silently lost the base rules would
        // present as "the system forgot how to do what it did yesterday".
        Assert.Contains("mechanic.check.ability", ids);

        Assert.DoesNotContain("mechanic.trade.barter", ids);
    }

    [Fact]
    public async Task A_campaign_rule_outranks_the_shared_one_it_replaces()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        await store.WriteAsync(Request(id: "mechanic.check.shared", name: "Ability check", matches: "check"));
        await store.WriteAsync(Request(id: "mechanic.check.house", name: "Ability check", matches: "check", scope: "campaign.one"));

        var ranked = await store.FindAsync("check", scope: "campaign.one");

        // This ordering is the whole of the inheritance chain, and it is why a scope column beat
        // a real hierarchy: the behaviour that was wanted costs one OrderByDescending.
        Assert.Equal("mechanic.check.house", ranked[0].Id);
    }

    // ---- checks: what a dry run tells the author before anything runs -------------------

    [Fact]
    public async Task Malformed_requirements_are_caught_before_the_mechanic_ever_runs()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        var checks = await store.CheckAsync(Request(requirements: "{not json"));
        var parse = checks.Single(c => c.Name == "requirements-parse");

        Assert.False(parse.Passed);
        Assert.Contains("roles", parse.Detail);
    }

    [Fact]
    public async Task Invalid_child_bindings_are_caught_before_a_parent_is_activated()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        var checks = await store.CheckAsync(Request(requirements: """
            {
              "roles":{"encounter":{"components":[]}},
              "children":{
                "rolls":{
                  "mechanicId":"mechanic.test.child",
                  "roleBindings":{"subject":"$item"}
                }
              }
            }
            """));

        var childDeclarations = checks.Single(c => c.Name == "child-declarations");
        Assert.False(childDeclarations.Passed);
        Assert.True(childDeclarations.Blocking);
        Assert.Contains("$item", childDeclarations.Detail);
    }

    [Fact]
    public async Task Ambiguous_child_input_selection_is_caught_before_a_parent_is_activated()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        var checks = await store.CheckAsync(Request(requirements: """
            {
              "children":{
                "child":{
                  "mechanicId":"mechanic.test.child",
                  "roleBindings":{},
                  "inputFromParentProperty":"childInput"
                }
              }
            }
            """));

        var childDeclarations = checks.Single(c => c.Name == "child-declarations");
        Assert.False(childDeclarations.Passed);
        Assert.Contains("inherit", childDeclarations.Detail);
    }

    [Fact]
    public async Task Naming_a_component_that_does_not_exist_is_reported_as_a_typo_not_a_silence()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        var checks = await store.CheckAsync(Request());
        var exist = checks.Single(c => c.Name == "components-exist");

        // Otherwise this surfaces mid-run as an empty object, which reads to the mechanic like
        // "this entity genuinely has no stats" — a wrong answer rather than an error.
        Assert.False(exist.Passed);
        Assert.Contains("stats", exist.Detail);
        Assert.Contains("define_component", exist.Detail);
    }

    [Fact]
    public async Task Declared_components_that_exist_pass_the_check()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        await world.DefineComponentAsync("stats", "Stats", "Numeric attributes.");

        var store = new MechanicStore(db);
        var checks = await store.CheckAsync(Request());

        Assert.True(checks.Single(c => c.Name == "components-exist").Passed);
        Assert.True(checks.Single(c => c.Name == "requirements-parse").Passed);
    }

    [Fact]
    public async Task Two_mechanics_answering_the_same_phrase_are_flagged_before_the_second_is_written()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        await store.WriteAsync(Request(id: "mechanic.check.ability", matches: "check\ntest an attribute"));

        var checks = await store.CheckAsync(
            Request(id: "mechanic.check.attribute", matches: "check\ntest an attribute"));

        var duplicate = checks.Single(c => c.Name == "no-near-duplicate");

        // §P12. Worse for mechanics than for contracts: two rules matching one phrase means the
        // same action resolves differently depending on which retrieval ranked first.
        Assert.False(duplicate.Passed);
        Assert.Contains("mechanic.check.ability", duplicate.Detail);
        Assert.False(duplicate.Blocking);
    }

    [Fact]
    public async Task Invalid_authoring_checks_are_blocking()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        var checks = await store.CheckAsync(Request(
            matches: "",
            source: "",
            requirements: "{not json"));

        Assert.All(
            checks.Where(c => c.Name is "requirements-parse" or "source-present" or "matches-stated"),
            check => Assert.True(check.Blocking));
    }

    [Fact]
    public async Task Archived_mechanics_are_hidden_unless_inactive_results_are_requested()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        await store.WriteAsync(Request(
            id: "mechanic.archived.rule",
            matches: "archived rule") with { Status = MechanicStatus.Archived });

        Assert.Empty(await store.FindAsync("archived rule"));
        Assert.Single(await store.FindAsync("archived rule", includeInactive: true));
    }

    [Fact]
    public async Task An_id_that_is_not_a_dotted_identifier_is_refused_with_the_reason()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        var checks = await store.CheckAsync(Request(id: "ability check"));
        var format = checks.Single(c => c.Name == "id-format");

        Assert.False(format.Passed);
        Assert.Contains("permanent", format.Detail);
    }

    [Fact]
    public async Task The_requirements_flatten_to_everything_the_mechanic_may_read()
    {
        // The supervision question — "what can this rule see?" — answered without reading source.
        var requirements = MechanicRequirements.Parse(
            """{"roles":{"subject":{"components":["stats","marks"],"includeContents":true,"contentComponentIds":["secrets"]},"other":{"components":["stats"]}}}""");

        Assert.Equal(2, requirements.Roles.Count);
        Assert.Equal(["stats", "marks", "secrets"], requirements.AllComponentIds());

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Invalid_nested_content_projection_is_rejected_before_a_mechanic_is_written()
    {
        await using var db = _fixture.CreateContext();
        var store = new MechanicStore(db);

        var checks = await store.CheckAsync(Request(requirements: """
            {"roles":{
              "container":{"components":[],"contentsDepth":2},
              "empty":{"components":[],"contentComponentIds":[]},
              "deep":{"components":[],"includeContents":true,"contentsDepth":5},
              "nested":{"components":[],"includeContents":true,"contentComponentIds":["stats","stats"]}
            }}
            """));

        var declaration = checks.Single(check => check.Name == "projection-declaration");
        Assert.False(declaration.Passed);
        Assert.Contains("includeContents", declaration.Detail);
        Assert.Contains("between 1 and 4", declaration.Detail);
        Assert.Contains("distinct", declaration.Detail);
    }

    [Fact]
    public async Task An_unknown_descendant_component_is_rejected_before_a_mechanic_is_written()
    {
        await using var db = _fixture.CreateContext();
        var world = new WorldStore(db);
        await world.DefineComponentAsync("stats", "Stats", "Numeric attributes.");
        var store = new MechanicStore(db);

        var checks = await store.CheckAsync(Request(requirements: """
            {"roles":{"container":{
              "components":[],
              "includeContents":true,
              "contentComponentIds":["missing.descendant.component"]
            }}}
            """));

        var components = checks.Single(check => check.Name == "components-exist");
        Assert.False(components.Passed);
        Assert.Contains("missing.descendant.component", components.Detail);
    }
}
