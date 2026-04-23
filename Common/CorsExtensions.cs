using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Common;

public static class CorsExtensions {
    public const string TauriCorsPolicyName = "TauriCorsPolicy";

    public static IServiceCollection AddTauriCors(this IServiceCollection services) {
        services.AddCors(options => {
            options.AddPolicy(TauriCorsPolicyName, ConfigureTauriCorsPolicy);
        });
        return services;
    }

    private static void ConfigureTauriCorsPolicy(CorsPolicyBuilder builder) {
        builder.WithOrigins(
                "https://tauri.localhost",
                "http://tauri.localhost",
                "tauri://localhost"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    }
}
