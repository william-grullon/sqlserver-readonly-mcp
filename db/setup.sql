-- Single entry point. Run with sqlcmd from the db/ folder (the :r paths are relative):
--   sqlcmd -C -b -I -i setup.sql -v McpReaderPassword="..."
-- Re-runnable: every step checks before it creates or seeds.
:on error exit
:r 01-database.sql
:r 02-tables.sql
:r 03-seed.sql
:r 04-views.sql
:r 05-security.sql
PRINT N'HarborSupply setup complete.';
GO
