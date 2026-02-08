var builder = DistributedApplication.CreateBuilder(args);

var compose = builder.AddDockerComposeEnvironment("production")
    .WithDashboard(dashboard => dashboard.WithHostPort(8080));

var postgres = builder.AddPostgres("postgres", port: 5432)
    .WithDataVolume("postgres-data");

var typesenseApiKey = builder.AddParameter("typesense-api-key", secret: true);

var typesense = builder.AddContainer("typesense", "typesense/typesense", "30.1")
    .WithEnvironment("TYPESENSE_API_KEY", typesenseApiKey)
    .WithEnvironment("TYPESENSE_DATA_DIR", "/data")
    .WithEnvironment("TYPESENSE_ENABLE_CORS", "true")
    .WithVolume("typesense-data", "/data")
    .WithHttpEndpoint(port: 8108, targetPort: 8108, name: "typesense")
    .WithHttpHealthCheck("/health", 200, "typesense");

var typesenseContainer = typesense.GetEndpoint("typesense");

var rabbitmq = builder.AddRabbitMQ("messaging", port: 5672)
    .WithDataVolume("rabbitmq-data")
    .WithManagementPlugin(port: 15672);

var liveDb = postgres.AddDatabase("live-db");

var liveService = builder.AddProject<Projects.LiveService>("live-svc")
    .WithReference(liveDb)
    .WithReference(rabbitmq)
    .WaitFor(liveDb)
    .WaitFor(rabbitmq);

var activityDb = postgres.AddDatabase("activity-db");

var activityService = builder.AddProject<Projects.ActivityService>("activity-svc")
    .WithReference(activityDb)
    .WithReference(rabbitmq)
    .WaitFor(activityDb)
    .WaitFor(rabbitmq);

var searchService = builder.AddProject<Projects.SearchService>("search-svc")
    .WithEnvironment("TYPESENSE_API_KEY", typesenseApiKey)
    .WithReference(typesenseContainer)
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
    .WithHostPort(8001);

builder.Build().Run();
