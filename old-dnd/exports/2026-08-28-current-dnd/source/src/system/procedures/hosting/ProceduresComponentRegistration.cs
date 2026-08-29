using DantesRoleplay.Procedures;
using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.DataAccess.Composition;

internal static class ProceduresComponentRegistration
{
    internal static IServiceCollection AddProceduresComponent(this IServiceCollection services)
    {
        services.AddScoped<IProcedureStore, ProcedureStore>();
        return services;
    }
}
