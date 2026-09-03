using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Interactions;
using DantesRoleplay.Mechanics;
using DantesRoleplay.SchemaValidation;
using DantesRoleplay.Sources;
using DantesRoleplay.SystemCapabilities;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Tests;

public sealed class InteractionMechanicSandboxTests : IDisposable
{
    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly ApplicationIdentifier App = ApplicationIdentifier.Parse("sample-app");
    private readonly SqliteFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task Reviewed_candidate_stays_in_sqlite_until_explicit_export_approval()
    {
        await using var db = fixture.CreateContext();
        var service = Service(db, CandidateSnapshot());
        var created = await service.CreateOrReviseAsync(Command(Candidate(), "draft.create"), Authority("create"));

        Assert.Equal("validated", created.Status);
        var preview = Assert.Single(Assert.Single(created.Validation.ScenarioResults).EffectPreviews!);
        Assert.Equal("component.set", preview.Type);
        Assert.Equal("component.test", preview.DefinitionId);
        Assert.Equal(1, created.Revision);
        Assert.Equal(1, await db.InteractionMechanicSandboxDrafts.CountAsync());
        Assert.Equal(1, await db.InteractionMechanicSandboxDraftRevisions.CountAsync());
        Assert.Equal(0, await db.Mechanics.CountAsync());

        var replay = await service.CreateOrReviseAsync(Command(Candidate(), "draft.create"), Authority("replay"));
        Assert.Equal(created.DraftId, replay.DraftId);
        Assert.Equal(1, await db.InteractionMechanicSandboxDraftRevisions.CountAsync());

        var approved = await service.PromoteAsync(new(App, "state.1", created.DraftId, 1, "promote.1"),
            Authority("promote"));
        Assert.Equal("approved-for-export", approved.Draft.Status);
        Assert.True(approved.Export.PermanentIdRequired);
        Assert.False(approved.Export.FilesystemWritePerformed);
        Assert.False(approved.Export.Activated);
        Assert.Equal(0, await db.Mechanics.CountAsync());
    }

