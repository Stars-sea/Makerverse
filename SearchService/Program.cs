using System.Text.RegularExpressions;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SearchService.Data;
using SearchService.Models;
using Typesense;
using Typesense.Setup;
using Wolverine;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.AddServiceDefaults();

builder.Services.AddOpenTelemetry().WithTracing(providerBuilder =>
    providerBuilder.SetResourceBuilder(ResourceBuilder.CreateDefault()
            .AddService(builder.Environment.ApplicationName))
        .AddSource("Wolverine")
);

builder.Host.UseWolverine(options => {
    options.UseRabbitMqUsingNamedConnection("messaging").AutoProvision();
    options.ListenToRabbitQueue(
        "lives.search",
        cfg => cfg.BindExchange("lives")
    );
});

builder.Services.AddTypesenseClient(config => {
    string? typesenseUri = builder.Configuration["services:typesense:typesense:0"];
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
app.MapDefaultEndpoints();

app.MapGet("/search",
    async (string query, ITypesenseClient client) => {
        string? tag      = null;
        var     tagMatch = Regex.Match(query, @"\[(.*?)\]");
        if (tagMatch.Success) {
            tag   = tagMatch.Groups[1].Value;
            query = query.Replace(tagMatch.Value, "").Trim();
        }

        SearchParameters searchParameters = new(query, "title");
        if (!string.IsNullOrEmpty(tag)) {
            searchParameters.FilterBy = $"tags:=[{tag}]";
        }

        try {
            var results = await client.Search<SearchLive>("lives", searchParameters);
            return Results.Ok(results.Hits.Select(hit => hit.Document));
        }
        catch (Exception e) {
            return Results.Problem("Typesense search failed: " + e.Message);
        }
    }
);

using (IServiceScope scope = app.Services.CreateScope()) {
    var client = scope.ServiceProvider.GetRequiredService<ITypesenseClient>();
    await SearchInitializer.EnsureIndexExistsAsync(client);
}

app.Run();
