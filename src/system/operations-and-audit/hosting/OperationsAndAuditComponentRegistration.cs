using DantesRoleplay.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess.Composition;

internal static class OperationsAndAuditComponentRegistration
{
    internal static IServiceCollection AddOperationsAndAuditComponent(this IServiceCollection services)
    {
        services.AddScoped<IOperationLog, OperationLog>();
        return services;
    }
}
