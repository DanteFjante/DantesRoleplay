using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Ecs;
using DantesRoleplay.Mechanics;

namespace DantesRoleplay.ApplicationExecution.Tests;

public sealed class ApplicationMechanicGenerationTests
{
    [Fact]
    public async Task One_navigator_is_pinned_across_root_and_child_and_next_invocation_gets_new_generation()
    {
        var app = ApplicationIdentifier.Parse("fixture");
        var first = Manifest(app, "first", "return {data:{generation:'first'}};", includeChild: true);
        var second = Manifest(app, "second", "return {data:{generation:'second'}};", includeChild: false);
        var provider = new Generations(first, second);
        var evaluator = new ApplicationMechanicEvaluator(provider, new EmptyResolver(), new JintMechanicEngine());
        var mapping = new ApplicationMechanicProjectionMapping(new Dictionary<string, EcsComponentReference>(), new Dictionary<string, string>());

        var firstResult = await evaluator.EvaluateAsync(new("space", app, "fixture.mechanic.root",
            Fingerprint(first, "fixture.mechanic.root"), mapping, new Dictionary<string, string>(), "{}", 1));
        var secondResult = await evaluator.EvaluateAsync(new("space", app, "fixture.mechanic.root",
            Fingerprint(second, "fixture.mechanic.root"), mapping, new Dictionary<string, string>(), "{}", 2));

        Assert.True(firstResult.Ok, string.Join(";", firstResult.Problems));
        Assert.True(secondResult.Ok, string.Join(";", secondResult.Problems));
        Assert.Equal(2, provider.Lookups);
        Assert.Contains("first", firstResult.Run!.Output.Data);
        var childResult = Assert.Single(firstResult.Projection!.Children["child"]);
        Assert.Contains("first-child", childResult.Output.Data);
        Assert.Contains("second", secondResult.Run!.Output.Data);
    }

    [Fact]
    public async Task Unavailable_or_stale_current_selection_fails_without_fallback()
    {
        var app = ApplicationIdentifier.Parse("fixture");
        var manifest = Manifest(app, "current", "return {data:{ok:true}};", includeChild: false);
        var mapping = new ApplicationMechanicProjectionMapping(new Dictionary<string, EcsComponentReference>(), new Dictionary<string, string>());
        var unavailable = new ApplicationMechanicEvaluator(new Generations(null, manifest), new EmptyResolver(), new JintMechanicEngine());
        var missing = await unavailable.EvaluateAsync(new("space", app, "fixture.mechanic.root", Fingerprint(manifest, "fixture.mechanic.root"), mapping, new Dictionary<string, string>(), "{}", 1));
        Assert.Contains("APPLICATION_CATALOG_UNAVAILABLE", missing.Problems.Single());

        var stale = new ApplicationMechanicEvaluator(new Generations(manifest, manifest), new EmptyResolver(), new JintMechanicEngine());
        var result = await stale.EvaluateAsync(new("space", app, "fixture.mechanic.root", new string('A', 64), mapping, new Dictionary<string, string>(), "{}", 1));
        Assert.Contains("MECHANIC_STALE", result.Problems.Single());
    }