    [Fact]
    public async Task Sandbox_escape_and_out_of_allowlist_effects_cannot_validate()
    {
        await using var db = fixture.CreateContext();
        var service = Service(db, CandidateSnapshot());
        var escape = Candidate() with
        {
            Name = "Escape attempt",
            MatchPhrases = ["attempt a sandbox escape"],
            Source = "return { narration: System.IO.File.ReadAllText('secret.txt') };",
            Scenarios = [Scenario([], [], 0)]
        };
        var escapeValidation = await service.ValidateAsync(App, "state.1", escape);
        Assert.False(escapeValidation.Passed);
        Assert.Contains(escapeValidation.ScenarioResults, value => !value.Passed && !value.SandboxOk);

        var extraEffect = Candidate() with
        {
            Name = "Undeclared effect attempt",
            MatchPhrases = ["write an undeclared component"],
            Source = "return { effects: [{ type: 'component.set', entityId: 'entity.1', definitionId: 'component.other', data: '{}' }] };",
            Scenarios = [Scenario(["component.set"], ["component.other"], 1)]
        };
        var effectValidation = await service.ValidateAsync(App, "state.1", extraEffect);
        Assert.False(effectValidation.Passed);
        Assert.Contains(effectValidation.ScenarioResults, value =>
            !value.Passed && value.Summary.Contains("outside its declared allowlist", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Quota_expiry_and_revision_bounds_are_enforced_per_application()
    {
        await using var db = fixture.CreateContext();
        var service = Service(db, CandidateSnapshot());
        for (var index = 0; index < InteractionMechanicSandboxProtocol.MaximumActiveDraftsPerApplication; index++)
        {
            var candidate = Candidate() with
            {
                Name = $"Candidate {index}",
                MatchPhrases = [$"perform distinct candidate operation {index}"]
            };
            await service.CreateOrReviseAsync(Command(candidate, $"draft.{index}"), Authority($"create.{index}"));
        }

        var quota = await Assert.ThrowsAsync<InteractionContractException>(() =>
            service.CreateOrReviseAsync(Command(Candidate() with
            {
                Name = "Candidate overflow",
                MatchPhrases = ["perform distinct candidate overflow"]
            }, "draft.overflow"), Authority("overflow")));
        Assert.Equal("MECHANIC_SANDBOX_QUOTA_EXCEEDED", quota.Code);

        var expired = await db.InteractionMechanicSandboxDrafts.OrderBy(value => value.Id).FirstAsync();
        expired.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        var replacement = await service.CreateOrReviseAsync(Command(Candidate() with
        {
            Name = "Replacement candidate",
            MatchPhrases = ["perform replacement candidate operation"]
        }, "draft.replacement"), Authority("replacement"));
        Assert.Contains(replacement.Status, new[] { "draft", "validated" });
    }

    [Fact]
    public async Task Deterministic_sprawl_against_catalog_or_another_draft_blocks_promotion()
    {
        await using var db = fixture.CreateContext();
        var catalogCandidate = Candidate();
        var catalogService = Service(db, CandidateSnapshot(catalogCandidate));
        var catalogConflict = await catalogService.ValidateAsync(App, "state.1", catalogCandidate);
        Assert.False(catalogConflict.Passed);
        Assert.Contains(catalogConflict.AntiSprawlChecks, value => value.Blocking && !value.Passed);

        using var isolatedFixture = new SqliteFixture();
        await using var isolatedDb = isolatedFixture.CreateContext();
        var service = Service(isolatedDb, CandidateSnapshot());
        var first = await service.CreateOrReviseAsync(Command(Candidate(), "first"), Authority("first"));
        var duplicate = Candidate() with { Name = "Duplicate responsibility" };
        var second = await service.CreateOrReviseAsync(Command(duplicate, "second"), Authority("second"));
        Assert.Equal("draft", second.Status);
        Assert.Contains(second.Validation.AntiSprawlChecks, value => value.Blocking && !value.Passed);

        var promotion = await Assert.ThrowsAsync<InteractionContractException>(() => service.PromoteAsync(
            new(App, "state.1", second.DraftId, second.Revision, "promote.conflict"), Authority("promote.conflict")));
        Assert.Equal("MECHANIC_SANDBOX_PROMOTION_BLOCKED", promotion.Code);
        Assert.Equal("validated", first.Status);
    }

    [Fact]
    public async Task Capability_outputs_return_schema_valid_repair_and_export_material()
    {
        await using var db = fixture.CreateContext();
        var opportunities = new Opportunities();
        var service = Service(db, CandidateSnapshot(), opportunities);
        var candidate = Candidate();
        var draftHandler = new InteractionMechanicSandboxWriteCapabilityHandler(
            SystemCapabilityIds.MechanicSandboxDraft, service, opportunities);
        var draftResult = await draftHandler.ExecuteAsync(DraftInput(candidate, "capability.create"),
            ExecutionContext("capability.create"));
        Assert.True(draftResult.Ok, draftResult.Error?.Message);
        ValidateOutput(draftHandler.Registration, draftResult.Data!.Value);
        Assert.True(draftResult.Data.Value.GetProperty("candidate").GetProperty("requirements").ValueKind
            == JsonValueKind.Object);
        Assert.True(draftResult.Data.Value.GetProperty("validation").GetProperty("passed").GetBoolean());

        var draftId = draftResult.Data.Value.GetProperty("draft").GetProperty("draftId").GetString()!;
        var readHandler = new InteractionMechanicSandboxReadCapabilityHandler(service);
        var readResult = await readHandler.ReadAsync(JsonSerializer.SerializeToElement(new
        {
            applicationId = App.Value,
            draftId
        }));
        Assert.True(readResult.Ok, readResult.Error?.Message);
        ValidateOutput(readHandler.Registration, readResult.Data!.Value);
        Assert.Equal(draftId, readResult.Data.Value.GetProperty("detail").GetProperty("draft")
            .GetProperty("draftId").GetString());

        var promoteHandler = new InteractionMechanicSandboxWriteCapabilityHandler(
            SystemCapabilityIds.MechanicSandboxPromote, service, opportunities);
        var promoteResult = await promoteHandler.ExecuteAsync(JsonSerializer.SerializeToElement(new
        {
            applicationId = App.Value,
            stateSpaceId = "state.1",
            draftId,
            expectedRevision = 1,
            idempotencyKey = "capability.promote"
        }), ExecutionContext("capability.promote"));
        Assert.True(promoteResult.Ok, promoteResult.Error?.Message);
        ValidateOutput(promoteHandler.Registration, promoteResult.Data!.Value);
        var export = promoteResult.Data.Value.GetProperty("export");
        Assert.True(export.GetProperty("permanentIdRequired").GetBoolean());
        Assert.False(export.GetProperty("filesystemWritePerformed").GetBoolean());
        Assert.False(export.GetProperty("activated").GetBoolean());
        Assert.True(export.GetProperty("candidate").GetProperty("requirements").ValueKind == JsonValueKind.Object);
    }

    private static InteractionMechanicSandboxService Service(
        DantesRoleplayDbContext db,
        ActiveCatalogFeatureSnapshot snapshot,
        IInteractionMechanicOpportunityStore? opportunities = null) => new(
        db, opportunities ?? new Opportunities(), new Snapshots(snapshot), new PassingMechanics(),
        new JintMechanicEngine(), new BoundedJsonSchemaValidator());

    private static JsonElement DraftInput(InteractionMechanicSandboxCandidate candidate, string idempotencyKey) =>
        JsonSerializer.SerializeToElement(new
        {
            applicationId = App.Value,
            stateSpaceId = "state.1",
            opportunityProposalFingerprint = HashA,
            candidate = new
            {
                candidate.Name,
                candidate.Category,
                candidate.Description,
                candidate.MatchPhrases,
                requirements = JsonSerializer.Deserialize<JsonElement>(candidate.RequirementsJson),
                candidate.Source,
                candidate.EffectAllowlist,
                candidate.Limits,
                scenarios = candidate.Scenarios.Select(value => new
                {
                    value.Name,
                    projection = JsonSerializer.Deserialize<JsonElement>(value.ProjectionJson),
                    value.Expected
                }).ToArray()
            },
            idempotencyKey
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static SystemCapabilityWriteExecutionContext ExecutionContext(string suffix)
    {
        var principal = PrivateOperatorPrincipal.Create("fixture", "mechanic-sandbox-tests");
        var invocation = new SystemCapabilityInvocationContext(principal, "private-operator", "correlation." + suffix)
        {
            ApplicationId = App,
            StateSpaceId = "state.1"
        };
        var evidence = new AuthorizationAuditEvidence(principal.PrincipalId, principal.AuthenticationMethod,
            "system.modify", "private-operator", "correlation." + suffix, true, "PRIVATE_OPERATOR_AUTHORIZED");
        return new(invocation, "request." + suffix, "Review the candidate in the governed sandbox.",
            ["procedure.system.create-feature"], evidence);
    }

    private static void ValidateOutput(SystemCapabilityRegistration registration, JsonElement output)
    {
        var validator = new BoundedJsonSchemaValidator();
        var schema = validator.Compile(registration.OutputSchemaJson);
        Assert.True(schema.IsAccepted, string.Join("; ", schema.Diagnostics.Select(value => value.Message)));
        var result = validator.Validate(schema.ProfileId, registration.OutputSchemaJson, output.GetRawText());
        Assert.Equal(SchemaValueStatus.Valid, result.Status);
    }

    private static InteractionMechanicSandboxDraftCommand Command(
        InteractionMechanicSandboxCandidate candidate,
        string idempotencyKey) => new(App, "state.1", HashA, candidate, idempotencyKey);

    private static InteractionMechanicSandboxWriteAuthority Authority(string suffix) =>
        new(Principal, "authorization.fixture", "request." + suffix,
            "Review the candidate mechanic inside the governed sandbox.", "operation." + suffix);

    private static InteractionMechanicSandboxCandidate Candidate() => new(
        "Set a test component", "application.sample.mechanics", "Set one declared component in a captured scenario.",
        ["set the captured test component"],
        """{"roles":{},"inputSchema":{"type":"object","additionalProperties":false},"effectComponentIds":["component.test"]}""",
        "return { narration: 'updated', effects: [{ type: 'component.set', entityId: 'entity.1', definitionId: 'component.test', data: '{\"value\":1}' }] };",
        new(["component.set"], ["component.test"]), new(),
        [Scenario(["component.set"], ["component.test"], 1)]);

    private static InteractionMechanicSandboxScenario Scenario(
        IReadOnlyList<string> effectTypes,
        IReadOnlyList<string> componentIds,
        int count) => new("captured state",
        JsonSerializer.Serialize(new MechanicProjection { Seed = 1, Input = "{}" }),
        new(true, count, count, effectTypes, componentIds));

    private static ActiveCatalogFeatureSnapshot CandidateSnapshot(
        InteractionMechanicSandboxCandidate? candidate = null)
    {
        var records = candidate is null ? Array.Empty<CatalogRecordDefinition>() : [Record(candidate)];
        var manifest = CatalogNavigationManifest.Create(App, HashA, "catalog-lexical-v1",
            [new(App.Value, "Sample", "Sample application catalog.")],
            [new(App.Value, "", "Sample", "Sample application catalog.", CatalogDescriptionStatus.Authored),
             new(App.Value, "mechanics", "Mechanics", "Application mechanics.", CatalogDescriptionStatus.Authored)],
            records);
        return new(manifest, records.Select(value => new ActiveCatalogFeatureDocument(value, SourceTrust.Trusted)).ToArray());
    }

    private static CatalogRecordDefinition Record(InteractionMechanicSandboxCandidate candidate)
    {
        var content = JsonSerializer.Serialize(new
        {
            category = candidate.Category,
            requirements = JsonSerializer.Deserialize<JsonElement>(candidate.RequirementsJson),
            source = candidate.Source,
            scope = App.Value
        });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        return new(App.Value, "mechanic", App.Value + ".existing", candidate.Name, candidate.Description,
            [], candidate.MatchPhrases, "mechanics", "active", 1, content, fingerprint, "source", "mechanics/existing.json");
    }

    private sealed class Opportunities : IInteractionMechanicOpportunityStore
    {
        private static readonly InteractionMechanicOpportunityProjection Value = new(
            HashA, App, new(InteractionRecipeIds.Create(App, HashA), 1, HashA), 1, HashA, HashA,
            "Set the captured test component", [], [], "{}", [], [], ["set the captured test component"],
            new(3, 1, 1, 0, 0, 1, 0), [], "A reviewed mechanic is more efficient than replaying this recipe.", DateTime.UtcNow);

        public Task<InteractionMechanicOpportunityWriteResult> AppendAsync(
            InteractionMechanicOpportunityDraft draft, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<InteractionMechanicOpportunityProjection?> GetAsync(
            ApplicationIdentifier applicationId, string sourceRecipeId, CancellationToken cancellationToken = default) =>
            Task.FromResult<InteractionMechanicOpportunityProjection?>(Value);

        public Task<IReadOnlyList<InteractionMechanicOpportunityProjection>> ListAsync(
            ApplicationIdentifier applicationId, int limit = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InteractionMechanicOpportunityProjection>>([Value]);
    }

    private sealed class Snapshots(ActiveCatalogFeatureSnapshot snapshot) : IActiveCatalogFeatureSnapshotProvider
    {
        public bool TryGetSnapshot(ApplicationIdentifier applicationId, out ActiveCatalogFeatureSnapshot value)
        {
            value = snapshot;
            return applicationId == App;
        }
    }

    private sealed class PassingMechanics : IMechanicStore
    {
        public Task<IReadOnlyList<MechanicCheck>> CheckAsync(WriteMechanicRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MechanicCheck>>(
            [new("requirements", true, "Requirements pass."), new("components", true, "Components pass.")]);
        public Task<IReadOnlyList<MechanicSummary>> FindAsync(string? query = null, string? category = null,
            string? scope = null, bool includeInactive = false, int limit = 50,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MechanicDetail?> GetAsync(string id, int? version = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<WriteMechanicResult> WriteAsync(WriteMechanicRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MechanicCategoryCount>> GetCategoriesAsync(bool includeInactive = false,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
