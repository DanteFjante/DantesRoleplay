using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Interactions;
using DantesRoleplay.SchemaValidation;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Workflow_review_closure_uses_exact_retained_pair_and_active_job_contract(bool staleJob)
    {
        await using var db = fixture.CreateContext();
        var setup = await SetupMechanicAsync(db);
        var host = ApplicationHost(setup, "workflow-dependency", grantReference: "mechanic-grant@1");
        var job = await setup.Resolver.ResolveCurrentAsync(host, "demo.runtime.inspect", "procedure");
        var target = Assert.IsType<StandingGrantDefinitionTarget>(job.Target);
        var compiledSchema = new BoundedJsonSchemaValidator().Compile(
            "{\"type\":\"object\",\"additionalProperties\":false}");
        var schema = compiledSchema.NormalizedSchema;
        var schemaHash = compiledSchema.SchemaHash;
        var dependencyHash = staleJob ? new string('F', 64) : target.ContentFingerprint;
        var requirements = InteractionCanonicalJson.CanonicalizeObject(JsonSerializer.Serialize(new
        {
            service = new
            {
                inputSchemaHash = schemaHash,
                inputSchemaJson = schema,
                outputSchemaHash = schemaHash,
                outputSchemaJson = schema,
                reads = Array.Empty<object>(),
                jobs = new[]
                {
                    new
                    {
                        alias = "inspect",
                        qualifiedProcedureId = target.DefinitionId,
                        procedureVersion = target.Revision,
                        contentFingerprint = dependencyHash,
                        resultSchemaFingerprint = schemaHash,
                        resultSchemaJson = schema
                    }
                }
            }
        }));
        var markdown = MechanicMarkdown("Workflow fixture").Replace("{}", requirements, StringComparison.Ordinal);
        var source = "return {data:{ok:true}};";
        var write = await Service(db, setup).WriteCandidateAsync(
            ApplicationHost(setup, "workflow-write", InteractionExecutionProfile.Atomic, "mechanic-grant@1"),
            MechanicRequest(setup.Activation.Current(Application)!.ActivationFingerprint,
                new("file:" + MechanicMarkdownPath, "catalog", MechanicMarkdownPath, "text/markdown", markdown),
                new("file:" + MechanicSourcePath, "catalog", MechanicSourcePath, "text/javascript", source)));
        Assert.Equal(InteractionInvocationResultTag.Committed, write.Tag);
        var candidate = await CandidateAsync(db);
        var selection = await new ApplicationCandidateSelectionReader(db, setup.Applications,
            setup.Activation, setup.Resolver).ReadAsync(host, candidate);
        Assert.NotNull(selection);
        var materializer = new ActivatedApplicationCatalogMaterializer(setup.Applications, setup.Activation,
            setup.Sources, setup.Roots, setup.Extensions);
        IApplicationCandidateReviewClosureReader reader = new ApplicationCandidateWorkflowReviewClosureReader(
            db, setup.Applications, setup.Activation, setup.Activation, setup.Resolver, materializer,
            new ApplicationReadOnlyServiceDefinitionReader(new BoundedJsonSchemaValidator()));

        var result = await reader.ReadAsync(host, candidate, selection!);

        Assert.Equal(staleJob ? ApplicationCandidateReviewClosureReadStatus.Rejected
            : ApplicationCandidateReviewClosureReadStatus.Available, result.Status);
        if (staleJob) return;
        var closure = Assert.IsType<ApplicationCandidateWorkflowReviewClosureEvidence>(result.Evidence);
        Assert.Equal(ApplicationCandidateWorkflowReviewClosureReader.GrammarVersion, closure.Grammar);
        Assert.Equal(selection!.EvidenceFingerprint, closure.SelectionEvidenceFingerprint);
        Assert.NotNull(closure.Predecessor);
        Assert.Equal(MechanicId, closure.Predecessor!.DefinitionId);
        Assert.Equal(new StandingGrantDefinitionReference(target.DefinitionId, target.Kind,
            target.Revision, target.ContentFingerprint), Assert.Single(closure.Dependencies));
        Assert.Equal(3, closure.ReviewDocuments.Length);
        Assert.Equal(2, closure.ReviewDocuments.Count(value =>
            value.Role is ApplicationCandidateReviewDocumentRole.Changed));
        Assert.Single(closure.ReviewDocuments.Where(value =>
            value.Role is ApplicationCandidateReviewDocumentRole.Dependency));
        Assert.Equal(closure.EvidenceFingerprint,
            (Assert.IsType<ApplicationCandidateWorkflowReviewClosureEvidence>(
                (await reader.ReadAsync(host, candidate, selection)).Evidence)).EvidenceFingerprint);
    }
}
