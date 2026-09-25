using System.ComponentModel;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;

namespace CXS.iVendSqlMcp;

[McpServerToolType]
public sealed class SqlTools(SqlSettings settings)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [McpServerTool(Name = "server_info", ReadOnly = true)]
    [Description("Returns the connected server, database, SQL Server version and whether write mode is enabled.")]
    public Task<string> ServerInfo(CancellationToken ct) =>
        RunAsync("SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName, @@VERSION AS Version, SUSER_SNAME() AS LoginName, "
               + $"CAST({(settings.AllowWrite ? 1 : 0)} AS bit) AS WriteEnabled", null, 1, readOnly: true, ct);

    [McpServerTool(Name = "list_tables", ReadOnly = true)]
    [Description("Lists user tables and views with approximate row counts. Optional LIKE filter on the name, e.g. 'Trx%' or '%Product%'. iVend UDTs are named u_<Name>.")]
    public Task<string> ListTables(
        [Description("Optional SQL LIKE pattern for the table/view name")] string? nameFilter = null,
        CancellationToken ct = default) =>
        RunAsync("""
            SELECT s.name AS SchemaName, o.name AS ObjectName, o.type_desc AS ObjectType,
                   (SELECT SUM(p.rows) FROM sys.partitions p WHERE p.object_id = o.object_id AND p.index_id IN (0,1)) AS ApproxRows
            FROM sys.objects o JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.type IN ('U','V') AND (@f IS NULL OR o.name LIKE @f)
            ORDER BY o.name
            """, [new("@f", (object?)nameFilter ?? DBNull.Value)], 5000, readOnly: true, ct);

    [McpServerTool(Name = "describe_table", ReadOnly = true)]
    [Description("Returns columns (type, length, nullability, identity, default), primary key and foreign keys for a table or view. UDF columns on system tables are prefixed U_.")]
    public async Task<string> DescribeTable(
        [Description("Table or view name, optionally schema-qualified (e.g. 'TrxTransaction' or 'dbo.InvProduct')")] string tableName,
        CancellationToken ct)
    {
        var p = new SqlParameter("@t", tableName);
        var columns = await RunAsync("""
            SELECT c.column_id AS Ord, c.name AS ColumnName, TYPE_NAME(c.user_type_id) AS DataType,
                   CASE WHEN c.max_length = -1 THEN 'MAX'
                        WHEN TYPE_NAME(c.user_type_id) IN ('nvarchar','nchar') THEN CAST(c.max_length/2 AS varchar(10))
                        ELSE CAST(c.max_length AS varchar(10)) END AS MaxLength,
                   c.precision AS [Precision], c.scale AS Scale, c.is_nullable AS IsNullable, c.is_identity AS IsIdentity,
                   dc.definition AS DefaultValue
            FROM sys.columns c
            LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
            WHERE c.object_id = OBJECT_ID(@t)
            ORDER BY c.column_id
            """, [p], 2000, readOnly: true, ct);

        var keys = await RunAsync("""
            SELECT i.name AS IndexName, i.is_primary_key AS IsPrimaryKey, i.is_unique AS IsUnique,
                   STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS Columns
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(@t)
            GROUP BY i.name, i.is_primary_key, i.is_unique
            """, [new SqlParameter("@t", tableName)], 500, readOnly: true, ct);

        var fks = await RunAsync("""
            SELECT fk.name AS ForeignKey, pc.name AS ColumnName,
                   OBJECT_SCHEMA_NAME(fk.referenced_object_id) + '.' + OBJECT_NAME(fk.referenced_object_id) AS ReferencedTable,
                   rc.name AS ReferencedColumn
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE fk.parent_object_id = OBJECT_ID(@t)
            """, [new SqlParameter("@t", tableName)], 500, readOnly: true, ct);

        return $"{{\"columns\":{columns},\"indexes\":{keys},\"foreignKeys\":{fks}}}";
    }

    [McpServerTool(Name = "list_procedures", ReadOnly = true)]
    [Description("Lists stored procedures, functions and views (programmable objects). Optional LIKE filter, e.g. 'usp_MBM%'.")]
    public Task<string> ListProcedures(
        [Description("Optional SQL LIKE pattern for the object name")] string? nameFilter = null,
        CancellationToken ct = default) =>
        RunAsync("""
            SELECT s.name AS SchemaName, o.name AS ObjectName, o.type_desc AS ObjectType, o.modify_date AS Modified
            FROM sys.objects o JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.type IN ('P','FN','IF','TF','V','TR') AND o.is_ms_shipped = 0 AND (@f IS NULL OR o.name LIKE @f)
            ORDER BY o.type, o.name
            """, [new("@f", (object?)nameFilter ?? DBNull.Value)], 5000, readOnly: true, ct);

    [McpServerTool(Name = "get_object_definition", ReadOnly = true)]
    [Description("Returns the T-SQL source of a stored procedure, function, view or trigger.")]
    public async Task<string> GetObjectDefinition(
        [Description("Object name, optionally schema-qualified")] string objectName,
        CancellationToken ct)
    {
        await using var conn = new SqlConnection(settings.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT OBJECT_DEFINITION(OBJECT_ID(@o))", conn) { CommandTimeout = settings.CommandTimeout };
        cmd.Parameters.AddWithValue("@o", objectName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is string s ? s : $"Object '{objectName}' not found or has no accessible definition.";
    }

    [McpServerTool(Name = "execute_query")]
    [Description("Executes T-SQL and returns every result set as JSON (row-capped). In read-only mode (default) the batch runs inside a transaction that is always rolled back, so INSERT/UPDATE/DELETE/DDL never persist. Set MSSQL_ALLOW_WRITE=true on the server to commit changes.")]
    public Task<string> ExecuteQuery(
        [Description("The T-SQL batch to execute")] string sql,
        [Description("Maximum rows to return per result set (defaults to server MSSQL_MAX_ROWS)")] int? maxRows = null,
        CancellationToken ct = default) =>
        RunAsync(sql, null, maxRows is > 0 ? maxRows.Value : settings.MaxRows, readOnly: !settings.AllowWrite, ct);

    private async Task<string> RunAsync(string sql, SqlParameter[]? parameters, int maxRows, bool readOnly, CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(settings.ConnectionString);
            await conn.OpenAsync(ct);
            await using var tx = readOnly ? (SqlTransaction)await conn.BeginTransactionAsync(ct) : null;
            await using var cmd = new SqlCommand(sql, conn, tx) { CommandTimeout = settings.CommandTimeout };
            if (parameters != null) cmd.Parameters.AddRange(parameters);

            var resultSets = new List<ResultSet>();
            int recordsAffected;
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                do
                {
                    if (reader.FieldCount == 0) continue;
                    var cols = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
                    var rows = new List<Dictionary<string, object?>>();
                    bool truncated = false;
                    while (await reader.ReadAsync(ct))
                    {
                        if (rows.Count >= maxRows) { truncated = true; break; }
                        var row = new Dictionary<string, object?>(cols.Length);
                        for (int i = 0; i < cols.Length; i++)
                            row[string.IsNullOrEmpty(cols[i]) ? $"col{i}" : cols[i]] = ToJsonValue(reader.GetValue(i));
                        rows.Add(row);
                    }
                    resultSets.Add(new ResultSet(rows.Count, truncated, rows));
                } while (await reader.NextResultAsync(ct));
                recordsAffected = reader.RecordsAffected;
            }

            if (tx != null) await tx.RollbackAsync(ct);

            // Single-result metadata queries return just the rows array for compactness
            if (parameters != null && resultSets.Count == 1)
                return JsonSerializer.Serialize(resultSets[0].Rows, JsonOpts);

            return JsonSerializer.Serialize(new
            {
                mode = readOnly ? "read-only (rolled back)" : "write (committed)",
                recordsAffected,
                resultSets
            }, JsonOpts);
        }
        catch (SqlException ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message, number = ex.Number, line = ex.LineNumber }, JsonOpts);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return JsonSerializer.Serialize(new { error = ex.GetType().Name + ": " + ex.Message }, JsonOpts);
        }
    }

    private sealed record ResultSet(int RowCount, bool Truncated, List<Dictionary<string, object?>> Rows);

    private static object? ToJsonValue(object value) => value switch
    {
        DBNull => null,
        byte[] b => b.Length <= 64 ? "0x" + Convert.ToHexString(b) : $"<binary {b.Length} bytes>",
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss.fff"),
        TimeSpan t => t.ToString(),
        _ => value
    };
}
