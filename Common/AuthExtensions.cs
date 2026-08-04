using Common.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Common;

public static class AuthExtensions {
    private const string KeycloakAuthenticationSectionName = "KeycloakAuthentication";
    private const string KeycloakServiceHttpKey            = "services:keycloak:http:0";
    private const string KeycloakServiceHttpsKey           = "services:keycloak:https:0";
    private const string KeycloakServiceLegacyHttpKey      = "KEYCLOAK_HTTP";

    public static IServiceCollection AddKeycloakAuthentication(
        this IServiceCollection services,
        IConfiguration          configuration
    ) {
        services.Configure<KeycloakAuthenticationOptions>(configuration.GetSection(KeycloakAuthenticationSectionName));

        // Keycloak is considered configured when Aspire injected a service reference
        // (services:keycloak:http:0 / services:keycloak:https:0) or the legacy
        // KEYCLOAK_HTTP variable. Without a reference authentication degrades
        // gracefully: no scheme is registered and startup logs a warning.
        bool keycloakConfigured =
            !string.IsNullOrWhiteSpace(configuration[KeycloakServiceHttpKey]) ||
            !string.IsNullOrWhiteSpace(configuration[KeycloakServiceHttpsKey]) ||
            !string.IsNullOrWhiteSpace(configuration[KeycloakServiceLegacyHttpKey]);

        if (!keycloakConfigured) {
            services.AddLogging();
            services.AddSingleton<IStartupFilter>(_ => new MissingKeycloakAuthenticationConfigurationStartupFilter());
            return services;
        }

        // The authority is resolved via service discovery
        // ("https+http://keycloak/realms/makerverse") against the endpoint
        // injected by WithReference(keycloak), so no URL is hardcoded here.
        services.AddAuthentication()
            .AddKeycloakJwtBearer(
                "keycloak",
                "makerverse",
                options => {
                    // Dev runs serve Keycloak over plain HTTP.
                    options.RequireHttpsMetadata = false;
                    options.Audience             = "makerverse";
                }
            );

        return services;
    }

    private sealed class MissingKeycloakAuthenticationConfigurationStartupFilter : IStartupFilter {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) {
            return app => {
                ILogger logger = app.ApplicationServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(AuthExtensions));
                logger.LogWarning(
                    "Keycloak authentication is not configured. Add a WithReference(keycloak) service reference to enable it."
                );
                next(app);
            };
        }
    }
}