    [Fact]
    public async Task In_flight_invocation_keeps_its_generation_while_next_invocation_advances()
    {
        var app = ApplicationIdentifier.Parse("fixture");
        var first = Manifest(app, "first", "return {data:{generation:'first'}};", includeChild: true);
        var second = Manifest(app, "second", "return {data:{generation:'second'}};", includeChild: false);
        var provider = new Generations(first, second);
        var resolver = new BlockingResolver();
        var evaluator = new ApplicationMechanicEvaluator(provider, resolver, new JintMechanicEngine());
        var mapping = new ApplicationMechanicProjectionMapping(
            new Dictionary<string, EcsComponentReference>(), new Dictionary<string, string>());

        var pending = evaluator.EvaluateAsync(new(
            "space", app, "fixture.mechanic.root", Fingerprint(first, "fixture.mechanic.root"),
            mapping, new Dictionary<string, string>(), "{}", 1));
        await resolver.Entered.Task;

        var next = await evaluator.EvaluateAsync(new(
            "space", app, "fixture.mechanic.root", Fingerprint(second, "fixture.mechanic.root"),
            mapping, new Dictionary<string, string>(), "{}", 2));
        resolver.Release();
        var original = await pending;

        Assert.True(original.Ok, string.Join(";", original.Problems));
        Assert.True(next.Ok, string.Join(";", next.Problems));
        Assert.Contains("first", original.Run!.Output.Data);
        Assert.Contains("first-child", Assert.Single(original.Projection!.Children["child"]).Output.Data);
        Assert.Contains("second", next.Run!.Output.Data);
        Assert.Equal(2, provider.Lookups);
    }

    private static CatalogNavigationManifest Manifest(ApplicationIdentifier app, string generation, string source, bool includeChild)
    {
        var root = Record(app, "root", source, includeChild ? "{\"children\":{\"child\":{\"mechanicId\":\"mechanic.child\",\"roleBindings\":{},\"inheritInput\":false,\"input\":\"{}\"}}}" : "{}");
        var records = includeChild ? new[] { root, Record(app, "child", "return {data:{generation:'first-child'}};", "{}") } : new[] { root };
        return CatalogNavigationManifest.Create(app, Hash(generation), "catalog-lexical-v1", [new(app.Value, "Fixture", "Fixture")],
            [new(app.Value, "", "Fixture", "Fixture", CatalogDescriptionStatus.Authored), new(app.Value, "mechanics", "Mechanics", "Mechanics", CatalogDescriptionStatus.Authored)], records);
    }

    private static CatalogRecordDefinition Record(ApplicationIdentifier app, string id, string source, string requirements)
    {
        var content = JsonSerializer.Serialize(new { requirements, source });
        return new(app.Value, "mechanic", app.Value + ".mechanic." + id, id, id, [], [], "mechanics", "active", 1, content, Hash(content), "catalog", "mechanics/" + id + ".md");
    }
    private static string Fingerprint(CatalogNavigationManifest manifest, string id) => manifest.Records.Single(x => x.QualifiedId == id).ContentFingerprint;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class Generations(CatalogNavigationManifest? first, CatalogNavigationManifest second) : IPublicApplicationCatalogProvider
    {
        public int Lookups { get; private set; }
        public bool TryGet(ApplicationIdentifier id, out ICatalogNavigator navigator)
        {
            Lookups++;
            var manifest = Lookups == 1 ? first : second;
            if (manifest is null) { navigator = null!; return false; }
            navigator = new InMemoryCatalogNavigator(manifest,
                new CatalogCursorCodec(Enumerable.Repeat((byte)0x31, 32).ToArray()));
            return true;
        }
    }

    private sealed class EmptyResolver : IApplicationMechanicProjectionResolver
    {
        public Task<ProjectionResult> ResolveAsync(string stateSpaceId, ApplicationIdentifier applicationId, MechanicRequirements requirements, ApplicationMechanicProjectionMapping mapping, IReadOnlyDictionary<string, string> roleAssignments, string inputJson, long seed, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectionResult(new MechanicProjection { Input = inputJson }, []));
    }

    private sealed class BlockingResolver : IApplicationMechanicProjectionResolver
    {
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        public TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => _release.TrySetResult(true);

        public async Task<ProjectionResult> ResolveAsync(
            string stateSpaceId, ApplicationIdentifier applicationId, MechanicRequirements requirements,
            ApplicationMechanicProjectionMapping mapping, IReadOnlyDictionary<string, string> roleAssignments,
            string inputJson, long seed, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                Entered.TrySetResult(true);
                await _release.Task.WaitAsync(cancellationToken);
            }
            return new(new MechanicProjection { Input = inputJson }, []);
        }
    }
}
