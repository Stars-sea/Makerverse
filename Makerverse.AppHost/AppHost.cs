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
    .WithDataVolume("postgres-data")
    .WithPgAdmin();

var liveDb = postgres.AddDatabase("live-db");

var liveService = builder.AddProject<Projects.LiveService>("live-svc")
    .WithReference(keycloak)
    .WithReference(liveDb)
    .WaitFor(keycloak)
    .WaitFor(liveDb);

builder.Build().Run();
