using ChisBallot.Data;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

FunctionsApplicationBuilder builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

string connectionString = builder.Configuration["DatabaseConnectionString"]
    ?? throw new InvalidOperationException("DatabaseConnectionString is not set.");

builder.Services.AddSingleton(Log.Logger);
builder.Services.AddSingleton(new ElectionReader(connectionString));
builder.Services.AddSingleton(new ElectionAdmin(connectionString, Log.Logger));
builder.Services.AddSingleton(new ResultsReader(connectionString));

builder.Build().Run();
