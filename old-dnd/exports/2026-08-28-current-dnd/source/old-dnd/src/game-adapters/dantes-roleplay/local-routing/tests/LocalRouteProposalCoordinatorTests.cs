using DantesRoleplay.Actions;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Retrieval;
using DantesRoleplay.Mechanics;
using DantesRoleplay.Procedures;
using DantesRoleplay.Retrieval;
using DantesRoleplay.World;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Tests;

public sealed class LocalRouteProposalCoordinatorTests
{
    private const string MechanicId = "mechanic.test.open-door";
    private const string ProcedureId = "procedure.test.doors";

    [Fact]
    public void Sqlite_registration_resolves_both_internal_slice_5c_coordinators()
    {
        using var services = new ServiceCollection()
            .AddDantesRoleplayDataAccess("Data Source=:memory:")
            .BuildServiceProvider();
        using var scope = services.CreateScope();

        Assert.IsType<KnowledgeReadAgentCoordinator>(
            scope.ServiceProvider.GetRequiredService<IKnowledgeReadAgentCoordinator>());
        Assert.IsType<LocalRouteProposalCoordinator>(
            scope.ServiceProvider.GetRequiredService<ILocalRouteProposalCoordinator>());
    }

    [Fact]
    public async Task Host_builds_and_read_validates_exact_action_without_executing_it()
    {
        var projection = new Projection(new(new MechanicProjection(), []));
        var coordinator = Coordinator(
            """{"status":"action","mechanicId":"mechanic.test.open-door","procedureIds":["procedure.test.doors"],"confidence":"high","reason":"Exact registered match."}""",
            projection: projection);
        var roles = new Dictionary<string, string> { ["actor"] = "actor.test" };

        var result = await coordinator.ProposeAsync(new("open the door", roles, "{}", "test"));

        Assert.Equal("proposed", result.Status);
        Assert.NotNull(result.Proposal);
        Assert.Equal(MechanicId, result.Proposal!.MechanicId);
        Assert.Equal(roles, result.Proposal.RoleEntityIds);
        Assert.Equal([ProcedureId], result.Proposal.ProceduresUsed);
        Assert.Equal(1, projection.Calls);
    }

    [Theory]
    [InlineData("mechanic.test.invented", "procedure.test.doors")]
    [InlineData("mechanic.test.open-door", "procedure.test.invented")]
    public async Task Model_cannot_invent_mechanics_or_procedures(string mechanicId, string procedureId)
    {
        var projection = new Projection(new(new MechanicProjection(), []));
        var coordinator = Coordinator(
            $$"""{"status":"action","mechanicId":"{{mechanicId}}","procedureIds":["{{procedureId}}"],"confidence":"high","reason":"Try it."}""",
            projection: projection);

        var result = await coordinator.ProposeAsync(new(
            "open the door", new Dictionary<string, string> { ["actor"] = "actor.test" }, Scope: "test"));

        Assert.Equal("unknown", result.Status);
        Assert.Equal("LOCAL_MODEL_SEMANTIC_INVALID", result.FallbackCode);
        Assert.Null(result.Proposal);
        Assert.Equal(0, projection.Calls);
    }

