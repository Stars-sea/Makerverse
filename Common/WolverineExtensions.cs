using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Wolverine;
using Wolverine.RabbitMQ;

namespace Common;

public static class WolverineExtensions {

    public static async Task UseWolverineWithRabbitMqAsync(
        this IHostApplicationBuilder builder,
        Action<WolverineOptions> configure
    ) {
        AsyncRetryPolicy retryPolicy = Policy
            .Handle<BrokerUnreachableException>()
            .Or<SocketException>()
            .WaitAndRetryAsync(
                retryCount: 5,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                (exception, timespan, retryCount, _) => {
                    Console.WriteLine(
                        $"[RabbitMQ Retry] Attempt {retryCount} failed. Retrying in {timespan.TotalSeconds:F0}s: {exception.Message}");
                }
            );

        await retryPolicy.ExecuteAsync(async () => {
            string endpoint = builder.Configuration.GetConnectionString("messaging") ??
                              throw new InvalidOperationException("cannot get messaging connection string");

            ConnectionFactory factory = new() {
                Uri = new Uri(endpoint)
            };
            await using IConnection connection = await factory.CreateConnectionAsync();
        });
        
        builder.Services.AddOpenTelemetry().WithTracing(providerBuilder =>
            providerBuilder.SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddService(builder.Environment.ApplicationName))
                .AddSource("Wolverine")
        );
        
        builder.UseWolverine(options => {
            options.UseRabbitMqUsingNamedConnection("messaging")
                .AutoProvision()
                .DeclareExchange("activities")
                .DeclareExchange("lives");
            configure(options);
        });
    }
}
