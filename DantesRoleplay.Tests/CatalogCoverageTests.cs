using System.Data;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

/// <summary>
/// Every table and every column is either carried by the catalog or deliberately left out, and this
/// is where "deliberately" is written down.
///
/// The catalog answers one question — can the database be extracted and put back? — and that answer
/// decays silently. Add a column next month and nothing fails: export keeps working, import keeps
/// working, and the new field is quietly dropped on every round trip. Nobody finds out until
/// somebody restores from a catalog and the data is subtly wrong.
///
/// So the lists below are not a description of the code. They are the specification, and the code
/// is checked against them. A new column fails this test until someone classifies it, and
/// classifying it means writing the sentence explaining the choice.
///
/// This is the same reasoning as <see cref="MigrationDriftTests"/>, one layer up: that one asserts
/// the migrations match the model, this one asserts the catalog matches the model.
/// </summary>
public sealed class CatalogCoverageTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    // ---- tables --------------------------------------------------------------------------

    /// <summary>Tables the catalog writes out, and where they land.</summary>
    private static readonly Dictionary<string, string> CarriedTables = new(StringComparer.Ordinal)
    {
        ["entity"] = "world/entities/<id>.json",
        ["component"] = "folded into the entity file that carries it",
        ["containment"] = "folded into the entity file — a thing is inside at most one thing",
        ["relationship"] = "world/relationships.json, as one set",
        ["component_definition"] = "components/<id>.json, schema in a sibling .schema.json",
        ["mechanic"] = "mechanics/<category>/<id>.md + .js",
        ["mechanic_version"] = "the current version only; the chain stays in the database",
        ["procedure_contract"] = "procedures/<category>/<id>.md",
        ["procedure_contract_version"] = "the current version only",
        ["event_type"] = "event-types/<id>.json, schema in a sibling .schema.json",
        ["event_type_version"] = "the current version only; the chain stays in the database",
        ["subscription"] = "subscriptions/<id>.json",
        ["subscription_version"] = "the current version only; the chain stays in the database",
        ["operation"] = "history/operations.jsonl, with --with-history. EXPORT ONLY — nothing imports it"
    };

    /// <summary>Tables the catalog does not write out, and why not.</summary>
    private static readonly Dictionary<string, string> SkippedTables = new(StringComparer.Ordinal)
    {
        // KNOWN GAP, deliberately open. Declared in the model with no store method, no MCP verb and
        // no seeder — nothing in the solution reads or writes it, and it holds zero rows. If that
        // ever changes, this entry is what should stop the change from silently losing data.
        ["procedure_relation"] = "GAP: declared but unused. Nothing reads or writes it. "
            + "If contract relations ever get an API, they must be added to the catalog in the "
            + "same change, and this entry removed.",

        ["__EFMigrationsHistory"] = "Schema bookkeeping, not content. A catalog describes what the "
            + "database holds, not which migrations built it.",
        ["__EFMigrationsLock"] = "Schema bookkeeping.",
        ["sqlite_sequence"] = "SQLite internal."
    };

    /// <summary>
    /// Tables that exist in a migrated database but not in one built by EnsureCreated, which is how
    /// the test fixture builds its own. They stay classified above — a real database has them — but
    /// their absence here is not evidence that the list has gone stale.
    /// </summary>
    private static readonly HashSet<string> OnlyInAMigratedDatabase = new(StringComparer.Ordinal)
    {
        "__EFMigrationsHistory",
        "__EFMigrationsLock"
    };

    [Fact]
    public void Every_table_is_either_carried_by_the_catalog_or_deliberately_skipped()
    {
        var actual = TableNames();
        var classified = CarriedTables.Keys.Concat(SkippedTables.Keys).ToHashSet(StringComparer.Ordinal);

        var unclassified = actual.Except(classified, StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList();

        Assert.True(
            unclassified.Count == 0,
            $"These tables are in the database and in neither list: {string.Join(", ", unclassified)}. "
            + "Add them to the catalog, or to SkippedTables with the reason. A table nobody decided "
            + "about is one that gets silently dropped on every round trip.");

        // And the reverse: a list that names a table which no longer exists is a list nobody is
        // maintaining, which is worse than no list.
        var stale = classified
            .Except(actual, StringComparer.Ordinal)
            .Except(OnlyInAMigratedDatabase, StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            stale.Count == 0,
            $"These tables are named in the coverage lists but no longer exist: {string.Join(", ", stale)}.");
    }

    // ---- columns -------------------------------------------------------------------------

    /// <summary>Columns whose value survives a round trip through the catalog.</summary>
    private static readonly HashSet<string> Carried = new(StringComparer.Ordinal)
    {
        "entity.Id", "entity.Name",

        "component.EntityId", "component.DefinitionId", "component.Data",

        "containment.ContainerId", "containment.ContainedId", "containment.Slot",

        "relationship.FromEntityId", "relationship.ToEntityId", "relationship.Kind", "relationship.Data",

        "component_definition.Id", "component_definition.Name",
        "component_definition.Description", "component_definition.Schema",

        "mechanic.Id", "mechanic.Category", "mechanic.Status", "mechanic.Scope",
        "mechanic_version.MechanicId", "mechanic_version.Name", "mechanic_version.Description",
        "mechanic_version.Matches", "mechanic_version.Requirements", "mechanic_version.Source",

        "procedure_contract.Id", "procedure_contract.Category", "procedure_contract.Status",
        "procedure_contract_version.ContractId", "procedure_contract_version.Name",
        "procedure_contract_version.Description", "procedure_contract_version.Instructions",
        "procedure_contract_version.Constraints", "procedure_contract_version.Governs",

        "event_type.Id", "event_type.Category", "event_type.Status", "event_type.Scope",
        "event_type_version.EventTypeId", "event_type_version.Name", "event_type_version.Description",
        "event_type_version.PayloadSchema",

        "subscription.Id", "subscription.Category", "subscription.Status", "subscription.Scope",
        "subscription_version.SubscriptionId", "subscription_version.EventTypeId", "subscription_version.EventMechanicId",
        "subscription_version.Mode", "subscription_version.Order", "subscription_version.FixedRoleEntityIdsJson",
        "subscription_version.TrackedEntityIdsJson", "subscription_version.PayloadEqualsJson",
        "subscription_version.MaxExecutionsPerChain",

        // The operation log is serialised whole, field for field. It is export only.
        "operation.Id", "operation.Error", "operation.Intent", "operation.ProceduresCited",
        "operation.ProceduresRead", "operation.Subject", "operation.Success", "operation.Summary",
        "operation.Timestamp", "operation.Tool", "operation.ConsumedReadEvidence",
        "operation.MechanicId", "operation.MechanicVersion", "operation.ProjectionJson", "operation.Seed"
    };

    /// <summary>Columns the catalog does not carry, and why each one is fine to lose.</summary>
    private static readonly Dictionary<string, string> NotCarried = new(StringComparer.Ordinal)
    {
        // --- KNOWN GAPS. These are authored text, not derived, and they ARE lost on a round trip.
        //     Ten of ten mechanics and twenty-six of twenty-seven contracts have a non-empty change
        //     note on their current version, and an import replaces every one of them with
        //     "Imported from the catalog." Closing this means carrying both as front matter,
        //     outside the fingerprint, since they describe an edit rather than the edited thing.
        ["mechanic_version.ChangeNote"] = "GAP: authored text, lost on a round trip.",
        ["mechanic_version.CreatedBy"] = "GAP: authored provenance, lost on a round trip.",
        ["procedure_contract_version.ChangeNote"] = "GAP: authored text, lost on a round trip.",
        ["procedure_contract_version.CreatedBy"] = "GAP: authored provenance, lost on a round trip.",
        ["event_type_version.ChangeNote"] = "GAP: authored text, lost on a round trip.",
        ["event_type_version.CreatedBy"] = "GAP: authored provenance, lost on a round trip.",
        ["subscription_version.ChangeNote"] = "GAP: authored text, lost on a round trip.",
        ["subscription_version.CreatedBy"] = "GAP: authored provenance, lost on a round trip.",

        // --- Surrogate keys. The catalog addresses records by their real identity.
        ["component.Id"] = "Surrogate key. A component is addressed by (entity, definition).",
        ["containment.Id"] = "Surrogate key. Containment is a property of the contained entity.",
        ["relationship.Id"] = "Surrogate key. An edge is addressed by (from, to, kind).",
        ["mechanic_version.Id"] = "Surrogate key.",
        ["procedure_contract_version.Id"] = "Surrogate key.",
        ["event_type_version.Id"] = "Surrogate key.",
        ["subscription_version.Id"] = "Surrogate key.",
        ["procedure_relation.Id"] = "Surrogate key on an unused table. See SkippedTables.",

        // --- Derived. Recomputed on write; carrying them would let a file assert something false.
        ["component.Revision"] = "Derived: a count of writes, incremented by the store.",
        ["mechanic.CurrentVersion"] = "Derived from the version rows.",
        ["procedure_contract.CurrentVersion"] = "Derived from the version rows.",
        ["mechanic_version.Version"] = "Derived. Recorded in the manifest for reference only.",
        ["procedure_contract_version.Version"] = "Derived. Recorded in the manifest for reference only.",
        ["event_type.CurrentVersion"] = "Derived from the version rows.",
        ["subscription.CurrentVersion"] = "Derived from the version rows.",
        ["event_type_version.Version"] = "Derived. Recorded in the manifest for reference only.",
        ["subscription_version.Version"] = "Derived. Recorded in the manifest for reference only.",
        ["mechanic_version.SourceHash"] = "Derived from the content; recomputed on every write.",
        ["procedure_contract_version.SourceHash"] = "Derived from the content; recomputed on every write.",
        ["event_type_version.SourceHash"] = "Derived from the content; recomputed on every write.",
        ["subscription_version.SourceHash"] = "Derived from the content; recomputed on every write.",
        ["operation.GuardEvidenceJson"] = "Runtime audit evidence, not authored catalog content.",

        // --- Timestamps. Provenance about when a row was touched, not what it says.
        ["entity.CreatedAt"] = "Timestamp.",
        ["component.CreatedAt"] = "Timestamp.",
        ["component.UpdatedAt"] = "Timestamp.",
        ["containment.CreatedAt"] = "Timestamp.",
        ["relationship.CreatedAt"] = "Timestamp.",
        ["component_definition.CreatedAt"] = "Timestamp.",
        ["component_definition.UpdatedAt"] = "Timestamp.",
        ["mechanic.CreatedAt"] = "Timestamp.",
        ["mechanic.UpdatedAt"] = "Timestamp.",
        ["mechanic_version.CreatedAt"] = "Timestamp.",
        ["procedure_contract.CreatedAt"] = "Timestamp.",
        ["procedure_contract.UpdatedAt"] = "Timestamp.",
        ["procedure_contract_version.CreatedAt"] = "Timestamp.",
        ["event_type.CreatedAt"] = "Timestamp.",
        ["event_type.UpdatedAt"] = "Timestamp.",
        ["event_type_version.CreatedAt"] = "Timestamp.",
        ["subscription.CreatedAt"] = "Timestamp.",
        ["subscription.UpdatedAt"] = "Timestamp.",
        ["subscription_version.CreatedAt"] = "Timestamp.",

        // --- Tombstones. A catalog states what the world IS; re-importing one would resurrect a
        //     row somebody deleted on purpose, so deleted entities are not exported at all.
        ["entity.DeletedAt"] = "Tombstone. Soft-deleted entities are excluded from the export.",

        // --- The unused table's own columns.
        ["procedure_relation.FromContractId"] = "Unused table. See SkippedTables.",
        ["procedure_relation.ToContractId"] = "Unused table. See SkippedTables.",
        ["procedure_relation.Kind"] = "Unused table. See SkippedTables."
    };

    [Fact]
    public void Every_column_is_either_carried_by_the_catalog_or_deliberately_left_out()
    {
        using var db = _fixture.CreateContext();

        var actual = db.Model.GetEntityTypes()
            .SelectMany(type => type.GetProperties()
                .Select(property => $"{type.GetTableName()}.{property.Name}"))
            .ToHashSet(StringComparer.Ordinal);

        var classified = Carried.Concat(NotCarried.Keys).ToHashSet(StringComparer.Ordinal);

        var unclassified = actual.Except(classified, StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unclassified.Count == 0,
            "These columns exist and nobody has decided whether the catalog carries them:\n  "
            + string.Join("\n  ", unclassified)
            + "\n\nAdd each to Carried, or to NotCarried with the sentence explaining why losing it "
            + "is fine. A column nobody decided about is one that gets silently dropped on every "
            + "round trip, and the loss shows up long after the change that caused it.");

        var stale = classified.Except(actual, StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            stale.Count == 0,
            "These columns are named in the coverage lists but no longer exist:\n  "
            + string.Join("\n  ", stale)
            + "\n\nRemove them. A list that names things which are gone is one nobody is "
            + "maintaining, which is worse than having no list.");
    }

    /// <summary>
    /// The gaps are gaps on purpose, and saying so out loud is the point of this test.
    ///
    /// If somebody closes one, this fails and they delete the entry — which is the moment to also
    /// update the plan. It is a small nag, and it is the difference between a known limitation and
    /// a forgotten one.
    /// </summary>
    [Fact]
    public void The_known_gaps_are_still_the_only_gaps()
    {
        var gaps = NotCarried
            .Where(entry => entry.Value.StartsWith("GAP:", StringComparison.Ordinal))
            .Select(entry => entry.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [
                "event_type_version.ChangeNote",
                "event_type_version.CreatedBy",
                "mechanic_version.ChangeNote",
                "mechanic_version.CreatedBy",
                "procedure_contract_version.ChangeNote",
                "procedure_contract_version.CreatedBy",
                "subscription_version.ChangeNote",
                "subscription_version.CreatedBy"
            ],
            gaps);

        Assert.Contains("procedure_relation", SkippedTables.Keys);
        Assert.StartsWith("GAP:", SkippedTables["procedure_relation"], StringComparison.Ordinal);
    }

    private List<string> TableNames()
    {
        using var db = _fixture.CreateContext();
        var connection = db.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "select name from sqlite_master where type = 'table' order by name";

        var names = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
