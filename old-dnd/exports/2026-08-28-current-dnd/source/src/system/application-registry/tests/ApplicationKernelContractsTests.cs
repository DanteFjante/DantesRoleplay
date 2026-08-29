using System.Text;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Projections;
using DantesRoleplay.Sources;

namespace DantesRoleplay.Tests;

public sealed class ApplicationKernelContractsTests
{
    private static readonly string Hash = new('A', 64);

    [Fact]
    public void Application_ids_reserve_system_and_registration_is_idempotent()
    {
        Assert.Throws<ArgumentException>(() => ApplicationIdentifier.Parse("system"));
        Assert.Throws<ArgumentException>(() => ApplicationIdentifier.Parse("Not-valid"));

        var registry = new InMemoryApplicationRegistry();
        var id = ApplicationIdentifier.Parse("fixture");
        var request = new ApplicationRegistration(id, "Fixture", "", []);
        var first = registry.Register(request);

        Assert.Equal(first, registry.Register(request));
        Assert.Throws<InvalidOperationException>(() => registry.Register(request with { DisplayName = "Changed" }));
    }

    [Fact]
    public void Application_registration_rejects_unknown_or_self_base_without_mutation()
    {
        var registry = new InMemoryApplicationRegistry();
        var app = ApplicationIdentifier.Parse("fixture");
        Assert.Throws<ArgumentException>(() => registry.Register(new(app, "Fixture", "", [ApplicationIdentifier.Parse("missing")])));
        Assert.Null(registry.Get(app));
        Assert.Throws<ArgumentException>(() => registry.Register(new(app, "Fixture", "", [app])));
    }

    [Fact]
    public void Application_registration_copies_mutable_input_and_returns_read_only_revisions()
    {
        var registry = new InMemoryApplicationRegistry();
        var bases = new List<ApplicationIdentifier>();
        var app = ApplicationIdentifier.Parse("fixture");
        var revision = registry.Register(new(app, "Fixture", "", bases));

        bases.Add(ApplicationIdentifier.Parse("later"));

        Assert.Empty(registry.Get(app)!.BaseApplications);
        Assert.Throws<NotSupportedException>(() => ((IList<ApplicationIdentifier>)revision.BaseApplications).Add(app));
    }

    [Fact]
    public void Application_fingerprint_preserves_meaningful_base_order()
    {
        var first = ApplicationIdentifier.Parse("first-base");
        var second = ApplicationIdentifier.Parse("second-base");
        var app = ApplicationIdentifier.Parse("fixture");

        var left = new InMemoryApplicationRegistry();
        left.Register(new(first, "First", "", []));
        left.Register(new(second, "Second", "", []));
        var leftRevision = left.Register(new(app, "Fixture", "", [first, second]));

        var right = new InMemoryApplicationRegistry();
        right.Register(new(first, "First", "", []));
        right.Register(new(second, "Second", "", []));
        var rightRevision = right.Register(new(app, "Fixture", "", [second, first]));

        Assert.NotEqual(leftRevision.Fingerprint, rightRevision.Fingerprint);

        var collisionLeft = new InMemoryApplicationRegistry().Register(new(app, "A\nB", "C", []));
        var collisionRight = new InMemoryApplicationRegistry().Register(new(app, "A", "B\nC", []));
        Assert.NotEqual(collisionLeft.Fingerprint, collisionRight.Fingerprint);
    }

    [Fact]
    public void State_space_binding_requires_exact_immutable_fingerprints()
    {
        var app = ApplicationIdentifier.Parse("fixture");
        var revision = new ApplicationRevision(app, 1, Hash, []);
        var binding = new StateSpaceBinding("campaign-one", revision, Hash);

        Assert.Equal("campaign-one", binding.StateSpaceId);
        Assert.Throws<ArgumentException>(() => new StateSpaceBinding("", revision, Hash));
        Assert.Throws<ArgumentException>(() => new StateSpaceBinding("campaign", revision, "not-a-hash"));
        Assert.Throws<ArgumentException>(() => new StateSpaceBinding("campaign", revision with { Revision = 0 }, Hash));
    }

