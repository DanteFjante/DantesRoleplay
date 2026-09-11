using System.Text.Json;
using DantesRoleplay.Applications;
using DantesRoleplay.Authorization;
using DantesRoleplay.DataAccess;
using DantesRoleplay.DataAccess.Composition;
using DantesRoleplay.Interactions;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.SystemCapabilities.Tests;

public sealed class StandingGrantAdministrationSystemCapabilityTests
{
    private static readonly TrustedPrincipalContext Operator =
        PrivateOperatorPrincipal.Create("test", "operator");

    [Fact]
    public async Task Confirmed_operator_capability_forwards_exact_mutation_to_existing_owner()
    {
        var owner = new RecordingOwner();
        var services = new ServiceCollection();
        services.AddDantesRoleplayDataAccess("Data Source=:memory:");
        services.AddScoped<IStandingGrantAdministration>(_ => owner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISystemCapabilityCatalog>();
        var context = new SystemCapabilityInvocationContext(
            Operator, PrivateOperatorAuthorizationPolicy.PrivateHostScope, "grant-admin-test");
        var descriptor = catalog.Discover(context).Capabilities.Single(value =>
            value.Id == SystemCapabilityIds.StandingGrantAdmin);
        var input = Input();

        var preflight = await catalog.PreflightWriteAsync(
            descriptor.Id, descriptor.Fingerprint, input, [], context);
        Assert.True(preflight.Ok, preflight.Error?.Message);
        var result = await catalog.ExecuteWriteAsync(
            descriptor.Id,
            descriptor.Fingerprint,
            input,
            new(context, "0123456789abcdef0123456789abcdef", "Issue the reviewed application grant.",
                descriptor.ProcedureIds, preflight.AuthorizationEvidence,
                preflight.Preflight!.ExecutionEvidenceJson));

        Assert.True(result.Ok, result.Error?.Message);
        var call = Assert.Single(owner.Calls);
        Assert.Equal(Operator.PrincipalId, call.Principal.PrincipalId);
        Assert.Equal("authors", call.Request.GrantId);
        Assert.Equal("0123456789abcdef0123456789abcdef", call.CommandId);
        Assert.Equal("candidate-app.runtime.roll", Assert.Single(call.Request.Definitions.ExactIds));
    }

    [Fact]
    public async Task Unauthenticated_context_cannot_reach_grant_owner()
    {
        var owner = new RecordingOwner();
        var services = new ServiceCollection();
        services.AddDantesRoleplayDataAccess("Data Source=:memory:");
        services.AddScoped<IStandingGrantAdministration>(_ => owner);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISystemCapabilityCatalog>();
        var context = new SystemCapabilityInvocationContext(
            TrustedPrincipalContext.Unauthenticated("TEST_UNAUTHENTICATED"),
            PrivateOperatorAuthorizationPolicy.PrivateHostScope, "grant-admin-test");

        var result = await catalog.PreflightWriteAsync(
            SystemCapabilityIds.StandingGrantAdmin, new string('A', 64), Input(), [], context);

        Assert.False(result.Ok);
        Assert.Equal("PRIVATE_OPERATOR_UNAUTHENTICATED", result.Error?.Code);
        Assert.Empty(owner.Calls);
    }

    private static string Input() => JsonSerializer.Serialize(new
    {
        mutation = "issue",
        grantId = "authors",
        expectedCurrentRevision = 0,
        principalReference = "principal." + new string('a', 64),
        applicationId = "candidate-app",
        scope = "application",
        stateSpaceId = (string?)null,
        capabilities = new[] { "author", "validate", "activate", "read" },
        definitions = new
        {
            mode = "exactIds",
            exactIds = new[] { "candidate-app.runtime.roll" },
            applicationOwnedNamespaces = Array.Empty<object>()
        },
        effectKinds = Array.Empty<string>(),
        maximumOperations = 16,
        expiresAtUtc = DateTime.UtcNow.AddHours(1)
    });

    private sealed class RecordingOwner : IStandingGrantAdministration
    {
        public List<(TrustedPrincipalContext Principal, StandingGrantMutationRequest Request, string CommandId)> Calls { get; } = [];

        public Task<InteractionInvocationResult> MutateAsync(
            TrustedPrincipalContext issuer,
            StandingGrantMutationRequest request,
            string commandId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((issuer, request, commandId));
            return Task.FromResult(InteractionInvocationResult.Committed(new(
                "0123456789abcdef0123456789abcdef", new string('A', 64), [], false)));
        }
    }
}
