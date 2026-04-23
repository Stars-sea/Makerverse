using Common;
using LiveService.Data;
using LiveService.Options;
using LiveService.Protos;
using LiveService.Services;
using Microsoft.EntityFrameworkCore;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.AddServiceDefaults();

builder.AddRedisClient("redis");
builder.AddMinioClient("minio");

builder.Services.AddTauriCors();
builder.Services.AddKeycloakAuthentication();

builder.AddNpgsqlDbContext<LiveDbContext>("live-db");

await builder.UseWolverineWithRabbitMqAsync(options => {
    options.PublishAllMessages().ToRabbitExchange("lives");
    options.ApplicationAssembly = typeof(Program).Assembly;
});

builder.Services.AddGrpc();
builder.Services.AddGrpcClient<Livestream.LivestreamClient>(options => {
    string? grpcUri = builder.Configuration["LIVESTREAM_SVC_GRPC"];
    if (string.IsNullOrEmpty(grpcUri))
        throw new InvalidOperationException("Livestream service gRPC endpoint is not configured.");

    options.Address = new Uri(grpcUri);
});
builder.Services.AddScoped<LivestreamService>();
builder.Services.AddScoped<LivestreamPersistentService>();
builder.Services.AddHostedService<LivestreamLifecycleWatcher>();
builder.Services.AddSingleton<LivestreamLifecycleWatcherQueue>();

builder.Services.AddSingleton<StreamDescriptorConverter>();
builder.Services.Configure<LivestreamOptions>(builder.Configuration.GetSection("LivestreamOptions"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()) {
    app.MapOpenApi();
}

app.UseCors(CorsExtensions.TauriCorsPolicyName);

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
