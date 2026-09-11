using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Authorization;
using DantesRoleplay.Interactions;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public async Task Pure_runtime_closure_rejects_an_actual_selected_procedure()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);
        await SeedGrantAsync(db, [StandingGrantCapability.Author]);
        var write = await Service(db, setup).WriteCandidateAsync(ApplicationHost(setup, "procedure-closure", InteractionExecutionProfile.Atomic),
            Request(setup.Activation.Current(Application)!.ActivationFingerprint));
        Assert.Equal(InteractionInvocationResultTag.Committed, write.Tag);
        var candidate = await CandidateAsync(db);
        var host = ApplicationHost(setup, "procedure-closure-read");
        Assert.NotNull(await new ApplicationCandidateSelectionReader(db, setup.Applications, setup.Activation, setup.Resolver)
            .ReadAsync(host, candidate));

        Assert.Null(await new ApplicationCandidatePureRuntimeClosureReader(db, setup.Applications, setup.Activation,
            setup.Resolver, new ApplicationCandidatePureMechanicClassifier(new BoundedJsonSchemaValidator())).ReadAsync(host, candidate));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pure_runtime_closure_uses_actual_retained_pairs_without_claiming_global_completeness(bool contractOnly)
    {
        await using var db = fixture.CreateContext();
        var setup = await SetupMechanicAsync(db);
        var source = contractOnly ? MechanicSource("v1") : MechanicSource("v2");
        var document = contractOnly
            ? new ApplicationCandidateDocumentInput("file:" + MechanicMarkdownPath, "catalog", MechanicMarkdownPath,
                "text/markdown", MechanicMarkdown("Updated pure contract"))
            : new ApplicationCandidateDocumentInput("file:" + MechanicSourcePath, "catalog", MechanicSourcePath,
                "text/javascript", source);
        var write = await Service(db, setup).WriteCandidateAsync(ApplicationHost(setup, "pure-write", InteractionExecutionProfile.Atomic,
            "mechanic-grant@1"), MechanicRequest(setup.Activation.Current(Application)!.ActivationFingerprint, document));
        Assert.Equal(InteractionInvocationResultTag.Committed, write.Tag);
        var candidate = await CandidateAsync(db);
        // Changing the filesystem after the write must not substitute executable bytes.
        WriteMechanic(MechanicMarkdown("Filesystem changed"), MechanicSource("unretained"));
        var host = ApplicationHost(setup, "pure-read", grantReference: "mechanic-grant@1");
        var selected = await new ApplicationCandidateSelectionReader(db, setup.Applications, setup.Activation, setup.Resolver)
            .ReadAsync(host, candidate);
        var reader = new ApplicationCandidatePureRuntimeClosureReader(db, setup.Applications, setup.Activation,
            setup.Resolver, new ApplicationCandidatePureMechanicClassifier(new BoundedJsonSchemaValidator()));

        var proof = await reader.ReadAsync(host, candidate);

        Assert.NotNull(proof);
        Assert.NotNull(selected);
        Assert.False(selected.DependenciesComplete);
        Assert.Equal(candidate, proof.Candidate);
        Assert.Equal(selected.EvidenceFingerprint, proof.SelectionEvidenceFingerprint);
        Assert.NotEqual(selected.EvidenceFingerprint, proof.EvidenceFingerprint);
        Assert.Equal("selected-pure-mechanic-runtime-v1", proof.CoverageVersion);
        var definition = Assert.Single(proof.Definitions);
        Assert.Equal(source, definition.Plan.Source);
        Assert.Equal(definition.Plan.Definition, definition.Markdown.Definition);
        Assert.Equal(definition.Plan.Definition, definition.JavaScript.Definition);
        Assert.Equal(MechanicMarkdownPath, definition.Markdown.Document.RelativePath);
        Assert.Equal(MechanicSourcePath, definition.JavaScript.Document.RelativePath);
        Assert.Equal(proof.EvidenceFingerprint, (await reader.ReadAsync(host, candidate))!.EvidenceFingerprint);
    }

    [Theory]
    [InlineData("{\"services\":[]}")]
    [InlineData("{\"unknown\":true}")]
    public async Task Pure_runtime_closure_rejects_requirements_outside_the_actual_classifier_grammar(string requirements)
    {
        await using var db = fixture.CreateContext();
        var setup = await SetupMechanicAsync(db);
        var write = await Service(db, setup).WriteCandidateAsync(ApplicationHost(setup, "unsupported-pure-write", InteractionExecutionProfile.Atomic,
            "mechanic-grant@1"), MechanicRequest(setup.Activation.Current(Application)!.ActivationFingerprint,
                new ApplicationCandidateDocumentInput("file:" + MechanicMarkdownPath, "catalog", MechanicMarkdownPath, "text/markdown",
                    MechanicMarkdown().Replace("{}", requirements, StringComparison.Ordinal))));
        Assert.Equal(InteractionInvocationResultTag.Committed, write.Tag);
        var candidate = await CandidateAsync(db);
        var host = ApplicationHost(setup, "unsupported-pure-read", grantReference: "mechanic-grant@1");
        Assert.NotNull(await new ApplicationCandidateSelectionReader(db, setup.Applications, setup.Activation, setup.Resolver)
            .ReadAsync(host, candidate));

        Assert.Null(await new ApplicationCandidatePureRuntimeClosureReader(db, setup.Applications, setup.Activation,
            setup.Resolver, new ApplicationCandidatePureMechanicClassifier(new BoundedJsonSchemaValidator())).ReadAsync(host, candidate));
    }

    [Theory]
    [InlineData("namespace")]
    [InlineData("source")]
    public async Task Pure_runtime_closure_rechecks_owner_drift(string drift)
    {
        await using var db = fixture.CreateContext();
        var setup = await SetupMechanicAsync(db);
        var write = await Service(db, setup).WriteCandidateAsync(ApplicationHost(setup, "drift-pure-write", InteractionExecutionProfile.Atomic,
            "mechanic-grant@1"), MechanicRequest(setup.Activation.Current(Application)!.ActivationFingerprint,
                new ApplicationCandidateDocumentInput("file:" + MechanicSourcePath, "catalog", MechanicSourcePath, "text/javascript", MechanicSource("v2"))));
        Assert.Equal(InteractionInvocationResultTag.Committed, write.Tag);
        var candidate = await CandidateAsync(db);
        if (drift == "namespace") setup.Namespaces.SetEnabled("demo.runtime.mechanics", false);
        else setup.Sources.Retire(Application, "catalog", "Fixture source retired.");

        Assert.Null(await new ApplicationCandidatePureRuntimeClosureReader(db, setup.Applications, setup.Activation,
            setup.Resolver, new ApplicationCandidatePureMechanicClassifier(new BoundedJsonSchemaValidator()))
            .ReadAsync(ApplicationHost(setup, "drift-pure-read", grantReference: "mechanic-grant@1"), candidate));
    }
}
