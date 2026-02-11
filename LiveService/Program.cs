using Common;
using LiveService.Data;
using Microsoft.EntityFrameworkCore;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.AddServiceDefaults();
builder.Services.AddKeycloakAuthentication();

builder.AddNpgsqlDbContext<LiveDbContext>("live-db");

await builder.UseWolverineWithRabbitMqAsync(options => {
    options.PublishAllMessages().ToRabbitExchange("lives");
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
        LiveDbContext context = scope.ServiceProvider.GetRequiredService<LiveDbContext>();
        await context.Database.MigrateAsync();
    }
    catch (Exception ex) {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating or initializing the database.");
    }
}

app.Run();
