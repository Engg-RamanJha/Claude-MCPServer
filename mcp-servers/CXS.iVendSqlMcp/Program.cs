using CXS.iVendSqlMcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Settings come from environment variables set in the MCP client config, or from
// .NET User Secrets when debugging in Visual Studio (never hard-code credentials here):
//   MSSQL_CONNECTION_STRING  - required
//   MSSQL_ALLOW_WRITE        - optional, "true" lets execute_query commit changes (default: read-only)
//   MSSQL_MAX_ROWS           - optional, default row cap per result set (default: 500)
//   MSSQL_COMMAND_TIMEOUT    - optional, seconds (default: 60)
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Configuration.AddEnvironmentVariables();   // env vars win over user secrets

var settings = SqlSettings.FromConfiguration(builder.Configuration);

// stdout is reserved for the MCP protocol - send all logs to stderr
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(settings);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
