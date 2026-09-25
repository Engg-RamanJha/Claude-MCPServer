# CXS.iVendSqlMcp.Http

HTTP (Streamable HTTP) version of [CXS.iVendSqlMcp](../CXS.iVendSqlMcp/README.md).
Same six tools and read-only behavior — the tool source is shared from that project
(`SqlTools.cs`, `SqlSettings.cs` are linked, not copied).

Unlike the stdio server, this one is a long-running process you start yourself;
clients connect to `http://<host>:5100/mcp`.

## Configuration (environment variables)

| Variable | Required | Default |
|---|---|---|
| `MSSQL_CONNECTION_STRING` | yes | — |
| `MCP_API_KEY` | yes (min 16 chars) | — |
| `MSSQL_ALLOW_WRITE` | no | `false` |
| `MSSQL_MAX_ROWS` | no | `500` |
| `MSSQL_COMMAND_TIMEOUT` | no | `60` |
| `ASPNETCORE_URLS` | no | `http://localhost:5100` (this machine only) |

Every request except `GET /health` must send `Authorization: Bearer <MCP_API_KEY>`.

## Build & run

```powershell
dotnet build -c Release
$env:MSSQL_CONNECTION_STRING = "<conn string>"
$env:MCP_API_KEY = "<long random key>"
.\bin\Release\net10.0\CXS.iVendSqlMcp.Http.exe
```

## Register with Claude Code

```bash
claude mcp add --transport http ivend-sql-http http://localhost:5100/mcp --scope user --header "Authorization: Bearer <MCP_API_KEY>"
```

## Sharing with the team

To let other machines connect, set `ASPNETCORE_URLS=http://0.0.0.0:5100` and open the port in
Windows Firewall. The API key then travels in clear text, so put the server behind HTTPS
(IIS / reverse proxy, or an `https://` URL with a certificate) before exposing it beyond your machine,
and use a read-only SQL login rather than `sa`.
