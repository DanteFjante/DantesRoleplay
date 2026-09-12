using DantesRoleplay.Interactions;
using DantesRoleplay.DataAccess.Bootstrap;

namespace DantesRoleplay.Tests;

public sealed class ProcedureIntentPhraseEditorTests
{
    [Fact]
    public void Replaces_inserts_and_removes_only_matches_text()
    {
        var source = Doc("## Matches\nold phrase\n\n## Notes\n<!-- keep -->\nunknown: value\n");
        var changed = ProcedureIntentPhraseEditor.Edit(source, [" New   phrase ", "new phrase", "Other"]);
        Assert.Contains("## Matches\nNew phrase\nOther\n", changed);
        Assert.Contains("<!-- keep -->\nunknown: value", changed);
        Assert.Equal(ProcedureFile.Parse(source, "before") with { Matches = "" }, ProcedureFile.Parse(changed, "after") with { Matches = "" });
        Assert.Equal(ProcedureFile.Parse(source, "before").ContentHash, ProcedureFile.Parse(changed, "after").ContentHash);
        Assert.Contains("custom: preserve this field", changed);
        Assert.Equal(Doc("## Notes\nkeep\n"), ProcedureIntentPhraseEditor.Edit(Doc("## Notes\nkeep\n"), []));
        Assert.DoesNotContain("## Matches", ProcedureIntentPhraseEditor.Edit(changed, []));
    }

    [Fact]
    public void Preserves_crlf_and_rejects_fenced_owner_ambiguity()
    {
        var source = Doc("```md\n## Matches\nnot a section\n```\n\n## Notes\nkeep\n").Replace("\n", "\r\n");
        Assert.Throws<ArgumentException>(() => ProcedureIntentPhraseEditor.Edit(source, ["phrase"]));
        var changed = ProcedureIntentPhraseEditor.Edit(Doc("## Notes\nkeep\n").Replace("\n", "\r\n"), ["phrase"]);
        Assert.Contains("## Matches\r\nphrase\r\n", changed);
        Assert.DoesNotContain("\n", changed.Replace("\r\n", "", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_injections_limits_and_repeated_sections()
    {
        Assert.Throws<ArgumentException>(() => ProcedureIntentPhraseEditor.Edit(Doc(""), ["bad\n## Notes"]));
        Assert.Throws<ArgumentException>(() => ProcedureIntentPhraseEditor.Edit(Doc(""), Enumerable.Repeat("x", 33).ToArray()));
        Assert.Throws<ArgumentException>(() => ProcedureIntentPhraseEditor.Edit(Doc(""), [new string('x', 201)]));
        Assert.Throws<ArgumentException>(() => ProcedureIntentPhraseEditor.Edit(Doc("## Matches\na\n## Matches\nb\n"), ["x"]));
    }

    private static string Doc(string body) => """
        ---
        id: procedure.fixture.inspect
        category: tools
        name: Inspect
        description: Inspect.
        governs: inspect
        status: active
        custom: preserve this field
        ---

        ## Description
        Inspect precisely.

        ## Instructions
        Read only.

        ## Constraints
        Do not mutate state.

        """.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n" + body;
}
