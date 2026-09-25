using Microsoft.Extensions.Configuration;

namespace CXS.iVendSqlMcp;

public sealed class SqlSettings
{
    public required string ConnectionString { get; init; }
    public bool AllowWrite { get; init; }
    public int MaxRows { get; init; } = 500;
    public int CommandTimeout { get; init; } = 60;

    /// <summary>
    /// Reads settings from configuration - environment variables (MCP client config),
    /// or .NET User Secrets when debugging from Visual Studio. Never from source files.
    /// </summary>
    public static SqlSettings FromConfiguration(IConfiguration config)
    {
        var cs = config["MSSQL_CONNECTION_STRING"];
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                "MSSQL_CONNECTION_STRING is not set. Provide it as an environment variable (MCP client config) " +
                "or, for Visual Studio debugging, run: dotnet user-secrets set MSSQL_CONNECTION_STRING \"<conn string>\"");

        return new SqlSettings
        {
            ConnectionString = cs,
            AllowWrite = string.Equals(config["MSSQL_ALLOW_WRITE"], "true", StringComparison.OrdinalIgnoreCase),
            MaxRows = int.TryParse(config["MSSQL_MAX_ROWS"], out var r) && r > 0 ? r : 500,
            CommandTimeout = int.TryParse(config["MSSQL_COMMAND_TIMEOUT"], out var t) && t > 0 ? t : 60,
        };
    }
}
