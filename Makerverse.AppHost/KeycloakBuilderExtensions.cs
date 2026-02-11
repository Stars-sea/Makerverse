namespace Makerverse.AppHost;

public static class KeycloakBuilderExtensions {

    public static IResourceBuilder<KeycloakResource> AddCustomKeycloak(
        this IDistributedApplicationBuilder builder,
        string name,
        string imageTag = "26.5"
    ) {
        KeycloakResource resource = new(
            name,
            null,
            ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, "keycloak-password")
        );

        return builder.AddResource(resource)
            .WithImage("keycloak/keycloak")
            .WithImageTag(imageTag)
            .WithImageRegistry("quay.io")
            .WithHttpEndpoint(targetPort: 8080)
            .WithHttpEndpoint(targetPort: 9000, name: "management")
            .WithHttpHealthCheck(path: "/health", statusCode: 200, endpointName: "management")
            .WithOtlpExporter()
            .WithArgs(builder.ExecutionContext.IsRunMode ? "start-dev" : "start", "--import-realm")
            .WithEnvironment(ctx => {
                ctx.EnvironmentVariables["KC_BOOTSTRAP_ADMIN_USERNAME"]       = "admin";
                ctx.EnvironmentVariables["KC_BOOTSTRAP_ADMIN_PASSWORD"]       = resource.AdminPasswordParameter;
                ctx.EnvironmentVariables["KC_HEALTH_ENABLED"]                 = "true";
                ctx.EnvironmentVariables["KC_HTTP_ENABLED"]                   = "true";
                ctx.EnvironmentVariables["KC_HOSTNAME_STRICT"]                = "false";
                ctx.EnvironmentVariables["KC_PROXY_HEADERS"]                  = "xforwarded";
                ctx.EnvironmentVariables["KC_HTTP_MANAGEMENT_HEALTH_ENABLED"] = "true";
                ctx.EnvironmentVariables["KC_HTTP_MANAGEMENT_SCHEME"]         = "http";
            });
    }
}
