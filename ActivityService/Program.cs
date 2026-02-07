using ActivityService.Data;
using ActivityService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.AddServiceDefaults();

builder.Services.AddMemoryCache();
builder.Services.AddScoped<TagService>();

builder.Services.AddAuthentication()
    .AddKeycloakJwtBearer(
        serviceName: "keycloak",
        realm: "makerverse",
        options => {
            options.RequireHttpsMetadata = false;
            options.Audience             = "makerverse";
        }
    );
builder.AddNpgsqlDbContext<ActivityDbContext>("activity-db");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()) {
    app.MapOpenApi();
}

app.MapControllers();

app.Run();
