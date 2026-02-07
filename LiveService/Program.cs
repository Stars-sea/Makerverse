using LiveService.Data;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Wolverine;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.AddServiceDefaults();
builder.Services.AddAuthentication()
    .AddKeycloakJwtBearer(
        serviceName: "keycloak",
        realm: "makerverse",
        options => {
            options.RequireHttpsMetadata = false;
            options.Audience             = "makerverse";
        }
    );
builder.AddNpgsqlDbContext<LiveDbContext>("live-db");

builder.Services.AddOpenTelemetry().WithTracing(providerBuilder =>
    providerBuilder.SetResourceBuilder(ResourceBuilder.CreateDefault()
        .AddService(builder.Environment.ApplicationName))
        .AddSource("Wolverine")
);

builder.Host.UseWolverine(options => {
    options.UseRabbitMqUsingNamedConnection("messaging").AutoProvision();
    options.PublishAllMessages().ToRabbitExchange("lives");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()) {
    app.MapOpenApi();
}

app.MapControllers();
app.MapDefaultEndpoints();

using (IServiceScope scope = app.Services.CreateScope()) {
    try {
        LiveDbContext context = scope.ServiceProvider.GetRequiredService<LiveDbContext>();
        await context.Database.MigrateAsync();
    }
    catch (Exception ex) {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating or initializing the database.");
    }
}

app.Run();
