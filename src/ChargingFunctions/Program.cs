using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using Azure.Data.Tables;
using Microsoft.Azure.Devices;
using ChargingFunctions.Auth;
using ChargingFunctions;

// Register the Azure clients that the functions depend on
var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// builder.Services.AddOpenTelemetry()
//     .UseFunctionsWorkerDefaults()
//     .UseAzureMonitorExporter();

builder.Services.AddSingleton(_ =>
{
    var conn = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
        ?? throw new InvalidOperationException("AzureWebJobsStorage is not set");
    return new Tables(conn);
});

builder.Services.AddSingleton(_ =>
{
    var conn = Environment.GetEnvironmentVariable("IoTHubServiceConnectionString")
        ?? throw new InvalidOperationException("IoTHubServiceConnectionString is not set");
    return ServiceClient.CreateFromConnectionString(conn);
});

builder.Services.AddSingleton(_ =>
{
    var conn = Environment.GetEnvironmentVariable("IoTHubServiceConnectionString")
        ?? throw new InvalidOperationException("IoTHubServiceConnectionString is not set");
    return RegistryManager.CreateFromConnectionString(conn);
});

builder.Services.AddSingleton(_ =>
{
    var secret = Environment.GetEnvironmentVariable("JwtSigningKey")
        ?? throw new InvalidOperationException("JwtSigningKey is not set");
    return new TokenService(secret);
});

builder.Services.AddSingleton<OwnershipService>();
builder.Services.AddSingleton<RequestAuth>();

builder.Build().Run();
