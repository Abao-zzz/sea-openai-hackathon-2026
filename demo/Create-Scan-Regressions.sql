USE master;
GO
IF DB_ID(N'AlyvoStage5SpScanAcceptance') IS NOT NULL THROW 51000,'Dedicated fixture already exists; no changes made.',1;
CREATE DATABASE AlyvoStage5SpScanAcceptance;
GO
ALTER DATABASE AlyvoStage5SpScanAcceptance SET COMPATIBILITY_LEVEL=170;
ALTER DATABASE AlyvoStage5SpScanAcceptance SET QUERY_STORE=ON (OPERATION_MODE=READ_WRITE,QUERY_CAPTURE_MODE=ALL);
GO
USE AlyvoStage5SpScanAcceptance;
GO
CREATE TABLE dbo.ScanOrders(OrderId int NOT NULL PRIMARY KEY,CustomerId int NOT NULL,OrderDate datetime2(3) NOT NULL,Amount decimal(18,2) NOT NULL);
WITH n AS(SELECT TOP(100000) ROW_NUMBER() OVER(ORDER BY(SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) INSERT dbo.ScanOrders SELECT n,n%100,DATEADD(day,n%730,CONVERT(datetime2(3),'20240101')),n%10000 FROM n;
CREATE INDEX IX_OrderDate ON dbo.ScanOrders(OrderDate) INCLUDE(CustomerId,Amount);
GO
CREATE PROCEDURE dbo.CostlyDailyOrders @Day date='2025-01-01' AS BEGIN SET NOCOUNT ON;SELECT CustomerId,SUM(Amount) AS TotalAmount FROM dbo.ScanOrders WHERE DATEDIFF(day,@Day,OrderDate)=0 GROUP BY CustomerId;END;
GO
CREATE PROCEDURE dbo.AlreadyLean @Id int=7 AS SELECT OrderId,CustomerId,Amount FROM dbo.ScanOrders WHERE OrderId=@Id;
GO
CREATE PROCEDURE dbo.EncryptedDefinition WITH ENCRYPTION AS SELECT 1 AS n;
GO
CREATE PROCEDURE dbo.HasSideEffect AS UPDATE dbo.ScanOrders SET Amount=Amount+1 WHERE OrderId=1;
GO
CREATE PROCEDURE dbo.CallsAnother AS EXEC dbo.AlreadyLean;
GO
CREATE PROCEDURE dbo.OutputParameter @n int OUTPUT AS SELECT COUNT_BIG(*) AS n FROM dbo.ScanOrders;
GO
CREATE PROCEDURE dbo.NoCostEvidence AS SELECT COUNT_BIG(*) AS n FROM dbo.ScanOrders;
GO
CREATE PROCEDURE dbo.ConditionalBody AS IF 1=1 SELECT 1 AS n;
GO
EXEC dbo.CostlyDailyOrders;
EXEC dbo.AlreadyLean;
EXEC sys.sp_query_store_flush_db;
GO
