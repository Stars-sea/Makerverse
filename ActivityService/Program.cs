using ActivityService.Data;
using ActivityService.Services;
using Common;
using Microsoft.EntityFrameworkCore;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.AddServiceDefaults();

builder.Services.AddMemoryCache();
builder.Services.AddScoped<TagService>();
builder.Services.AddKeycloakAuthentication();

builder.AddNpgsqlDbContext<ActivityDbContext>("activity-db");

await builder.UseWolverineWithRabbitMqAsync(options => {
    options.PublishAllMessages().ToRabbitExchange("activities");
    options.ApplicationAssembly = typeof(Program).Assembly;
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
        ActivityDbContext context = scope.ServiceProvider.GetRequiredService<ActivityDbContext>();
        await context.Database.MigrateAsync();
    }
    catch (Exception ex) {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating or initializing the database.");
    }
}

app.Run();
