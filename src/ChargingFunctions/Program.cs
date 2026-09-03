using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using Azure.Data.Tables;

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

builder.Build().Run();
