using System.Text.Json;
using DantesRoleplay.AI;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Catalog;
using DantesRoleplay.Interactions;
using DantesRoleplay.Operations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.SystemCapabilities.Tests;

public sealed class ApplicationCandidateCatalogCompareCapabilityTests
{
    private static readonly ApplicationIdentifier Application = ApplicationIdentifier.Parse("candidate-app");
    private static readonly TrustedPrincipalContext Principal = PrivateOperatorPrincipal.Create("test", "operator");
    private static readonly string Hash = new('A', 64);

    [Fact]
    public async Task Website_and_codex_gateway_forward_only_the_bounded_selected_catalog_request()
    {
        await using var fixture = await Fixture.CreateAsync(combinedGrant: true);
        var gateway = fixture.Scope.ServiceProvider.GetRequiredService<IApplicationCandidateCapabilityGateway>();
        var discovery = gateway.Discover(Principal, Application, "catalog-discovery");
        var descriptor = discovery.Capabilities.Single(value =>
            value.Id == SystemCapabilityIds.ApplicationCandidateCatalogCompare);
        var input = Input();

        var website = await gateway.InvokeAsync(Principal, Application, descriptor.Id,
            input, null, "website-catalog-compare");
        var source = new ApplicationCandidateCapabilityAiToolSource(gateway);
        var tool = source.CreateTools(new(
                new("test", "Test", "Test catalog comparison."),
                new("test", "model", [new(AiMessageRole.User, "Compare the selected record")], AiRequestKind.Task),
                new(Principal, ApplicationCandidateCapabilityAccess.Scope, "codex-catalog-compare")
                { ApplicationId = Application }, null, null, () => []))
            .Single(value => value.Definition.Name == "system_application-candidate_catalog-compare");
        var codex = await tool.InvokeAsync(new("call.catalog", tool.Definition.Name,
            JsonSerializer.Deserialize<JsonElement>(input), AiRequestKind.Task));

        Assert.True(discovery.Ok, discovery.Error?.Message);
        Assert.Equal("read", descriptor.Mode);
        Assert.Equal([StandingGrantCapability.Read, StandingGrantCapability.Author],
            descriptor.RequiredStandingGrantCapabilities);
        Assert.False(descriptor.RequiresConfirmation);
        Assert.False(descriptor.RequiresIdempotencyKey);
        Assert.DoesNotContain("relativePath", descriptor.InputSchemaJson, StringComparison.Ordinal);
        Assert.DoesNotContain("filesystem", descriptor.InputSchemaJson, StringComparison.OrdinalIgnoreCase);
        Assert.True(website.Ok, website.Error?.Message);
        Assert.True(codex.Ok, codex.ErrorMessage);
        Assert.Equal(2, fixture.Synchronization.Calls.Count);
        Assert.All(fixture.Synchronization.Calls, call =>
        {
            Assert.Equal(Application, call.Host.ApplicationRevision.ApplicationId);
            Assert.Equal(Principal.PrincipalId, call.Host.Principal.PrincipalId);
            Assert.Equal("grant.catalog@1", call.Host.GrantReference);
            Assert.Equal(InteractionExecutionProfile.Atomic, call.Host.Profile);
            Assert.Equal(1, call.Host.Budget.MaximumOperations);
            Assert.Matches("^[0-9a-f]{32}$", call.Host.CommandId);
            Assert.Equal("catalog-root", call.Request.AllowedRootId);
            Assert.Equal(Hash, call.Request.ExpectedActiveFingerprint);
            var record = Assert.Single(call.Request.Records);
            Assert.Equal(CatalogRecordKind.Procedure, record.Kind);
            Assert.Equal("candidate-app.runtime.inspect", record.Id);
        });
    }

    [Fact]
    public async Task Separate_read_and_author_grants_cannot_be_combined_into_compare_authority()
    {
        await using var fixture = await Fixture.CreateAsync(combinedGrant: false);
        var gateway = fixture.Scope.ServiceProvider.GetRequiredService<IApplicationCandidateCapabilityGateway>();

        var result = await gateway.InvokeAsync(Principal, Application,
            SystemCapabilityIds.ApplicationCandidateCatalogCompare, Input(), null, "split-grants");

        Assert.False(result.Ok);
        Assert.Equal("STANDING_GRANT_DENIED", result.Error?.Code);
        Assert.Empty(fixture.Synchronization.Calls);
    }

