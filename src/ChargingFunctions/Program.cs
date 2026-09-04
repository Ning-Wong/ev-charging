using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using Azure.Data.Tables;
using Microsoft.Azure.Devices;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// builder.Services.AddOpenTelemetry()
//     .UseFunctionsWorkerDefaults()
//     .UseAzureMonitorExporter();

builder.Services.AddSingleton(_ =>
{
    var conn = Environment.GetEnvironmentVariable("AzureWebJobsStorage")!;
    var tableName = Environment.GetEnvironmentVariable("TableName") ?? "CarState";
    var client = new TableClient(conn, tableName);
    client.CreateIfNotExists();
    return client;
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

builder.Build().Run();
