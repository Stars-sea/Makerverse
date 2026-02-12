using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Common;

public static class AuthExtensions {

    public static IServiceCollection AddKeycloakAuthentication(
        this IServiceCollection services
    ) {
        services.AddAuthentication()
            .AddKeycloakJwtBearer(
                serviceName: "keycloak",
                realm: "makerverse",
                options => {
                    options.RequireHttpsMetadata = false;
                    options.Audience             = "makerverse";
                    options.TokenValidationParameters = new TokenValidationParameters {
                        ValidIssuers = [
                            "http://localhost:6001/realms/makerverse",
                            "http://keycloak/realms/makerverse",
                            "http://id.makerverse.local/realms/makerverse"
                        ]
                    };
                }
            );

        return services;
    }
}
