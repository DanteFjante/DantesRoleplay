using Microsoft.Extensions.DependencyInjection;

namespace DantesRoleplay.SchemaValidation;

internal static class SchemaValidationComponentRegistration
{
    internal static IServiceCollection AddSchemaValidationComponent(this IServiceCollection services)
    {
        services.AddSingleton<IBoundedJsonSchemaValidator, BoundedJsonSchemaValidator>();
        return services;
    }
}
