using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.CatalogNavigation;
using DantesRoleplay.Interactions;
using DantesRoleplay.Web.Data;
using DantesRoleplay.Web.Pages;

namespace DantesRoleplay.Tests;

public sealed class CompositionQueryMaterializerTests
{
    private static readonly string Hash = new('A', 64);

    [Fact]
    public async Task Preview_combines_one_content_generation_with_exact_selected_queries()
    {
        var document = new WebCompositionParser().Parse("""
            {"formatVersion":1,"generation":"generation-one","queries":[{"name":"record"}],"components":[],
             "root":{"kind":"element","tag":"p","children":[{"kind":"value","path":"record.name"}]}}
            """).Document!;
        var host = Host();
        var adapter = new RecordingAdapter(_ => Read("{\"name\":\"<private>\"}"));
        var preview = new CompositionPagePreview(new(adapter));
        var mismatch = await preview.RenderAsync(document, host, new Dictionary<string, ApplicationReadModelInvocationRequest>());
        Assert.Null(mismatch.Html);
        Assert.Equal("COMPOSITION_QUERY_SELECTION_MISMATCH", Assert.Single(mismatch.Errors).Code);
        Assert.Empty(adapter.Requests);
        var result = await preview.RenderAsync(document, host,
            new Dictionary<string, ApplicationReadModelInvocationRequest> { ["record"] = Request(host) });
        Assert.Equal("generation-one", result.Generation);
        Assert.Equal("<p>&lt;private&gt;</p>", result.Html);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Each_render_reauthorizes_through_adapter_and_never_reuses_another_scope_data()
    {
        var adapter = new RecordingAdapter(request => Read("{\"scope\":\"" + request.Host.StateSpaceId + "\"}"));
        var materializer = new CompositionQueryMaterializer(adapter);
        var first = Host("first");
        var second = Host("second");
        var a = await materializer.ReadAsync(first, new Dictionary<string, ApplicationReadModelInvocationRequest> { ["record"] = Request(first) });
        var b = await materializer.ReadAsync(second, new Dictionary<string, ApplicationReadModelInvocationRequest> { ["record"] = Request(second) });
        Assert.Equal("first", a.Values["record"].GetProperty("scope").GetString());
        Assert.Equal("second", b.Values["record"].GetProperty("scope").GetString());
        Assert.Equal(2, adapter.Requests.Count);
        Assert.All(adapter.Requests, request => Assert.Equal(100, request.PageSize));
        Assert.Same(adapter.Results[0], a.Results["record"]);
    }

    [Fact]
    public async Task Mixed_hosts_or_invalid_later_bindings_fail_before_any_read()
    {
        var adapter = new RecordingAdapter(_ => Read("{}"));
        var host = Host();
        var bindings = new Dictionary<string, ApplicationReadModelInvocationRequest>
        { ["first"] = Request(host), ["other"] = Request(Host("other")) };
        await Assert.ThrowsAsync<ArgumentException>(() => new CompositionQueryMaterializer(adapter).ReadAsync(host, bindings));
        Assert.Empty(adapter.Requests);
        bindings["other"] = Request(host) with { PageSize = 101 };
        await Assert.ThrowsAsync<ArgumentException>(() => new CompositionQueryMaterializer(adapter).ReadAsync(host, bindings));
        Assert.Empty(adapter.Requests);
    }

    [Fact]
    public async Task A_denial_keeps_its_shared_code_and_suppresses_partial_render_values()
    {
        var denied = InteractionInvocationResult.Failed("INVOCATION_NOT_AUTHORIZED", "This read is forbidden.");
        var adapter = new RecordingAdapter(request => request.QualifiedQueryId == "query.denied" ? denied : Read("{\"ok\":true}"));
        var host = Host();
        var result = await new CompositionQueryMaterializer(adapter).ReadAsync(host,
            new Dictionary<string, ApplicationReadModelInvocationRequest>
            { ["first"] = Request(host), ["denied"] = Request(host) with { QualifiedQueryId = "query.denied" } });
        Assert.Empty(result.Values);
        Assert.Same(denied, result.Results["denied"]);
    }

    [Fact]
    public async Task Computation_evidence_cannot_substitute_for_a_registered_read()
    {
        var host = Host();
        var result = await new CompositionQueryMaterializer(new RecordingAdapter(_ =>
            InteractionInvocationResult.CompletedComputation("{}", "worker.output"))).ReadAsync(host,
                new Dictionary<string, ApplicationReadModelInvocationRequest> { ["query"] = Request(host) });
        Assert.Empty(result.Values);
        Assert.Equal("COMPOSITION_READ_REQUIRED", result.Results["query"].Code);
    }

    [Fact]
    public async Task Cursor_is_forwarded_once_and_adapter_errors_are_safe_unavailable_results()
    {
        var adapter = new RecordingAdapter(_ => throw new InvalidOperationException("sensitive internal failure"));
        var host = Host();
        var result = await new CompositionQueryMaterializer(adapter).ReadAsync(host,
            new Dictionary<string, ApplicationReadModelInvocationRequest> { ["query"] = Request(host) with { Cursor = "next", PageSize = 12 } });
        var request = Assert.Single(adapter.Requests);
        Assert.Equal("next", request.Cursor);
        Assert.Equal(12, request.PageSize);
        Assert.Equal("READ_ADAPTER_UNAVAILABLE", result.Results["query"].Code);
        Assert.DoesNotContain("sensitive", result.Results["query"].SafeMessage);
    }

    private static InteractionInvocationResult Read(string json) => InteractionInvocationResult.Completed(json, new(Hash, Hash, Hash, Hash, Hash));
    private static InteractionInvocationHost Host(string scope = "scope") => new(
        TrustedPrincipalContext.VerifiedPrincipal("principal." + new string('a', 64), "test"),
        new(ApplicationIdentifier.Parse("sample"), 1, Hash, []), scope, "grant", "command.read", "revision",
        InteractionExecutionProfile.ReadOnly, new(16, DateTime.UtcNow.AddMinutes(1)));
    private static ApplicationReadModelInvocationRequest Request(InteractionInvocationHost host) => new(host, "query.sample",
        new("projection", "projection.sample", 1, Hash, Hash, "{}", ApplicationQueryExposure.ModelVisible, []),
        new Dictionary<string, string>());

    // Presentation conformance only: these doubles are not production authorization/execution evidence.
    private sealed class RecordingAdapter(Func<ApplicationReadModelInvocationRequest, InteractionInvocationResult> result)
        : IApplicationReadModelInvocationAdapter
    {
        public List<ApplicationReadModelInvocationRequest> Requests { get; } = [];
        public List<InteractionInvocationResult> Results { get; } = [];
        public Task<InteractionInvocationResult> ReadAsync(ApplicationReadModelInvocationRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var value = result(request); Results.Add(value);
            return Task.FromResult(value);
        }
    }
}
