using System.Text;
using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNamespaces;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace DantesRoleplay.Authorization.Tests;

public sealed partial class SqliteStandingGrantTargetResolverTests
{
    [Fact]
    public async Task Large_retained_catalog_uses_exact_locator_and_rechecks_only_selected_evidence()
    {
        await using var db = fixture.CreateContext();
        var setup = Setup(db);
        await ActivateAsync(setup);
        var first = await setup.Resolver.ResolveCurrentAsync(Host(setup), "demo.runtime.inspect", CatalogNamespaceKinds.Procedure);
        var expected = first.Target!.ContentFingerprint;
        var previous = setup.Activation.Current(Application)!.ActivationFingerprint;
        for (var index = 0; index < 160; index++)
        {
            var path = Path.Combine(root, "content", "procedures", $"extra-{index}.md");
            File.WriteAllText(path, File.ReadAllText(Path.Combine(root, RelativePath))
                .Replace("demo.runtime.inspect", $"demo.runtime.extra-{index}", StringComparison.Ordinal), new UTF8Encoding(false));
        }
        await ActivateAsync(setup, previous);
        var counted = new CountingSelectedEvidence(setup.Activation);
        var materializer = new ActivatedApplicationCatalogMaterializer(setup.Applications, setup.Activation, setup.Sources, setup.Roots, setup.Extensions)
            .UsePreparationCache(new ActivatedApplicationCatalogSnapshotCache(), new ActivatedApplicationCatalogCacheAuthority());
        var resolver = new SqliteStandingGrantTargetResolver(db, setup.Applications, setup.Activation, counted,
            setup.Sources, setup.Extensions, setup.Namespaces, materializer);
        var result = await resolver.ResolveCurrentAsync(Host(setup), "demo.runtime.inspect", CatalogNamespaceKinds.Procedure);
        Assert.Equal(StandingGrantTargetResolutionStatus.Available, result.Status);
        Assert.Equal(expected, result.Target!.ContentFingerprint);
        Assert.Equal(1, counted.Reads);

        // A hot locator is only a location hint. Unrelated bytes need not be fetched again,
        // while the exact selected retained bytes are revalidated on every permission decision.
        var unrelated = await db.Set<ApplicationActivationDocumentEvidenceRecord>()
            .SingleAsync(value => value.RelativePath == "content/procedures/extra-159.md");
        unrelated.RetainedBytes = null;
        await db.SaveChangesAsync();
        counted.Reads = 0;
        var repeated = await resolver.ResolveAsync(Host(setup), Selection(result.Target));
        Assert.Equal(StandingGrantTargetResolutionStatus.Available, repeated.Status);
        Assert.Equal(result.Target, repeated.Target);
        Assert.Equal(1, counted.Reads);
    }

    private sealed class CountingSelectedEvidence(IActivatedApplicationEvidenceReader inner) : IActivatedApplicationEvidenceReader
    {
        internal int Reads { get; set; }
        public ActivatedApplicationDocumentEvidence? ReadDocumentEvidence(ApplicationIdentifier applicationId, int activationRevision, string logicalIdentity)
        {
            Reads++;
            return inner.ReadDocumentEvidence(applicationId, activationRevision, logicalIdentity);
        }
    }
}
