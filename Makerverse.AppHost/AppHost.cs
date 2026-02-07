var builder = DistributedApplication.CreateBuilder(args);

#pragma warning disable ASPIRECERTIFICATES001
var keycloak = builder.AddKeycloak("keycloak", 6001)
    .WithDataVolume("keycloak-data")
    // To solve health check issues
    .WithAnnotation(new HttpsCertificateAnnotation {
        UseDeveloperCertificate = false
    });
#pragma warning restore ASPIRECERTIFICATES001

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
    .WithReference(keycloak)
    .WithReference(liveDb)
    .WithReference(rabbitmq)
    .WaitFor(keycloak)
    .WaitFor(liveDb)
    .WaitFor(rabbitmq);

var searchService = builder.AddProject<Projects.SearchService>("search-svc")
    .WithEnvironment("TYPESENSE_API_KEY", typesenseApiKey)
    .WithReference(typesenseContainer)
    .WithReference(rabbitmq)
    .WaitFor(typesense)
    .WaitFor(rabbitmq);

builder.Build().Run();
