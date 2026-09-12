USE master;
GO
IF DB_ID(N'AlyvoStage5Demo2') IS NOT NULL THROW 51000, 'Demo database already exists; no changes made.', 1;
CREATE DATABASE [AlyvoStage5Demo2] COLLATE Latin1_General_100_CI_AI;
GO
ALTER DATABASE [AlyvoStage5Demo2] SET COMPATIBILITY_LEVEL=170;
ALTER DATABASE [AlyvoStage5Demo2] SET QUERY_STORE=ON (OPERATION_MODE=READ_WRITE,QUERY_CAPTURE_MODE=ALL);
GO
USE [AlyvoStage5Demo2];
GO
CREATE TABLE dbo.Orders(Id int NOT NULL PRIMARY KEY, CustomerId int NOT NULL, OrderDate datetime2(3) NOT NULL, Amount decimal(18,2) NOT NULL, Label nvarchar(40) NULL);
WITH n AS (SELECT TOP (100000) ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) INSERT dbo.Orders SELECT n,n%1000,DATEADD(day,n%730,CONVERT(datetime2(3),'20240101')),CONVERT(decimal(18,2),n%10000)/100,CASE n%5 WHEN 0 THEN NULL WHEN 1 THEN N'' WHEN 2 THEN N'null' WHEN 3 THEN N'É' ELSE N'e' END FROM n;
CREATE INDEX IX_Orders_Date ON dbo.Orders(OrderDate) INCLUDE(CustomerId,Amount,Label);
GO
CREATE PROCEDURE dbo.BatchCandidate1 AS SELECT CustomerId,SUM(Amount) AS TotalAmount FROM dbo.Orders WHERE DATEDIFF(day,CONVERT(date,'2025-01-01'),OrderDate)=0 GROUP BY CustomerId;
GO
CREATE PROCEDURE dbo.BatchCandidate2 AS SELECT CustomerId,SUM(Amount) AS TotalAmount FROM dbo.Orders WHERE DATEDIFF(day,CONVERT(date,'2025-01-02'),OrderDate)=0 GROUP BY CustomerId;
GO
CREATE PROCEDURE dbo.BatchCandidate3 AS SELECT CustomerId,SUM(Amount) AS TotalAmount FROM dbo.Orders WHERE DATEDIFF(day,CONVERT(date,'2025-01-03'),OrderDate)=0 GROUP BY CustomerId;
GO
CREATE PROCEDURE dbo.BatchCandidate4 AS SELECT CustomerId,SUM(Amount) AS TotalAmount FROM dbo.Orders WHERE DATEDIFF(day,CONVERT(date,'2025-01-04'),OrderDate)=0 GROUP BY CustomerId;
GO
CREATE PROCEDURE dbo.BatchCandidate5 AS SELECT CustomerId,SUM(Amount) AS TotalAmount FROM dbo.Orders WHERE DATEDIFF(day,CONVERT(date,'2025-01-05'),OrderDate)=0 GROUP BY CustomerId;
GO
CREATE PROCEDURE dbo.Map01 AS EXEC dbo.Map02;
GO
CREATE PROCEDURE dbo.Map02 AS EXEC dbo.Map03;
GO
CREATE PROCEDURE dbo.Map03 AS EXEC dbo.Map04;
GO
CREATE PROCEDURE dbo.Map04 AS EXEC dbo.Map05;
GO
CREATE PROCEDURE dbo.Map05 AS EXEC dbo.Map06;
GO
CREATE PROCEDURE dbo.Map06 AS EXEC dbo.Map07;
GO
CREATE PROCEDURE dbo.Map07 AS EXEC dbo.Map08;
GO
CREATE PROCEDURE dbo.Map08 AS EXEC dbo.Map09;
GO
CREATE PROCEDURE dbo.Map09 AS EXEC dbo.Map10;
GO
CREATE PROCEDURE dbo.Map10 AS EXEC dbo.Map11;
GO
CREATE PROCEDURE dbo.Map11 AS EXEC dbo.Map12;
GO
CREATE PROCEDURE dbo.Map12 AS EXEC dbo.Map13;
GO
CREATE PROCEDURE dbo.Map13 AS EXEC dbo.Map14;
GO
CREATE PROCEDURE dbo.Map14 AS EXEC dbo.Map15;
GO
CREATE PROCEDURE dbo.Map15 AS EXEC dbo.Map16;
GO
CREATE PROCEDURE dbo.Map16 AS EXEC dbo.Map17;
GO
CREATE PROCEDURE dbo.Map17 AS EXEC dbo.Map18;
GO
CREATE PROCEDURE dbo.Map18 AS EXEC dbo.Map19;
GO
CREATE PROCEDURE dbo.Map19 AS EXEC dbo.BatchCandidate1;
GO
CREATE PROCEDURE dbo.Map20 AS SELECT COUNT_BIG(*) AS n FROM dbo.Orders;
GO
CREATE PROCEDURE dbo.RejectWrite AS UPDATE dbo.Orders SET Amount=Amount+1 WHERE Id=1;
GO
CREATE PROCEDURE dbo.RejectDynamic AS EXEC(N'SELECT COUNT(*) FROM dbo.Orders');
GO
CREATE PROCEDURE dbo.RejectOutput @n int OUTPUT AS SELECT @n=COUNT(*) FROM dbo.Orders;
GO
EXEC dbo.BatchCandidate1;
EXEC dbo.BatchCandidate2;
EXEC dbo.BatchCandidate3;
EXEC dbo.BatchCandidate4;
EXEC dbo.BatchCandidate5;
EXEC sys.sp_query_store_flush_db;
GO
