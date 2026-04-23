using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Common.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Common;

public static class AuthExtensions {
    private const string KeycloakAuthenticationSectionName = "KeycloakAuthentication";
    private const string KeycloakServiceHttpKey = "services__keycloak__http__0";
    private const string KeycloakServiceLegacyHttpKey = "KEYCLOAK_HTTP";

    public static IServiceCollection AddKeycloakAuthentication(
        this IServiceCollection services,
        IConfiguration configuration
    ) {
        services.Configure<KeycloakAuthenticationOptions>(configuration.GetSection(KeycloakAuthenticationSectionName));

        KeycloakAuthenticationOptions authOptions = configuration
            .GetSection(KeycloakAuthenticationSectionName)
            .Get<KeycloakAuthenticationOptions>()
            ?? new KeycloakAuthenticationOptions();

        string? serviceBaseUrl = configuration[KeycloakServiceHttpKey] ?? configuration[KeycloakServiceLegacyHttpKey];
        serviceBaseUrl = string.IsNullOrWhiteSpace(serviceBaseUrl) ? null : serviceBaseUrl.TrimEnd('/');

        string? issuer = !string.IsNullOrWhiteSpace(authOptions.Issuer)
            ? authOptions.Issuer
            : serviceBaseUrl is null
                ? null
                : $"{serviceBaseUrl}/realms/makerverse";

        string? metadataAddress = !string.IsNullOrWhiteSpace(authOptions.MetadataAddress)
            ? authOptions.MetadataAddress
            : serviceBaseUrl is null
                ? null
                : $"{serviceBaseUrl}/realms/makerverse/.well-known/openid-configuration";

        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(metadataAddress)) {
            services.AddLogging();
            services.AddSingleton<IStartupFilter>(_ => new MissingKeycloakAuthenticationConfigurationStartupFilter());
            return services;
        }

        services.AddAuthentication()
            .AddKeycloakJwtBearer(
                serviceName: "keycloak",
                realm: "makerverse",
                options => {
                    options.RequireHttpsMetadata = false;
                    options.MetadataAddress = metadataAddress;
                    options.Audience = "makerverse";
                    options.TokenValidationParameters = new TokenValidationParameters {
                        ValidIssuer = issuer
                    };
                }
            );

        return services;
    }

    private sealed class MissingKeycloakAuthenticationConfigurationStartupFilter : IStartupFilter {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) {
            return app => {
                var logger = app.ApplicationServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(AuthExtensions));
                logger.LogWarning(
                    "Keycloak authentication is not configured. Provide KeycloakAuthentication:Issuer/MetadataAddress or a keycloak service reference."
                );
                next(app);
            };
        }
    }
}
