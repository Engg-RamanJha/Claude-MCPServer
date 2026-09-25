# CXS.iVendSqlMcp

Stdio MCP server (.NET 10) that exposes an iVend Retail SQL Server database to Claude.

## Tools

| Tool | Purpose |
|---|---|
| `server_info` | Server, database, version, login, write mode |
| `list_tables` | Tables/views with approx row counts (optional LIKE filter, e.g. `U_KSA%`) |
| `describe_table` | Columns, indexes/PK, foreign keys |
| `list_procedures` | Procs, functions, views, triggers (optional LIKE filter) |
| `get_object_definition` | T-SQL source of a proc/function/view/trigger |
| `execute_query` | Run any T-SQL batch; returns all result sets as JSON |

**Read-only by default:** `execute_query` runs inside a transaction that is always rolled back,
so DML/DDL never persist. Set `MSSQL_ALLOW_WRITE=true` to commit.

## Configuration (environment variables — never commit credentials)

| Variable | Required | Default |
|---|---|---|
| `MSSQL_CONNECTION_STRING` | yes | — |
| `MSSQL_ALLOW_WRITE` | no | `false` |
| `MSSQL_MAX_ROWS` | no | `500` |
| `MSSQL_COMMAND_TIMEOUT` | no | `60` (seconds) |

## Build & register

```bash
dotnet build -c Release
claude mcp add ivend-sql --scope user -e "MSSQL_CONNECTION_STRING=<conn string>" -- "D:\CXS-Claude\mcp-servers\CXS.iVendSqlMcp\bin\Release\net10.0\CXS.iVendSqlMcp.exe"
```

To point at another database, register a second server with a different name and connection string.
