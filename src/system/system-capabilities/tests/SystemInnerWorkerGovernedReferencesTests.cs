using DantesRoleplay.Interactions;
using DantesRoleplay.SystemCapabilities;

namespace DantesRoleplay.Tests;

public sealed class SystemInnerWorkerGovernedReferencesTests
{
    [Fact]
    public void Parses_only_explicit_complete_application_references()
    {
        var values = SystemInnerWorkerGovernedReferences.Parse(
            "execute demo.action; query(kind: \"demo.query\"); system capability system.conversation-memory; may execute records; query(kind: \"mechanic\") when useful");

        Assert.Collection(values,
            action =>
            {
                Assert.Equal(SystemInnerWorkerGovernedReferenceKind.Action, action.Kind);
                Assert.Equal("demo.action", action.QualifiedId);
            },
            query =>
            {
                Assert.Equal(SystemInnerWorkerGovernedReferenceKind.Query, query.Kind);
                Assert.Equal("demo.query", query.QualifiedId);
            },
            system =>
            {
                Assert.Equal(SystemInnerWorkerGovernedReferenceKind.SystemCapability, system.Kind);
                Assert.Equal("system.conversation-memory", system.QualifiedId);
            });
    }

    [Fact]
    public void Prose_that_mentions_execute_is_not_a_reference()
    {
        Assert.Empty(SystemInnerWorkerGovernedReferences.Parse(
            "The worker may execute demo.action when the model believes it is useful."));
    }

    [Fact]
    public void More_than_sixteen_explicit_references_is_rejected()
    {
        var governs = string.Join(';', Enumerable.Range(0, 17).Select(value => $"execute demo.action-{value}"));

        var error = Assert.Throws<InteractionContractException>(() =>
            SystemInnerWorkerGovernedReferences.Parse(governs));

        Assert.Equal("INNER_WORKER_GOVERNED_REFERENCES_EXCEEDED", error.Code);
    }
}
