using DantesRoleplay.DataAccess.Bootstrap;

namespace DantesRoleplay.Tests;

/// <summary>
/// Guards the bootstrap fingerprint.
///
/// A field omitted from <see cref="ProcedureFile.ContentHash"/> becomes permanently uneditable:
/// changing it in the markdown yields the same hash, so the seeder concludes nothing changed and
/// silently ignores the edit. That is not a hypothetical — `governs` shipped missing from the
/// hash and was invisible until someone went looking.
///
/// Rather than asserting the expression's shape, these vary one field at a time and check the
/// hash moves. That catches an omission regardless of how the hash is implemented.
/// </summary>
public sealed class ProcedureFileHashTests
{
    private static ProcedureFile Sample() => new(
        Id: "procedure.test.example",
        Category: "test",
        Name: "An example",
        Description: "A description.",
        Governs: "some_tool",
        Instructions: "1. Do the thing.",
        Constraints: "- Never do the other thing.",
        Status: DantesRoleplay.Procedures.ProcedureStatus.Active);

    public static TheoryData<string, ProcedureFile> ChangedField() => new()
    {
        { "Category",     Sample() with { Category = "changed" } },
        { "Name",         Sample() with { Name = "changed" } },
        { "Description",  Sample() with { Description = "changed" } },
        { "Governs",      Sample() with { Governs = "changed" } },
        { "Instructions", Sample() with { Instructions = "changed" } },
        { "Constraints",  Sample() with { Constraints = "changed" } },
        { "Status",       Sample() with { Status = DantesRoleplay.Procedures.ProcedureStatus.Deprecated } }
    };

    [Theory]
    [MemberData(nameof(ChangedField))]
    public void Editing_any_authored_field_changes_the_fingerprint(string field, ProcedureFile edited)
    {
        Assert.True(
            Sample().ContentHash != edited.ContentHash,
            $"Editing '{field}' did not change ContentHash, so an edit to it in a bootstrap file "
            + "would be silently ignored by the seeder forever. Add it to the hash.");
    }

    /// <summary>
    /// Matches is the single authored field held out of the fingerprint, so this pins the choice
    /// rather than leaving it to look like the omission bug the theory above exists to catch.
    ///
    /// Phrases decide how a contract is found, never what it says. Retrieval reads them from the
    /// file when the application activates, not from this hash, so editing them still takes effect.
    /// Including them would move all 78 procedure fingerprints at once and re-open 63 previously
    /// reviewed near-duplicate warnings, with no way to re-record that baseline while
    /// `roleplay export` is blocked by unrelated stored state.
    ///
    /// The consequence to accept knowingly: a phrase-only edit does not create a new stored
    /// revision, so the database copy lags until another field changes.
    /// </summary>
    [Fact]
    public void Match_phrases_are_deliberately_outside_the_fingerprint()
    {
        Assert.Equal(
            Sample().ContentHash,
            (Sample() with { Matches = "an entirely different phrase" }).ContentHash);
    }

    [Fact]
    public void The_fingerprint_is_stable_for_identical_content()
    {
        Assert.Equal(Sample().ContentHash, Sample().ContentHash);
    }

    [Fact]
    public void Field_boundaries_cannot_be_confused()
    {
        // Without a separator between fields, "ab"+"c" and "a"+"bc" hash identically, and two
        // genuinely different contracts would be treated as unchanged copies of each other.
        var left = Sample() with { Category = "ab", Name = "c" };
        var right = Sample() with { Category = "a", Name = "bc" };

        Assert.NotEqual(left.ContentHash, right.ContentHash);
    }
}