    [Fact]
    public void Sources_reject_unsafe_paths_and_lower_trust_override_without_mutation()
    {
        var app = ApplicationIdentifier.Parse("fixture");
        var registry = new InMemorySourceRegistry();
        var trusted = new SourceRegistration(app, "core", "workspace", "catalog/**/*.json", SourceTrust.Trusted, 1, "component:fixture.stats");
        registry.Register(trusted);

        Assert.Throws<ArgumentException>(() => registry.Register(trusted with { SourceId = "unsafe", RelativePathOrGlob = "../catalog" }));
        Assert.Throws<InvalidOperationException>(() => registry.Register(trusted with { SourceId = "untrusted", Trust = SourceTrust.Untrusted, Precedence = 2 }));
        Assert.Equal([trusted], registry.For(app));
        Assert.Throws<InvalidOperationException>(() => registry.Register(trusted with { SourceId = "equal", Precedence = 1 }));
        Assert.Equal([trusted], registry.For(app));
    }

    [Fact]
    public void Component_types_require_qualified_owner_and_immutable_contiguous_versions()
    {
        var app = ApplicationIdentifier.Parse("fixture");
        var registry = new InMemoryComponentTypeRegistry();
        Assert.Throws<ArgumentException>(() => registry.Define(new ComponentTypeVersion(app, "other.stats", 1, Hash)));
        var first = new ComponentTypeVersion(app, "fixture.stats", 1, Hash);
        Assert.Equal(first, registry.Define(first));
        Assert.Throws<InvalidOperationException>(() => registry.Define(first with { SchemaHash = new string('B', 64) }));
        Assert.Throws<InvalidOperationException>(() => registry.Define(first with { Version = 3 }));
        Assert.NotNull(registry.Define(first with { Version = 2 }));
    }

    [Fact]
    public void Projection_validation_builds_stable_reverse_edges_and_rejects_cycles_or_undeclared_mapping()
    {
        var app = ApplicationIdentifier.Parse("fixture");
        var type = new ComponentTypeVersion(app, "fixture.stats", 1, Hash);
        var baseProjection = new ProjectionDefinition(app, "fixture.base", 1, [new("subject", type, "/strength")], [], [new("subject", "/strength", "/score")]);
        var consumer = new ProjectionDefinition(app, "fixture.consumer", 1, [new("subject", type, "/name")], [new("fixture.base", 1)], [new("subject", "/name", "/name")]);

        var graph = ProjectionValidator.Validate([consumer, baseProjection]);
        Assert.Equal(["fixture.consumer@1"], graph.Reverse["fixture.base@1"]);

        Assert.Throws<ArgumentException>(() => ProjectionValidator.Validate([baseProjection with { Dependencies = [new("fixture.consumer", 1)] }, consumer with { Dependencies = [new("fixture.base", 1)] }]));
        Assert.Throws<ArgumentException>(() => ProjectionValidator.Validate([baseProjection with { Mappings = [new("missing", "/x", "/x")] }]));
        Assert.Throws<ArgumentException>(() => ProjectionValidator.Validate([baseProjection with { Sources = [new("subject", type, "not-a-pointer")] }]));
        Assert.Throws<ArgumentException>(() => ProjectionValidator.Validate([baseProjection with { Sources = [new("subject", type with { Owner = ApplicationIdentifier.Parse("other") }, "/strength")] }]));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, IReadOnlyList<string>>)graph.Reverse).Add("other", []));

        var other = ApplicationIdentifier.Parse("other");
        var otherProjection = baseProjection with
        {
            Owner = other,
            QualifiedId = "other.base",
            Sources = [new("subject", type with { Owner = other, QualifiedId = "other.stats" }, "/strength")]
        };
        var hiddenCrossApplication = consumer with { Dependencies = [new("other.base", 1)] };
        Assert.Throws<ArgumentException>(() => ProjectionValidator.Validate([otherProjection, hiddenCrossApplication]));
        Assert.NotNull(ProjectionValidator.Validate([otherProjection, hiddenCrossApplication], [other]));
    }

    [Fact]
    public void Catalog_cursors_are_authenticated_and_manifest_bound()
    {
        Assert.Throws<ArgumentException>(() => new CatalogCursorCodec(new byte[31]));
        var codec = new CatalogCursorCodec(Encoding.UTF8.GetBytes("this-is-a-test-key-with-enough-bytes"));
        var binding = new CatalogCursorBinding("manifest-a", "fixture", "components", "", "all", "v1", 25, "fixture.stats");
        var cursor = codec.Encode(binding);

        Assert.Equal(binding, codec.Decode(cursor, binding));
        Assert.Throws<InvalidOperationException>(() => codec.Decode(cursor, binding with { ManifestFingerprint = "manifest-b" }));
        Assert.Throws<ArgumentException>(() => codec.Decode(cursor[..^1] + "x", binding));
    }
}