    [Fact]
    public async Task Missing_or_unknown_roles_return_questions_and_never_propose_a_write()
    {
        var projection = new Projection(ProjectionResult.Failed(
            "MISSING_REQUIRED_ROLE: Role 'actor' is required."));
        var coordinator = Coordinator(
            """{"status":"action","mechanicId":"mechanic.test.open-door","procedureIds":[],"confidence":"medium","reason":"The rule matches."}""",
            projection: projection);

        var result = await coordinator.ProposeAsync(new("open the door", Scope: "test"));

        Assert.Equal("needs-input", result.Status);
        Assert.Null(result.Proposal);
        Assert.Contains(result.MissingInformation, value => value.Contains("actor", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Changed_mechanic_is_rejected_as_stale()
    {
        var store = new Mechanics(staleOnFinalRead: true);
        var coordinator = Coordinator(
            """{"status":"action","mechanicId":"mechanic.test.open-door","procedureIds":[],"confidence":"high","reason":"Exact match."}""",
            mechanics: store);

        var result = await coordinator.ProposeAsync(new(
            "open the door", new Dictionary<string, string> { ["actor"] = "actor.test" }, Scope: "test"));

        Assert.Equal("ROUTE_INPUT_STALE", result.FallbackCode);
        Assert.Null(result.Proposal);
    }

    [Fact]
    public async Task Live_qwen3_8b_proposes_only_registered_top_action_when_enabled()
    {
        if (Environment.GetEnvironmentVariable("DANTESROLEPLAY_OLLAMA_COMPLETION") != "1") return;
        var coordinator = new LocalRouteProposalCoordinator(
            new Mechanics(),
            new Procedures(),
            new Projection(new(new MechanicProjection(), [])),
            new OllamaStructuredCompletionProvider(new HttpClient(), new()
            {
                Enabled = true,
                Model = "qwen3:8b",
                Timeout = TimeSpan.FromMinutes(2)
            }));

        var result = await coordinator.ProposeAsync(new(
            "open the door",
            new Dictionary<string, string> { ["actor"] = "actor.test" },
            Scope: "test"));

        Assert.Equal("proposed", result.Status);
        Assert.Equal(MechanicId, result.Proposal!.MechanicId);
    }

    private static LocalRouteProposalCoordinator Coordinator(
        string json,
        Mechanics? mechanics = null,
        Projection? projection = null) =>
        new(
            mechanics ?? new(),
            new Procedures(),
            projection ?? new(new(new MechanicProjection(), [])),
            new Completion(json));

    private sealed class Completion(string json) : ILocalStructuredCompletionProvider
    {
        public Task<StructuredCompletionResult> CompleteAsync(StructuredCompletionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StructuredCompletionResult(new("test", "qwen3:8b", "digest"), json, 5));
        public Task<LocalModelStatus> CheckAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Projection(ProjectionResult result) : IProjectionResolver
    {
        public int Calls { get; private set; }
        public Task<ProjectionResult> ResolveAsync(MechanicRequirements requirements, IReadOnlyDictionary<string, string> roleAssignments, string input = "{}", long seed = 0, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class Mechanics(bool staleOnFinalRead = false) : IMechanicStore
    {
        private int _reads;
        private static readonly MechanicSummary Summary = new(
            MechanicId, "test", "Open door", "Open one door.", "open the door", "test", MechanicStatus.Active, 1);
        public Task<IReadOnlyList<MechanicSummary>> FindAsync(string? query = null, string? category = null, string? scope = null, bool includeInactive = false, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MechanicSummary>>([Summary]);
        public Task<MechanicDetail?> GetAsync(string id, int? version = null, CancellationToken cancellationToken = default)
        {
            _reads++;
            var detail = new MechanicDetail(
                MechanicId, "test", "Open door", "Open one door.", "open the door",
                "{\"roles\":{\"actor\":{\"components\":[],\"description\":\"Who opens the door.\"}}}",
                "return {};", "test", MechanicStatus.Active, 1, 1, "test", "", DateTime.UtcNow)
            { SourceHash = staleOnFinalRead && _reads > 1 ? new string('b', 64) : new string('a', 64) };
            return Task.FromResult<MechanicDetail?>(detail);
        }
        public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WriteMechanicResult> WriteAsync(WriteMechanicRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MechanicCheck>> CheckAsync(WriteMechanicRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MechanicCategoryCount>> GetCategoriesAsync(bool includeInactive = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Procedures : IProcedureStore
    {
        private static readonly ProcedureSummary Summary = new(
            ProcedureId, "test", "Doors", "How doors work.", "commit action", ProcedureStatus.Active, 1);
        public Task<IReadOnlyList<ProcedureSummary>> FindAsync(string? query = null, string? category = null, bool includeInactive = false, int limit = 200, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProcedureSummary>>([Summary]);
        public Task<ProcedureDetail?> GetAsync(string id, int? version = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProcedureDetail?>(new(
                ProcedureId, "test", "Doors", "How doors work.", "commit action", "Read.", "Safe.",
                ProcedureStatus.Active, 1, 1, "test", "", DateTime.UtcNow)
            { SourceHash = new string('c', 64) });
        public Task<WriteProcedureResult> WriteAsync(WriteProcedureRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProcedureSummary>> GetVersionsAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProcedureCategoryCount>> GetCategoriesAsync(bool includeInactive = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WriteCheck>> CheckAsync(WriteProcedureRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
