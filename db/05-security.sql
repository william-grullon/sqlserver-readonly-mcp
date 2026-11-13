-- 05: read-only login. Password comes from sqlcmd variable McpReaderPassword (.env), never the repo.
-- Views and tables share owner dbo, so ownership chaining lets reporting.* read dbo.*
-- while mcp_reader cannot touch dbo directly.
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'mcp_reader')
    CREATE LOGIN mcp_reader WITH PASSWORD = N'$(McpReaderPassword)', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
ELSE
    ALTER LOGIN mcp_reader WITH PASSWORD = N'$(McpReaderPassword)';
GO

USE HarborSupply;
GO

IF DATABASE_PRINCIPAL_ID(N'mcp_reader') IS NULL
    CREATE USER mcp_reader FOR LOGIN mcp_reader;   -- member of public only
GO

GRANT SELECT ON SCHEMA::reporting TO mcp_reader;
DENY SELECT, INSERT, UPDATE, DELETE, EXECUTE, ALTER ON SCHEMA::dbo TO mcp_reader;
-- belt and braces: even a simple, updatable view can never be written through
DENY INSERT, UPDATE, DELETE, EXECUTE, ALTER ON SCHEMA::reporting TO mcp_reader;
GO
