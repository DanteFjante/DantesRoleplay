using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Interactions;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Tests;

public sealed class InteractionMechanicOpportunityTests : IDisposable
{
    private const string Principal = "principal.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string HashB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private readonly SqliteFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task Third_matching_successful_use_creates_one_inert_exact_proposal()
    {
        await using var db = fixture.CreateContext();
        var app = ApplicationIdentifier.Parse("sample-app");
        var first = Record(app, "sample-app.mechanic.first", "Open a secured passage",
            ["passage"], "component.passage");
        var second = Record(app, "sample-app.mechanic.second", "Record the traversal",
            ["actor"], "component.history");
        var composite = CompositeRecord(app, first, second);
        var template = InteractionRecipeTemplate.FromProposal(app, new([
            new("open", InteractionPlanStepKind.Action, first.QualifiedId, 1, first.ContentFingerprint,
                [], new Dictionary<string, string> { ["passage"] = "entity.passage" }, "{}"),
            new("record", InteractionPlanStepKind.Action, second.QualifiedId, 1, second.ContentFingerprint,
                ["open"], new Dictionary<string, string> { ["actor"] = "entity.actor" }, "{}")
        ]));
        var recipeId = InteractionRecipeIds.Create(app, template.Fingerprint);
        var recipe = new InteractionRecipe
        {
            Id = recipeId,
            ApplicationId = app.Value,
            TemplateFingerprint = template.Fingerprint,
            TemplateJson = template.CanonicalJson,
            CreatedAtUtc = DateTime.UtcNow
        };
        recipe.Revisions.Add(new InteractionRecipeRevision
        {
            RecipeId = recipeId,
            Version = 2,
            Status = "verified",
            ApplicationRevision = 1,
            ApplicationFingerprint = HashA,
            EffectiveSetFingerprint = HashB,
            ResolutionFingerprint = HashB,
            ReviewerPrincipalReference = Principal,
            Reason = "Verified fixture.",
            RequestToken = "review.opportunity.fixture",
            RequestFingerprint = HashA,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.InteractionRecipes.Add(recipe);
        await db.SaveChangesAsync();

        var receipts = new InteractionReceiptStore(db, new Allow());
        var evidence = new List<(string Resolution, string Execution)>();
        for (var index = 0; index < 4; index++)
            evidence.Add(await SuccessfulReceiptPair(receipts, app, first, second, index));
        recipe.Evidence.Add(new InteractionRecipeEvidence
        {
            RecipeId = recipeId,
            ResolutionReceiptId = evidence[0].Resolution,
            ExecutionReceiptId = evidence[0].Execution,
            Kind = "derived",
            IntentText = "Open the secured passage and record the traversal",
            IntentFingerprint = HashA,
            RoleProfile = InteractionRoleProfile.Direct.StableKey,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-4)
        });
        for (var index = 1; index < 3; index++)
            recipe.Evidence.Add(Use(recipeId, evidence[index], index));
        await db.SaveChangesAsync();

        var manifest = CatalogNavigationManifest.Create(app, HashA, "catalog-lexical-v1",
            [new(app.Value, "Sample", "Sample application.")],
            [new(app.Value, "", "Sample", "Sample application.", CatalogDescriptionStatus.Authored),
             new(app.Value, "mechanics", "Mechanics", "Application mechanics.", CatalogDescriptionStatus.Authored)],
            [first, second, composite]);
        var snapshot = new ActiveCatalogFeatureSnapshot(manifest,
            [new(first, SourceTrust.Trusted), new(second, SourceTrust.Trusted), new(composite, SourceTrust.Trusted)]);
        var application = new ApplicationRevision(app, 1, HashA, []);
        var activation = new ActiveApplicationManifest(app, 1, 1, HashA, HashA, HashA, HashA, HashA,
            HashB, "coverage-v1", true, [], [], "operation.activation", DateTime.UtcNow);
        var recipeStore = new InteractionRecipeStore(db);
        var proposalStore = new InteractionMechanicOpportunityStore(db);
        var proposalLearner = new InteractionMechanicOpportunityLearner(recipeStore, proposalStore,
            new Registry(application), new Activation(activation), new Snapshots(snapshot));
        var recipeLearner = new InteractionRecipeLearner(recipeStore, null, proposalLearner);
        var reference = new InteractionRecipeReference(recipeId, 2, template.Fingerprint);

        Assert.Null(await proposalLearner.ObserveAsync(reference));
        await recipeLearner.RecordUseAsync(new(reference, evidence[3].Resolution, evidence[3].Execution,
            true, HashB, InteractionRoleProfile.Direct.StableKey,
            "Open the secured passage and record the traversal"));

        var proposal = Assert.Single(await proposalStore.ListAsync(app));
        Assert.Equal(3, proposal.SupportingReceipts.Count);
        Assert.Equal(["step.1", "step.2"], proposal.ExactChildDependencies.Select(value => value.StepId));
        Assert.Equal(["actor", "passage"], proposal.ProposedRoles.Select(value => value.Role));
        Assert.Contains("step.1", proposal.ProposedInputSchemaJson, StringComparison.Ordinal);
        Assert.Equal(["component.passage"], proposal.IntendedEffectsAndOwnership[0].EffectComponentIds);
        Assert.Equal(1, proposal.EstimatedCallReduction.GrossCallsSavedPerUse);
        Assert.Equal(0, proposal.EstimatedCallReduction.IncrementalToolCallsSavedVersusRecipe);
        Assert.Contains(proposal.PossibleOverlap, value => value.QualifiedId == composite.QualifiedId
            && value.Reason == "Equivalent declared child graph.");
        Assert.Null(proposal.GetType().GetProperty("MechanicId"));
        var json = JsonSerializer.Serialize(proposal, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("proposedMechanicId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("activate", json, StringComparison.OrdinalIgnoreCase);
        Assert.Single(db.InteractionMechanicOpportunities);

        var replay = await proposalLearner.ObserveAsync(reference);
        Assert.Equal(InteractionMechanicOpportunityWriteDisposition.Replayed, replay!.Disposition);
        Assert.Single(db.InteractionMechanicOpportunities);
    }

    private static InteractionRecipeEvidence Use(
        string recipeId,
        (string Resolution, string Execution) receipt,
        int index) => new()
    {
        RecipeId = recipeId,
        ResolutionReceiptId = receipt.Resolution,
        ExecutionReceiptId = receipt.Execution,
        Kind = "use-success",
        IntentText = "Open the secured passage and record the traversal",
        IntentFingerprint = index == 1 ? HashA : HashB,
        RoleProfile = InteractionRoleProfile.Direct.StableKey,
        CreatedAtUtc = DateTime.UtcNow.AddMinutes(index - 4)
    };

    private static async Task<(string Resolution, string Execution)> SuccessfulReceiptPair(
        InteractionReceiptStore receipts,
        ApplicationIdentifier app,
        CatalogRecordDefinition first,
        CatalogRecordDefinition second,
        int index)
    {
        var request = new InteractionAuthorizationRequest(
            TrustedPrincipalContext.VerifiedPrincipal(Principal, "local-loopback"), app, "state.1",
            InteractionCapability.Plan, "plan.request");
        var host = new InteractionHostContext(request.Principal, new(app, 1, HashA, []), "state.1",
            "session.1", "revision.1", HashB, InteractionRoleProfile.Direct,
            new(2, 4096, 4096), InteractionAuthorizationDecision.Allow(request, "plan.evidence"));
        var envelope = AuthorizedInteractionEnvelope.Create(InteractionIntent.Parse(JsonSerializer.Serialize(new
        {
            idempotencyKey = $"plan.opportunity.{index}",
            intentText = "Open the secured passage and record the traversal",
            maximumPlanSteps = 2,
            roleHints = new Dictionary<string, string>
            {
                ["passage"] = "entity.passage",
                ["actor"] = "entity.actor"
            }
        })), host);
        var proposal = InteractionProposal.Create(envelope,
        [
            new("step.1", InteractionPlanStepKind.Action,
                new(InteractionFeatureScope.Application, app, first.QualifiedId, first.Name, 1, first.ContentFingerprint),
                [], new Dictionary<string, string> { ["passage"] = "entity.passage" }, "{}", "revision.1"),
            new("step.2", InteractionPlanStepKind.Action,
                new(InteractionFeatureScope.Application, app, second.QualifiedId, second.Name, 1, second.ContentFingerprint),
                ["step.1"], new Dictionary<string, string> { ["actor"] = "entity.actor" }, "{}", "revision.1")
        ]);
        var resolution = (await receipts.AppendResolutionAsync(new(envelope,
            InteractionResolutionResult.Resolved(proposal), HashA))).Receipt!;
        var execution = (await receipts.AppendExecutionAsync(new(
            new(resolution.Id, proposal.Fingerprint, Principal, app, "state.1", $"execute.opportunity.{index}"),
            HashB, InteractionExecutionReceiptDisposition.Succeeded, "Completed.", [],
            [new(1, "step.1", InteractionExecutionStepDisposition.Succeeded, null),
             new(2, "step.2", InteractionExecutionStepDisposition.Succeeded, null)]))).Receipt!;
        return (resolution.Id, execution.Id);
    }

    private static CatalogRecordDefinition Record(
        ApplicationIdentifier app,
        string id,
        string description,
        IReadOnlyList<string> roles,
        string effectComponentId)
    {
        var requirements = JsonSerializer.Serialize(new
        {
            roles = roles.ToDictionary(value => value, _ => new { components = Array.Empty<string>() }),
            inputSchema = new { type = "object", additionalProperties = false },
            effectComponentIds = new[] { effectComponentId }
        });
        return Definition(app, id, description, JsonSerializer.Serialize(new { id, requirements }),
            [description]);
    }

    private static CatalogRecordDefinition CompositeRecord(
        ApplicationIdentifier app,
        CatalogRecordDefinition first,
        CatalogRecordDefinition second)
    {
        var id = "sample-app.mechanic.existing-composite";
        var requirements = JsonSerializer.Serialize(new
        {
            roles = new { },
            inputSchema = new { type = "object", additionalProperties = false },
            children = new Dictionary<string, object>
            {
                ["first"] = new { mechanicId = "mechanic.first", roleBindings = new { }, inheritInput = true },
                ["second"] = new { mechanicId = "mechanic.second", roleBindings = new { }, inheritInput = true,
                    after = new[] { "first" } }
            }
        });
        return Definition(app, id, "Existing secured-passage composite.",
            JsonSerializer.Serialize(new { id, requirements }), ["open and record a secured passage"]);
    }

    private static CatalogRecordDefinition Definition(
        ApplicationIdentifier app,
        string id,
        string description,
        string content,
        IReadOnlyList<string> matches)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        return new(app.Value, "mechanic", id, id[(id.LastIndexOf('.') + 1)..], description,
            [], matches, "mechanics", "active", 1, content, fingerprint, "source", $"mechanics/{id}.md");
    }

    private sealed class Allow : IInteractionAuthorizationPolicy
    {
        public InteractionAuthorizationDecision Evaluate(InteractionAuthorizationRequest request) =>
            InteractionAuthorizationDecision.Allow(request, "test.evidence");
    }

    private sealed class Registry(ApplicationRevision revision) : IApplicationRegistry
    {
        public ApplicationRevision Register(ApplicationRegistration registration) => throw new NotSupportedException();
        public ApplicationRevision? Get(ApplicationIdentifier applicationId) =>
            applicationId == revision.ApplicationId ? revision : null;
        public ApplicationRegistration? Describe(ApplicationIdentifier applicationId) => null;
        public IReadOnlyList<ApplicationRegistration> List(int limit) => [];
        public ApplicationDiscoveryPage ListPage(string? afterApplicationId, int limit) => new([], null);
    }

    private sealed class Activation(ActiveApplicationManifest value) : IApplicationActivationReader
    {
        public ActiveApplicationManifest? Current(ApplicationIdentifier applicationId) =>
            applicationId == value.ApplicationId ? value : null;
    }

    private sealed class Snapshots(ActiveCatalogFeatureSnapshot value) : IActiveCatalogFeatureSnapshotProvider
    {
        public bool TryGetSnapshot(ApplicationIdentifier applicationId, out ActiveCatalogFeatureSnapshot snapshot)
        {
            snapshot = value;
            return applicationId == value.Manifest.ApplicationId;
        }
    }
}
