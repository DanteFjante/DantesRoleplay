using DantesRoleplay.ApplicationActivation;
using DantesRoleplay.ApplicationExecution;
using DantesRoleplay.MCPServer;
using DantesRoleplay.SystemCapabilities;
using DantesRoleplay.SystemTasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.Interactions.Tests;

public sealed class InteractionFoundationRegistrationTests
{
    [Fact]
    public async Task Production_composition_resolves_real_adapters_and_truthful_optional_boundaries()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddDantesRoleplayMcpServer(":memory:",
            blobStorageRoot: Path.Combine(Path.GetTempPath(), "foundation-di-" + Guid.NewGuid().ToString("N")));
        await using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;

        Assert.IsType<ApplicationReadModelInvocationAdapter>(services.GetRequiredService<IApplicationReadModelInvocationAdapter>());
        Assert.IsType<ApplicationActionInvocationAdapter>(services.GetRequiredService<IApplicationActionInvocationAdapter>());
        Assert.IsType<UnavailableSystemTaskDurableService>(services.GetRequiredService<ISystemTaskDurableService>());
        Assert.IsType<UnavailableSystemInnerWorkerService>(services.GetRequiredService<ISystemInnerWorkerService>());
        var activation = services.GetRequiredService<IApplicationActivationService>();
        Assert.Same(activation, services.GetRequiredService<IActivatedApplicationEvidenceReader>());
        Assert.Same(activation, services.GetRequiredService<IApplicationDefinitionChangeReader>());
    }
}
