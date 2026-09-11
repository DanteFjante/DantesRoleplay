using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using DantesRoleplay.Procedures;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.SystemCapabilities;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public async Task Application_gateway_authors_validates_and_activates_through_actual_owners()
    {
        await using var db = fixture.CreateContext();
        var initial = await PureRuntimeFixtureAsync(db,
            "return { data: { count: ctx.input.count + 1 } };");
        await AllowPurePublicationAsync(db);
        db.ChangeTracker.Clear();
        var before = initial.Setup.Activation.Current(Application)!;
        var materializer = new ActivatedApplicationCatalogMaterializer(initial.Setup.Applications,
            initial.Setup.Activation, initial.Setup.Sources, initial.Setup.Roots, initial.Setup.Extensions);
        var catalogs = new ActivatedApplicationCatalogProvider(
            new ConfiguredPublicApplicationCatalogPolicy([Application.Value]), materializer,
            new CatalogCursorCodec(new byte[32]), initial.Setup.Activation);
        var manuals = new InteractionManualContextService(new ProcedureStore(db),
            new InteractionFeatureRetriever(catalogs),
            new SqliteStandingGrantPolicy(db, initial.Setup.Resolver), initial.Setup.Resolver,
            initial.Setup.Activation, ["system"]);
        var service = new SqliteApplicationAuthoringService(db, initial.Setup.Applications,
            initial.Setup.Activation, initial.Setup.Activation, initial.Setup.Sources,
            new SqliteStandingGrantPolicy(db, initial.Setup.Resolver), initial.Setup.Resolver,
            new OperationLog(db), PureRuntimeValidator(db, initial.Setup), manuals);
        var catalog = CandidateCatalog(db, initial.Setup, service);
        var gateway = new ApplicationCandidateCapabilityGateway(catalog);
        var principal = PureRuntimeHost(initial.Setup).Principal;

        var write = await gateway.InvokeAsync(principal, Application,
            SystemCapabilityIds.ApplicationCandidateWrite,
            JsonSerializer.Serialize(new
            {
                applicationId = Application.Value,
                candidateId = initial.Candidate.CandidateId,
                expectedCandidateRevision = initial.Candidate.Revision,
                expectedActiveFingerprint = before.ActivationFingerprint,
                origin = "runtime",
                synchronizationEvidenceReference = (string?)null,
                newImplementationReason = "Exercise the application-scoped authoring gateway.",
                documents = new[]
                {
                    new
                    {
                        logicalIdentity = "file:" + PureJavaScriptPath,
                        sourceId = "catalog",
                        relativePath = PureJavaScriptPath,
                        mediaType = "text/javascript",
                        text = "return { data: { count: ctx.input.count + 2 } };"
                    }
                }
            }), "gateway-write", "website");
        Assert.True(write.Ok, write.Error?.Message);

        var candidateRow = await db.Set<ApplicationCandidateRevisionRecord>().AsNoTracking()
            .SingleAsync(value => value.CandidateId == initial.Candidate.CandidateId && value.Revision == 2);
        var candidate = new ApplicationCandidateReference(
            Application, candidateRow.CandidateId, candidateRow.Revision, candidateRow.ContentFingerprint);
        var metadata = await new ApplicationCandidateRetainedReader(db, initial.Setup.Applications)
            .ReadMetadataAsync(Application, candidate.CandidateId, candidate.Revision);
        var documents = await new ApplicationCandidateRetainedReader(db, initial.Setup.Applications)
            .ReadSelectedAsync(metadata!, [PureMarkdownPath, PureJavaScriptPath]);
        var definition = Assert.Single(SqliteApplicationAuthoringService.Definitions(
            Application, documents, [PureJavaScriptPath]));

        var validation = await gateway.InvokeAsync(principal, Application,
            SystemCapabilityIds.ApplicationCandidateValidate,
            JsonSerializer.Serialize(new
            {
                applicationId = Application.Value,
                candidateId = candidate.CandidateId,
                revision = candidate.Revision,
                contentFingerprint = candidate.ContentFingerprint,
                samples = new[]
                {
                    new
                    {
                        definition = new
                        {
                            definitionId = definition.DefinitionId,
                            kind = definition.Kind,
                            revision = definition.Revision,
                            contentFingerprint = definition.ContentFingerprint
                        },
                        inputJson = "{\"count\":1}",
                        expectedDataJson = "{\"count\":3}"
                    }
                }
            }), "gateway-validate", "codex");
        Assert.True(validation.Ok, validation.Error?.Message);
        var validationRow = await db.Set<ApplicationCandidateValidationRecord>().AsNoTracking()
            .SingleAsync(value => value.OperationId == validation.OperationId);
        Assert.True(validationRow.Outcome == "valid", validationRow.DiagnosticsJson);

        var activation = await gateway.InvokeAsync(principal, Application,
            SystemCapabilityIds.ApplicationCandidateActivate,
            JsonSerializer.Serialize(new
            {
                applicationId = Application.Value,
                candidateId = candidate.CandidateId,
                revision = candidate.Revision,
                contentFingerprint = candidate.ContentFingerprint,
                validationOperationId = validation.OperationId
            }), "gateway-activate", "website");

        Assert.True(activation.Ok, activation.Error?.Code + ": " + activation.Error?.Message);
        Assert.Equal(before.ActivationRevision + 1,
            initial.Setup.Activation.Current(Application)!.ActivationRevision);
        Assert.Single(await db.Set<ApplicationCandidatePublicationRecord>().AsNoTracking()
            .Where(value => value.CandidateId == candidate.CandidateId
                && value.Revision == candidate.Revision).ToArrayAsync());
    }

    private static ISystemCapabilityCatalog CandidateCatalog(
        DantesRoleplayDbContext db,
        SetupState setup,
        IApplicationAuthoringService service) => new SystemCapabilityCatalog(
        [new ApplicationCandidateInspectCapabilityHandler(db, setup.Applications, service)],
        new BoundedJsonSchemaValidator(),
        new PrivateOperatorAuthorizationPolicy(),
        new ISystemWriteCapabilityHandler[]
        {
            new ApplicationCandidateWriteCapabilityHandler(SystemCapabilityIds.ApplicationCandidateWrite,
                db, setup.Applications, service),
            new ApplicationCandidateWriteCapabilityHandler(SystemCapabilityIds.ApplicationCandidateValidate,
                db, setup.Applications, service),
            new ApplicationCandidateWriteCapabilityHandler(SystemCapabilityIds.ApplicationCandidateActivate,
                db, setup.Applications, service),
            new ApplicationCandidateWriteCapabilityHandler(SystemCapabilityIds.ApplicationCandidateRecover,
                db, setup.Applications, service)
        });
}
