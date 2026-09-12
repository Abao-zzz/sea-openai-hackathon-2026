USE [AlyvoFullTest20260912];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @drop nvarchar(max)=N'';
SELECT @drop=@drop+N'DROP PROCEDURE '+QUOTENAME(SCHEMA_NAME(schema_id))+N'.'+QUOTENAME(name)+N';' FROM sys.procedures WHERE is_ms_shipped=0;
EXEC sys.sp_executesql @drop;
EXEC(N'CREATE PROCEDURE dbo.Demo00_IndexCustomerOrders AS
SELECT OrderId,OrderDate,Amount
FROM dbo.Orders
WHERE CustomerId=123
ORDER BY OrderDate;');
EXEC(N'CREATE PROCEDURE dbo.Demo10_OrderDetail AS
SELECT OrderId,CustomerId,OrderDate,Amount
FROM dbo.Orders
WHERE OrderId=123;');
EXEC(N'CREATE PROCEDURE dbo.Demo20_OrderEntry AS
BEGIN
 EXEC dbo.Demo10_OrderDetail;
END;');
COMMIT;
GO
SET NOCOUNT ON;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo00_IndexCustomerOrders;
EXEC dbo.Demo20_OrderEntry;
EXEC dbo.Demo20_OrderEntry;
EXEC sys.sp_query_store_flush_db;
GO
