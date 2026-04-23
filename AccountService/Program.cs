using AccountService.Options;
using AccountService.Services;
using Common;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.AddServiceDefaults();

builder.AddMinioClient("minio");

builder.Services.AddTauriCors();
builder.Services.AddKeycloakAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

builder.Services.Configure<KeycloakOptions>(builder.Configuration.GetSection("KeycloakOptions"));
builder.Services.Configure<KeycloakAdminOptions>(builder.Configuration.GetSection("KeycloakAdminOptions"));
builder.Services.Configure<AvatarOptions>(builder.Configuration.GetSection("AvatarOptions"));

builder.Services.AddHttpClient<KeycloakOidcService>((serviceProvider, client) => {
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<KeycloakOptions>>().Value;
    client.BaseAddress = new Uri(options.InternalBaseUrl);
});
builder.Services.AddHttpClient<KeycloakAdminService>((serviceProvider, client) => {
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<KeycloakOptions>>().Value;
    client.BaseAddress = new Uri(options.InternalBaseUrl);
});

builder.Services.AddScoped<AccountProfileService>();
builder.Services.AddScoped<AvatarStorageService>();

var app = builder.Build();

if (app.Environment.IsDevelopment()) {
    app.MapOpenApi();
}

app.UseCors(CorsExtensions.TauriCorsPolicyName);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapDefaultEndpoints();

app.Run();
