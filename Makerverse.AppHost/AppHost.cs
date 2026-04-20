using Makerverse.AppHost;
using Microsoft.Extensions.Hosting;

#pragma warning disable ASPIRECOMPUTE003
#pragma warning disable ASPIREPIPELINES003
#pragma warning disable ASPIRECERTIFICATES001

var builder = DistributedApplication.CreateBuilder(args);

var typesenseApiKey            = ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, "typesense-api-key");
var livestreamBucket           = builder.AddParameter("livestream-bucket", value: "videos", publishValueAsDefault: true);
var livestreamAppname          = builder.AddParameter("livestream-appname", value: "lives", publishValueAsDefault: true);
var accountAvatarBucket        = builder.AddParameter("account-avatar-bucket", value: "avatars", publishValueAsDefault: true);
var accountServiceClientSecret = ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, "account-service-client-secret");

var hostId   = builder.AddParameter("host-id", value: "id.makerverse.local");
var hostApi  = builder.AddParameter("host-api", value: "api.makerverse.local");
var hostLive = builder.AddParameter("host-live", value: "live.makerverse.local");

var registryEndpoint = builder.AddParameter("registry-endpoint", secret: true);

var registry = builder.AddContainerRegistry(
    "container-registry",
    registryEndpoint
);

var compose = builder.AddDockerComposeEnvironment("production")
    .WithDashboard(dashboard => dashboard.WithHostPort(8080));

var postgres = builder.AddPostgres("postgres", port: 5432)
    .WithDataVolume("postgres-data")
    .WithBindMount("./data/postgres", "/docker-entrypoint-initdb.d");

var keycloakDb = postgres.AddDatabase("keycloak-db");
var keycloak = builder.AddKeycloak("keycloak", port: 6001)
    .WithEnvironment("KC_HTTP_ENABLED", "true")
    .WithEnvironment("ACCOUNT_SERVICE_CLIENT_SECRET", accountServiceClientSecret)
    .WithRealmImport("./data/keycloak-realms")
    .WithPostgres(keycloakDb)
    .WaitFor(keycloakDb)
    .WithEnvironment("VIRTUAL_HOST", hostId)
    .WithEnvironment("VIRTUAL_PORT", "8080");

if (!builder.Environment.IsDevelopment()) {
    keycloak.WithEnvironment("KC_HOSTNAME", ReferenceExpression.Create($"https://{hostId}"))
        .WithEnvironment("KC_HOSTNAME_STRICT", "true")
        .WithEnvironment("KC_PROXY_HEADERS", "xforwarded");
}

var typesense = builder.AddContainer("typesense", "typesense/typesense", "30.1")
    .WithEnvironment("TYPESENSE_API_KEY", typesenseApiKey)
    .WithEnvironment("TYPESENSE_DATA_DIR", "/data")
    .WithEnvironment("TYPESENSE_ENABLE_CORS", "true")
    .WithVolume("typesense-data", "/data")
    .WithHttpEndpoint(port: 8108, targetPort: 8108, name: "typesense")
    .WithHttpHealthCheck("/health", 200, "typesense");

var rabbitmq = builder.AddRabbitMQ("messaging", port: 5672)
    .WithDataVolume("rabbitmq-data")
    .WithManagementPlugin(port: 15672);

var redis = builder.AddRedis("redis")
    .WithoutHttpsCertificate()
    .WithDataVolume("redis-data");

var minio = builder.AddMinioContainer("minio")
    .WithDataVolume("minio-data");

var livestreamService = builder.AddLivestreamService(
        "livestream-svc",
        rtmpAppname: livestreamAppname,
        bucketName: livestreamBucket,
        grpcPort: 50050,
        srtPorts: 40000..40100
    )
    .WithContainerRegistry(registry)
    .WithRemoteImageTag("latest")
    .WithEnvironment("RUST_LOG", "info")
    .WithReference(minio)
    .WaitFor(minio);

var accountService = builder.AddProject<Projects.AccountService>("account-svc")
    .WithContainerRegistry(registry)
    .WithRemoteImageTag("latest")
    .WithReference(keycloak)
    .WithReference(minio)
    .WithEnvironment("AvatarOptions__BucketName", accountAvatarBucket)
    .WithEnvironment("KeycloakOptions__Realm", "makerverse")
    .WithEnvironment("KeycloakOptions__PublicClientId", "makerverse")
    .WithEnvironment("KeycloakAdminOptions__ClientId", "makerverse-account-service")
    .WithEnvironment("KeycloakAdminOptions__ClientSecret", accountServiceClientSecret)
    .WithEnvironment("VIRTUAL_HOST", hostId)
    .WithEnvironment("VIRTUAL_PORT", "8080")
    .WithEnvironment("VIRTUAL_PATH", "/account/")
    .WaitFor(keycloak)
    .WaitFor(minio);

var liveDb = postgres.AddDatabase("live-db");
var liveService = builder.AddProject<Projects.LiveService>("live-svc")
    .WithContainerRegistry(registry)
    .WithRemoteImageTag("latest")
    .WithEnvironment("LivestreamOptions__Hostname", hostLive)
    .WithEnvironment("LivestreamOptions__BucketName", livestreamBucket)
    .WithReference(keycloak)
    .WithReference(liveDb)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WithReference(minio)
    .WithReference(livestreamService.GetEndpoint("grpc"))
    .WaitFor(keycloak)
    .WaitFor(liveDb)
    .WaitFor(rabbitmq)
    .WaitFor(redis)
    .WaitFor(minio)
    .WaitFor(livestreamService, WaitBehavior.StopOnResourceUnavailable);

var activityDb = postgres.AddDatabase("activity-db");
var activityService = builder.AddProject<Projects.ActivityService>("activity-svc")
    .WithContainerRegistry(registry)
    .WithRemoteImageTag("latest")
    .WithReference(keycloak)
    .WithReference(activityDb)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(keycloak)
    .WaitFor(activityDb)
    .WaitFor(rabbitmq)
    .WaitFor(redis);

var searchService = builder.AddProject<Projects.SearchService>("search-svc")
    .WithContainerRegistry(registry)
    .WithRemoteImageTag("latest")
    .WithEnvironment("TYPESENSE_API_KEY", typesenseApiKey)
    .WithReference(typesense.GetEndpoint("typesense"))
    .WithReference(rabbitmq)
    .WaitFor(typesense)
    .WaitFor(rabbitmq);

var yarp = builder.AddYarp("gateway")
    .WithConfiguration(yarpBuilder => {
        yarpBuilder.AddRoute("/activities/{**catch-all}", activityService);
        yarpBuilder.AddRoute("/tags/{**catch-all}", activityService);
        yarpBuilder.AddRoute("/lives/{**catch-all}", liveService);
        yarpBuilder.AddRoute("/search/{**catch-all}", searchService);
    })
    .WithHostPort(8001)
    .WithEnvironment("VIRTUAL_HOST", hostApi)
    .WithEnvironment("VIRTUAL_PORT", "5000");

if (!builder.Environment.IsDevelopment()) {
    builder.AddContainer("nginx-proxy", "nginxproxy/nginx-proxy", "1.9")
        .WithEndpoint(80, 80, "nginx", isExternal: true)
        .WithBindMount("/var/run/docker.sock", "/tmp/docker.sock", isReadOnly: true);
}

builder.Build().Run();
