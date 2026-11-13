-- 01: database + compatibility level 110 (SQL Server 2012 T-SQL behavior)
IF DB_ID(N'HarborSupply') IS NULL
    CREATE DATABASE HarborSupply;
GO
ALTER DATABASE HarborSupply SET COMPATIBILITY_LEVEL = 110;
GO
ALTER DATABASE HarborSupply SET RECOVERY SIMPLE;
GO
