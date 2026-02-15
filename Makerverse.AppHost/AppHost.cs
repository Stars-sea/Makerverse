using Makerverse.AppHost;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var compose = builder.AddDockerComposeEnvironment("production")
    .WithDashboard(dashboard => dashboard.WithHostPort(8080));

var postgres = builder.AddPostgres("postgres", port: 5432)
    .WithDataVolume("postgres-data")
    .WithBindMount("./data/postgres", "/docker-entrypoint-initdb.d");

var keycloakDb = postgres.AddDatabase("keycloak-db");
var keycloak = builder.AddCustomKeycloak("keycloak")
    .WithRealmImport("./data/keycloak-realms")
    .WithPostgres(keycloakDb)
    .WaitFor(keycloakDb)
    .WithEnvironment("VIRTUAL_HOST", "id.makerverse.local")
    .WithEnvironment("VIRTUAL_PORT", "8080");

var typesenseApiKey = builder.AddParameter("typesense-api-key", secret: true);

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

#pragma warning disable ASPIRECERTIFICATES001
var redis = builder.AddRedis("redis", port: 6379)
    .WithoutHttpsCertificate()
    .WithDataVolume("redis-data")
    .WithRedisInsight();
#pragma warning restore ASPIRECERTIFICATES001

var minio = builder.AddMinioContainer("minio", port: 9000)
    .WithDataVolume("minio-data");

var livestreamService = builder.AddLivestreamService("livestream-svc", srtPorts: 40000..40100)
    .WithEnvironment("SRT_HOST", "live.makerverse.local")
    .WithReference(redis)
    .WithReference(minio)
    .WaitFor(redis)
    .WaitFor(minio);

var liveDb = postgres.AddDatabase("live-db");
var liveService = builder.AddProject<Projects.LiveService>("live-svc")
    .WithReference(keycloak)
    .WithReference(liveDb)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WithReference(livestreamService.GetEndpoint("grpc"))
    .WaitFor(keycloak)
    .WaitFor(liveDb)
    .WaitFor(rabbitmq)
    .WaitFor(redis)
    .WaitFor(livestreamService);

var activityDb = postgres.AddDatabase("activity-db");
var activityService = builder.AddProject<Projects.ActivityService>("activity-svc")
    .WithReference(keycloak)
    .WithReference(activityDb)
    .WithReference(rabbitmq)
    .WithReference(redis)
    .WaitFor(keycloak)
    .WaitFor(activityDb)
    .WaitFor(rabbitmq)
    .WaitFor(redis);

var searchService = builder.AddProject<Projects.SearchService>("search-svc")
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
    .WithEnvironment("VIRTUAL_HOST", "api.makerverse.local")
    .WithEnvironment("VIRTUAL_PORT", "5000");

if (!builder.Environment.IsDevelopment()) {
    builder.AddContainer("nginx-proxy", "nginxproxy/nginx-proxy", "1.9")
        .WithEndpoint(80, 80, "nginx", isExternal: true)
        .WithBindMount("/var/run/docker.sock", "/tmp/docker.sock", isReadOnly: true);
}

builder.Build().Run();
