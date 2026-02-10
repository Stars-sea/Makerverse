using System.Text.Json;
using System.Text.Json.Serialization;
using AuthService.Data;
using AuthService.Models;
using AuthService.Services;
using AuthService.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options => {
        options.JsonSerializerOptions.PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });
builder.Services.AddOpenApi();
builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<AccountDbContext>("account-db",
    null,
    options => {
        options.UseOpenIddict();
    }
);

builder.Services.AddAuthentication(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options => {
        options.Password.RequireDigit           = false;
        options.Password.RequireLowercase       = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase       = false;
        options.Password.RequiredLength         = 6;
        options.Password.RequiredUniqueChars    = 1;

        options.ClaimsIdentity.UserNameClaimType      = Claims.Name;
        options.ClaimsIdentity.UserIdClaimType        = Claims.Subject;
        options.ClaimsIdentity.RoleClaimType          = Claims.Role;
        options.ClaimsIdentity.EmailClaimType         = Claims.Email;
        options.ClaimsIdentity.SecurityStampClaimType = "security_stamp";
    })
    .AddEntityFrameworkStores<AccountDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<IAuthGrantService, AuthGrantService>();
builder.Services.Configure<ClientSettings>(builder.Configuration.GetSection("Identity"));

builder.Services.AddOpenIddict()
    .AddCore(options => {
        options.UseEntityFrameworkCore()
            .UseDbContext<AccountDbContext>();
    })
    .AddServer(options => {
        // Enable the endpoints.
        options.SetAuthorizationEndpointUris("auth/authorize")
            .SetEndSessionEndpointUris("auth/logout")
            .SetTokenEndpointUris("auth/token")
            .SetUserInfoEndpointUris("auth/userinfo");

        // Enable the flows.
        options.AllowAuthorizationCodeFlow()
            .AllowClientCredentialsFlow()
            .AllowRefreshTokenFlow()
            .AllowPasswordFlow();

        options.RegisterScopes(Scopes.Email, Scopes.Profile, Scopes.Roles, Scopes.OfflineAccess);

        // Register the signing and encryption credentials.
        options.AddDevelopmentEncryptionCertificate()
            .AddDevelopmentSigningCertificate();

        // Disable encryption for tokens so that resource servers can validate them locally without shared encryption keys.
        options.DisableAccessTokenEncryption();

        // Register the ASP.NET Core host and configure the ASP.NET Core options.
        options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableEndSessionEndpointPassthrough()
            .EnableTokenEndpointPassthrough()
            .EnableUserInfoEndpointPassthrough()
            .EnableStatusCodePagesIntegration()
            .DisableTransportSecurityRequirement();
    })
    .AddValidation(options => {;
        options.UseLocalServer();
        // Register the System.Net.Http integration.
        options.UseSystemNetHttp();
        // Registers the ASP.NET Core host
        options.UseAspNetCore();
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()) {
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapDefaultEndpoints();

using (IServiceScope scope = app.Services.CreateScope()) {
    // Apply any pending migrations and create the database if it doesn't exist.
    try {
        AccountDbContext context = scope.ServiceProvider.GetRequiredService<AccountDbContext>();
        await context.Database.MigrateAsync();
    }
    catch (Exception ex) {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating or initializing the database.");
    }

    // Add roles to the database if they don't exist.
    var      roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    string[] roles       = ["admin", "member"];
    foreach (string role in roles) {
        if (await roleManager.RoleExistsAsync(role)) continue;
        await roleManager.CreateAsync(new IdentityRole(role));
    }

    // Seed the database with the "service-worker" client application if it doesn't exist.
    IOpenIddictApplicationManager manager  = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
    ClientSettings                settings = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ClientSettings>>().Value;

    // TODO: Delete this block after the client application is created for the first time, to prevent accidental deletion of the client application in production environments.
    if (await manager.FindByClientIdAsync(settings.ClientId) is {} existingApp)
        await manager.DeleteAsync(existingApp);

    if (await manager.FindByClientIdAsync(settings.ClientId) is null) {
        await manager.CreateAsync(new OpenIddictApplicationDescriptor {
            ClientId     = settings.ClientId,
            ClientSecret = settings.ClientSecret,
            Permissions = {
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.ClientCredentials,
                Permissions.GrantTypes.RefreshToken,
                Permissions.GrantTypes.Password,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Roles
            }
        });
    }
}

app.Run();
