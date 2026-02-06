using LiveService.Data;
using Microsoft.EntityFrameworkCore;

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()) {
    app.MapOpenApi();
}

app.MapControllers();
app.MapDefaultEndpoints();

using (IServiceScope scope = app.Services.CreateScope()) {
    try {
        var context = scope.ServiceProvider.GetRequiredService<LiveDbContext>();
        await context.Database.MigrateAsync();
    }
    catch (Exception ex) {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating or initializing the database.");
    }
}

app.Run();
