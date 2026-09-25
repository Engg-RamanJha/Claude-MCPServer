#!/usr/bin/env node

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import sql from "mssql";

// ─── Connection Config ────────────────────────────────────────────────────────
const sqlConfig = {
  server: "localhost",
  database: "DB_Name",
  user: "user",
  password: "password",
  options: {
    trustServerCertificate: true,
    enableArithAbort: true,
  },
  pool: {
    max: 10,
    min: 0,
    idleTimeoutMillis: 30000,
  },
};

// ─── Connection Pool ──────────────────────────────────────────────────────────
let pool;

async function getPool() {
  if (!pool) {
    pool = await sql.connect(sqlConfig);
  }
  return pool;
}

// ─── MCP Server ───────────────────────────────────────────────────────────────
const server = new McpServer({
  name: "mssql-mcp-server",
  version: "1.0.0",
});

// ── Tool: query ───────────────────────────────────────────────────────────────
server.tool(
  "query",
  "Run a SELECT SQL query and return results as JSON",
  { sql: z.string().describe("The SELECT SQL query to execute") },
  async ({ sql: sqlText }) => {
    // Basic safety guard – allow only SELECT statements
    const trimmed = sqlText.trim().toUpperCase();
    if (!trimmed.startsWith("SELECT") && !trimmed.startsWith("WITH")) {
      return {
        isError: true,
        content: [{ type: "text", text: "Only SELECT / WITH queries are allowed via this tool. Use 'execute' for write operations." }],
      };
    }
    try {
      const p = await getPool();
      const result = await p.query(sqlText);
      return {
        content: [{ type: "text", text: JSON.stringify(result.recordset, null, 2) }],
      };
    } catch (err) {
      return { isError: true, content: [{ type: "text", text: `Query error: ${err.message}` }] };
    }
  }
);

// ── Tool: execute ─────────────────────────────────────────────────────────────
server.tool(
  "execute",
  "Run an INSERT / UPDATE / DELETE / DDL statement and return rows affected",
  { sql: z.string().describe("The SQL statement to execute") },
  async ({ sql: sqlText }) => {
    try {
      const p = await getPool();
      const result = await p.query(sqlText);
      return {
        content: [{ type: "text", text: `Rows affected: ${result.rowsAffected.join(", ")}` }],
      };
    } catch (err) {
      return { isError: true, content: [{ type: "text", text: `Execute error: ${err.message}` }] };
    }
  }
);

// ── Tool: list_tables ─────────────────────────────────────────────────────────
server.tool(
  "list_tables",
  "List all user tables in the connected database",
  {},
  async () => {
    try {
      const p = await getPool();
      const result = await p.query(
        "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_SCHEMA, TABLE_NAME"
      );
      return {
        content: [{ type: "text", text: JSON.stringify(result.recordset, null, 2) }],
      };
    } catch (err) {
      return { isError: true, content: [{ type: "text", text: `Error: ${err.message}` }] };
    }
  }
);

// ── Tool: describe_table ──────────────────────────────────────────────────────
server.tool(
  "describe_table",
  "Describe columns, types and nullability of a table",
  {
    table: z.string().describe("Table name (optionally schema-qualified, e.g. dbo.Orders)"),
  },
  async ({ table }) => {
    const [schema, tbl] = table.includes(".") ? table.split(".") : ["dbo", table];
    try {
      const p = await getPool();
      const result = await p.request()
        .input("schema", sql.NVarChar, schema)
        .input("table", sql.NVarChar, tbl)
        .query(
          `SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH,
                  IS_NULLABLE, COLUMN_DEFAULT
           FROM INFORMATION_SCHEMA.COLUMNS
           WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
           ORDER BY ORDINAL_POSITION`
        );
      return {
        content: [{ type: "text", text: JSON.stringify(result.recordset, null, 2) }],
      };
    } catch (err) {
      return { isError: true, content: [{ type: "text", text: `Error: ${err.message}` }] };
    }
  }
);

// ── Tool: list_stored_procedures ──────────────────────────────────────────────
server.tool(
  "list_stored_procedures",
  "List all stored procedures in the database",
  {},
  async () => {
    try {
      const p = await getPool();
      const result = await p.query(
        "SELECT ROUTINE_SCHEMA, ROUTINE_NAME FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'PROCEDURE' ORDER BY ROUTINE_SCHEMA, ROUTINE_NAME"
      );
      return {
        content: [{ type: "text", text: JSON.stringify(result.recordset, null, 2) }],
      };
    } catch (err) {
      return { isError: true, content: [{ type: "text", text: `Error: ${err.message}` }] };
    }
  }
);

// ─── Start ────────────────────────────────────────────────────────────────────
const transport = new StdioServerTransport();
await server.connect(transport);
console.error("MSSQL MCP Server running on stdio");
