using Common;
using JasperFx.CodeGeneration.Model;
using SearchService.Data;
using Typesense;
using Typesense.Setup;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.AddServiceDefaults();
builder.Services.AddTauriCors();

await builder.UseWolverineWithRabbitMqAsync(options => {
    options.ListenToRabbitQueue(
        "lives.search",
        cfg => cfg.BindExchange("lives")
    );
    options.ListenToRabbitQueue(
        "activities.search",
        cfg => cfg.BindExchange("activities")
    );
    options.ApplicationAssembly = typeof(Program).Assembly;

    // AddTypesenseClient 以 opaque lambda factory 注册 ITypesenseClient，
    // Wolverine 6 默认 ServiceLocationPolicy.NotAllowed 会导致 handler 代码生成失败、
    // 消息被静默丢弃（不重试、不进死信）。Typesense 客户端无法改为直接注入，放行该场景。
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
});

builder.Services.AddTypesenseClient(config => {
    string? typesenseUri = builder.Configuration["TYPESENSE_TYPESENSE"];
    if (string.IsNullOrEmpty(typesenseUri))
        throw new InvalidOperationException("Typesense service endpoint is not configured.");

    string? typesenseApiKey = builder.Configuration["TYPESENSE_API_KEY"];
    if (string.IsNullOrEmpty(typesenseApiKey))
        throw new InvalidOperationException("Typesense API key is not configured.");

    Uri uri = new(typesenseUri);

    config.ApiKey = typesenseApiKey;
    config.Nodes = [
        new Node(uri.Host, uri.Port.ToString(), uri.Scheme)
    ];
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()) {
    app.MapOpenApi();
}

app.UseCors(CorsExtensions.TauriCorsPolicyName);

app.MapControllers();
app.MapDefaultEndpoints();

using (IServiceScope scope = app.Services.CreateScope()) {
    var client = scope.ServiceProvider.GetRequiredService<ITypesenseClient>();
    await SearchInitializer.EnsureIndexesExistsAsync(client);
}

app.Run();
