USE [AlyvoFullTest20260912];
GO
SET XACT_ABORT ON;
SET LOCK_TIMEOUT 5000;
BEGIN TRANSACTION;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Orders') AND name=N'IX_Orders_CustomerId_OrderDate')
    DROP INDEX [IX_Orders_CustomerId_OrderDate] ON [dbo].[Orders];
EXEC(N'ALTER PROCEDURE [dbo].[Demo00_IndexCustomerOrders]
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        OrderId,
        OrderDate,
        Amount
    FROM dbo.Orders
    WHERE CustomerId = 123
    ORDER BY OrderDate;
END;');
EXEC(N'ALTER PROCEDURE [dbo].[Demo02_DailySalesReport]
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Status,
        COUNT_BIG(*) AS OrderCount,
        SUM(Amount) AS TotalAmount
    FROM dbo.Orders
    WHERE
        DATEDIFF(
            DAY,
            CONVERT(date, ''2025-01-01''),
            OrderDate
        ) = 0
    GROUP BY
        Status;
END;');
COMMIT;
GO
SET NOCOUNT ON;
DECLARE @i int=0;
WHILE @i<80 BEGIN EXEC dbo.Demo00_IndexCustomerOrders; SET @i+=1; END;
SET @i=0;
WHILE @i<20 BEGIN EXEC dbo.Demo02_DailySalesReport; SET @i+=1; END;
EXEC sys.sp_query_store_flush_db;
GO
