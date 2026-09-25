using System.Security.Cryptography;
using System.Text;
using CXS.iVendSqlMcp;

// Configuration comes from environment variables, or from .NET User Secrets when
// debugging in Visual Studio (never hard-code credentials here):
//   MSSQL_CONNECTION_STRING  - required
//   MCP_API_KEY              - required, clients send "Authorization: Bearer <key>"
//   MSSQL_ALLOW_WRITE        - optional, "true" lets execute_query commit changes (default: read-only)
//   MSSQL_MAX_ROWS           - optional, default 500
//   MSSQL_COMMAND_TIMEOUT    - optional, seconds, default 60
//   ASPNETCORE_URLS          - optional, listen address (default: http://localhost:5100 - local machine only)
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Configuration.AddEnvironmentVariables();   // env vars win over user secrets

var settings = SqlSettings.FromConfiguration(builder.Configuration);

var apiKey = builder.Configuration["MCP_API_KEY"];
if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 16)
    throw new InvalidOperationException(
        "MCP_API_KEY is not set (min 16 chars). Provide it as an environment variable or, " +
        "for Visual Studio debugging, run: dotnet user-secrets set MCP_API_KEY \"<key>\"");
var apiKeyBytes = Encoding.UTF8.GetBytes(apiKey);

if (string.IsNullOrEmpty(builder.Configuration["ASPNETCORE_URLS"]) && string.IsNullOrEmpty(builder.Configuration["urls"]))
    builder.WebHost.UseUrls("http://localhost:5100");

builder.Services.AddSingleton(settings);
builder.Services
    .AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<SqlTools>();

var app = builder.Build();

// Unauthenticated liveness probe
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Bearer API-key check for everything else
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/health")) { await next(); return; }

    var header = ctx.Request.Headers.Authorization.ToString();
    var supplied = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : "";
    if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), apiKeyBytes))
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next();
});

app.MapMcp("/mcp");

app.Run();
