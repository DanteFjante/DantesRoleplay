using DantesRoleplay.Procedures;

namespace DantesRoleplay.Tests;

public sealed class ProcedureManualSectionsTests
{
    [Fact]
    public void Derive_preserves_heading_context_and_bounds_each_section()
    {
        var sections = ProcedureManualSections.Derive("procedure.system.inspect", """
            # Prepare
            Read the target.
            ## Validate
            Confirm the current revision.
            """, """
            ## Safety
            Do not write during inspection.
            """);

        Assert.Contains(sections, value => value.Heading == "Validate" && value.ParentContext == "Prepare");
        Assert.Contains(sections, value => value.Heading == "Safety");
        Assert.All(sections, value =>
        {
            Assert.StartsWith("procedure.system.inspect#", value.Reference);
            Assert.InRange(value.Text.Length, 1, ProcedureManualSections.MaximumSectionCharacters);
            Assert.Equal(64, value.ContentFingerprint.Length);
        });
    }

    [Fact]
    public void Derive_splits_large_bodies_without_losing_the_section_identity()
    {
        var sections = ProcedureManualSections.Derive("procedure.system.inspect",
            "## Detail\n" + new string('x', ProcedureManualSections.MaximumSectionCharacters + 50), "");

        Assert.Equal(2, sections.Count);
        Assert.Equal(sections[0].Heading, sections[1].Heading);
        Assert.NotEqual(sections[0].Reference, sections[1].Reference);
        Assert.All(sections, value => Assert.InRange(value.Text.Length, 1, ProcedureManualSections.MaximumSectionCharacters));
    }
}
