using AccountService.Options;
using AccountService.Services;
using Common;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.AddServiceDefaults();

builder.AddMinioClient("minio");

builder.Services.AddKeycloakAuthentication();
builder.Services.AddAuthorization();

builder.Services.Configure<KeycloakOptions>(builder.Configuration.GetSection("KeycloakOptions"));
builder.Services.Configure<KeycloakAdminOptions>(builder.Configuration.GetSection("KeycloakAdminOptions"));
builder.Services.Configure<AvatarOptions>(builder.Configuration.GetSection("AvatarOptions"));

builder.Services.AddHttpClient<KeycloakOidcService>(client => {
    client.BaseAddress = new Uri("http://keycloak");
});
builder.Services.AddHttpClient<KeycloakAdminService>(client => {
    client.BaseAddress = new Uri("http://keycloak");
});

builder.Services.AddScoped<AccountProfileService>();
builder.Services.AddScoped<AvatarStorageService>();

var app = builder.Build();

if (app.Environment.IsDevelopment()) {
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapDefaultEndpoints();

app.Run();