    private static string Input() => JsonSerializer.Serialize(new
    {
        applicationId = Application.Value,
        allowedRootId = "catalog-root",
        expectedActiveFingerprint = Hash,
        records = new[] { new { kind = "procedure", id = "candidate-app.runtime.inspect" } }
    });

    private sealed class RecordingSynchronization : ICatalogSynchronizationService
    {
        public List<(InteractionInvocationHost Host, CatalogSynchronizationCompareRequest Request)> Calls { get; } = [];

        public Task<InteractionInvocationResult> CompareAsync(InteractionInvocationHost host,
            CatalogSynchronizationCompareRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add((host, request));
            return Task.FromResult(InteractionInvocationResult.CompletedComputation(
                "{\"status\":\"ready\"}", "catalog-compare-evidence"));
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider provider;
        public AsyncServiceScope Scope { get; }
        public RecordingSynchronization Synchronization { get; }

        private Fixture(SqliteConnection connection, ServiceProvider provider,
            AsyncServiceScope scope, RecordingSynchronization synchronization)
        {
            this.connection = connection;
            this.provider = provider;
            Scope = scope;
            Synchronization = synchronization;
        }

        public static async Task<Fixture> CreateAsync(bool combinedGrant)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var synchronization = new RecordingSynchronization();
            var services = new ServiceCollection();
            services.AddDantesRoleplayDataAccess("Data Source=:memory:");
            services.AddDbContext<DantesRoleplayDbContext>(options => options.UseSqlite(connection));
            services.AddScoped<ICatalogSynchronizationService>(_ => synchronization);
            var provider = services.BuildServiceProvider();
            var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DantesRoleplayDbContext>();
            await db.Database.EnsureCreatedAsync();
            scope.ServiceProvider.GetRequiredService<IApplicationRegistry>().Register(
                new(Application, "Candidate", "Candidate application", []));
            if (combinedGrant)
                await AddGrantAsync(db, "catalog", [StandingGrantCapability.Read, StandingGrantCapability.Author]);
            else
            {
                await AddGrantAsync(db, "read", [StandingGrantCapability.Read]);
                await AddGrantAsync(db, "author", [StandingGrantCapability.Author]);
            }
            return new(connection, provider, scope, synchronization);
        }

        private static async Task AddGrantAsync(DantesRoleplayDbContext db, string id,
            IReadOnlyList<StandingGrantCapability> capabilities)
        {
            var operationId = "grant-issuer-" + id;
            var grant = new StandingGrantRevision($"grant.{id}@1", $"grant.{id}", 1,
                new string('0', 64), Principal.PrincipalId, Application, StandingGrantScope.Application, null,
                capabilities, new(StandingGrantDefinitionMode.ExactIds,
                    ["candidate-app.runtime.inspect"], []), [], 16,
                DateTime.UtcNow.AddMinutes(5), false, operationId);
            grant = grant with { ContentFingerprint = StandingGrantRevisionCanonicalization.ContentFingerprint(grant) };
            db.Add(new Operation { Id = operationId, Timestamp = DateTime.UtcNow, Tool = "test" });
            db.Add(new StandingGrantRevisionRecord
            {
                GrantId = grant.GrantId, Revision = grant.Revision, GrantReference = grant.GrantReference,
                PrincipalReference = grant.PrincipalReference, ApplicationId = grant.ApplicationId.Value,
                Scope = "application", PermissionsJson = StandingGrantRevisionCanonicalization.PermissionsJson(grant),
                ContentFingerprint = grant.ContentFingerprint, MaximumOperations = grant.MaximumOperations,
                ExpiresAtUtc = grant.ExpiresAtUtc, IssuedByOperationId = operationId
            });
            db.Add(new StandingGrantCurrentRecord { GrantId = grant.GrantId, Revision = grant.Revision });
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Scope.DisposeAsync();
            await provider.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
