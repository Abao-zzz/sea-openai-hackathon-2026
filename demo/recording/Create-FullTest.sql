USE master;
GO
IF DB_ID(N'AlyvoFullTest20260912') IS NOT NULL THROW 51000,'Database already exists; no changes made.',1;
CREATE DATABASE [AlyvoFullTest20260912];
GO
ALTER DATABASE [AlyvoFullTest20260912] SET COMPATIBILITY_LEVEL=170;
ALTER DATABASE [AlyvoFullTest20260912] SET RECOVERY SIMPLE;
ALTER DATABASE [AlyvoFullTest20260912] SET QUERY_STORE=ON (OPERATION_MODE=READ_WRITE,QUERY_CAPTURE_MODE=ALL,MAX_STORAGE_SIZE_MB=256,INTERVAL_LENGTH_MINUTES=1);
GO
USE [AlyvoFullTest20260912];
GO
CREATE TABLE dbo.Customers(CustomerId int NOT NULL PRIMARY KEY,CustomerName nvarchar(80) NOT NULL,City nvarchar(30) NOT NULL,Email nvarchar(120) NOT NULL);
CREATE TABLE dbo.Products(ProductId int NOT NULL PRIMARY KEY,ProductName nvarchar(80) NOT NULL,CategoryId int NOT NULL,Price decimal(18,2) NOT NULL);
CREATE TABLE dbo.Orders(OrderId int NOT NULL PRIMARY KEY,CustomerId int NOT NULL REFERENCES dbo.Customers(CustomerId),OrderDate datetime2(0) NOT NULL,Status int NOT NULL,Amount decimal(18,2) NOT NULL,Notes nvarchar(150) NOT NULL);
CREATE TABLE dbo.OrderItems(ItemId int NOT NULL PRIMARY KEY,OrderId int NOT NULL REFERENCES dbo.Orders(OrderId),ProductId int NOT NULL REFERENCES dbo.Products(ProductId),Quantity int NOT NULL,UnitPrice decimal(18,2) NOT NULL);
CREATE TABLE dbo.Payments(PaymentId int NOT NULL PRIMARY KEY,OrderId int NOT NULL REFERENCES dbo.Orders(OrderId),PaidAt datetime2(0) NOT NULL,Amount decimal(18,2) NOT NULL);
CREATE TABLE dbo.AuditEvents(EventId int IDENTITY PRIMARY KEY,OccurredAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),Message nvarchar(100) NOT NULL);
INSERT dbo.Customers SELECT value,CONCAT(N'測試客戶',value),CHOOSE(value%4+1,N'台北',N'台中',N'台南',N'高雄'),CONCAT('customer',value,'@example.test') FROM GENERATE_SERIES(1,50000);
INSERT dbo.Products SELECT value,CONCAT(N'測試商品',value),value%40,CONVERT(decimal(18,2),value%5000+10) FROM GENERATE_SERIES(1,5000);
INSERT dbo.Orders SELECT value,(value*37)%50000+1,DATEADD(second,value%86400,DATEADD(day,value%730,CONVERT(datetime2(0),'20240101'))),value%5,CONVERT(decimal(18,2),value%20000+100),REPLICATE(N'測試訂單內容',12) FROM GENERATE_SERIES(1,500000);
INSERT dbo.OrderItems SELECT value,(value-1)/2+1,(value*13)%5000+1,value%5+1,CONVERT(decimal(18,2),value%5000+10) FROM GENERATE_SERIES(1,1000000);
INSERT dbo.Payments SELECT value,value,DATEADD(day,value%730,CONVERT(datetime2(0),'20240102')),CONVERT(decimal(18,2),value%20000+100) FROM GENERATE_SERIES(1,300000);
CREATE INDEX IX_Orders_OrderDate ON dbo.Orders(OrderDate) INCLUDE(CustomerId,Status,Amount);
CREATE INDEX IX_OrderItems_OrderId ON dbo.OrderItems(OrderId) INCLUDE(ProductId,Quantity,UnitPrice);
CREATE INDEX IX_Payments_OrderId ON dbo.Payments(OrderId) INCLUDE(Amount);
GO
CREATE VIEW dbo.vOrderSummary AS SELECT o.OrderId,o.CustomerId,c.CustomerName,o.OrderDate,o.Status,o.Amount FROM dbo.Orders o JOIN dbo.Customers c ON c.CustomerId=o.CustomerId;
GO
CREATE VIEW dbo.vPaidOrders AS SELECT o.OrderId,o.CustomerId,o.Amount,p.PaidAt FROM dbo.Orders o JOIN dbo.Payments p ON p.OrderId=o.OrderId;
GO
CREATE PROCEDURE dbo.OptimizeDaily01 AS SELECT Status,COUNT_BIG(*) AS OrderCount,SUM(Amount) AS TotalAmount FROM dbo.Orders WHERE DATEDIFF(day,CONVERT(date,'2025-01-01'),OrderDate)=0 GROUP BY Status;
GO
CREATE PROCEDURE dbo.OptimizeDaily02 AS SELECT Status,COUNT_BIG(*) AS OrderCount,SUM(Amount) AS TotalAmount FROM dbo.Orders WHERE DATEDIFF(day,CONVERT(date,'2025-01-02'),OrderDate)=0 GROUP BY Status;
GO
CREATE PROCEDURE dbo.OptimizeDaily03 AS SELECT Status,COUNT_BIG(*) AS OrderCount,SUM(Amount) AS TotalAmount FROM dbo.Orders WHERE DATEDIFF(day,CONVERT(date,'2025-01-03'),OrderDate)=0 GROUP BY Status;
GO
CREATE PROCEDURE dbo.OptimizeDaily04 AS SELECT Status,COUNT_BIG(*) AS OrderCount,SUM(Amount) AS TotalAmount FROM dbo.Orders WHERE DATEDIFF(day,CONVERT(date,'2025-01-04'),OrderDate)=0 GROUP BY Status;
GO
CREATE PROCEDURE dbo.OptimizeDaily05 AS SELECT Status,COUNT_BIG(*) AS OrderCount,SUM(Amount) AS TotalAmount FROM dbo.Orders WHERE DATEDIFF(day,CONVERT(date,'2025-01-05'),OrderDate)=0 GROUP BY Status;
GO
CREATE PROCEDURE dbo.OptimizeDaily06 AS SELECT Status,COUNT_BIG(*) AS OrderCount,SUM(Amount) AS TotalAmount FROM dbo.Orders WHERE DATEDIFF(day,CONVERT(date,'2025-01-06'),OrderDate)=0 GROUP BY Status;
GO
CREATE PROCEDURE dbo.OptimizeDaily07 AS SELECT Status,COUNT_BIG(*) AS OrderCount,SUM(Amount) AS TotalAmount FROM dbo.Orders WHERE DATEDIFF(day,CONVERT(date,'2025-01-07'),OrderDate)=0 GROUP BY Status;
GO
CREATE PROCEDURE dbo.OptimizeDaily08 AS SELECT Status,COUNT_BIG(*) AS OrderCount,SUM(Amount) AS TotalAmount FROM dbo.Orders WHERE DATEDIFF(day,CONVERT(date,'2025-01-08'),OrderDate)=0 GROUP BY Status;
GO
CREATE PROCEDURE dbo.OptimizeByDay @Day date='2025-01-01' AS SELECT Status,COUNT_BIG(*) AS OrderCount,SUM(Amount) AS TotalAmount FROM dbo.Orders WHERE DATEDIFF(day,@Day,OrderDate)=0 GROUP BY Status;
GO
CREATE PROCEDURE dbo.IndexCustomerOrders @CustomerId int=123 AS SELECT OrderId,OrderDate,Amount FROM dbo.Orders WHERE CustomerId=@CustomerId ORDER BY OrderDate;
GO
CREATE PROCEDURE dbo.IndexProductItems @ProductId int=123 AS SELECT ItemId,OrderId,Quantity,UnitPrice FROM dbo.OrderItems WHERE ProductId=@ProductId;
GO
CREATE PROCEDURE dbo.AlreadyOptimized @OrderId int=123 AS SELECT OrderId,CustomerId,OrderDate,Amount FROM dbo.Orders WHERE OrderId=@OrderId;
GO
CREATE PROCEDURE dbo.IndependentCustomerCount AS SELECT COUNT_BIG(*) AS CustomerCount FROM dbo.Customers;
GO
CREATE PROCEDURE dbo.IndependentProducts AS SELECT CategoryId,COUNT_BIG(*) AS ProductCount FROM dbo.Products GROUP BY CategoryId;
GO
CREATE PROCEDURE dbo.MapDashboard AS
BEGIN
 EXEC dbo.MapSales;
 EXEC dbo.MapCustomer;
END;
GO
CREATE PROCEDURE dbo.MapSales AS
BEGIN
 EXEC dbo.MapDaily;
 EXEC dbo.MapPayments;
END;
GO
CREATE PROCEDURE dbo.MapDaily AS
BEGIN
 EXEC dbo.OptimizeDaily01;
 EXEC dbo.OptimizeDaily02;
END;
GO
CREATE PROCEDURE dbo.MapCustomer AS
BEGIN
 EXEC dbo.IndexCustomerOrders;
 EXEC dbo.MapOrderDetail;
END;
GO
CREATE PROCEDURE dbo.MapOrderDetail AS EXEC dbo.AlreadyOptimized;
GO
CREATE PROCEDURE dbo.MapPayments AS SELECT COUNT_BIG(*) AS PaidOrderCount,SUM(Amount) AS TotalAmount FROM dbo.vPaidOrders;
GO
CREATE PROCEDURE dbo.MapViewSummary AS SELECT Status,COUNT_BIG(*) AS OrderCount FROM dbo.vOrderSummary GROUP BY Status;
GO
CREATE PROCEDURE dbo.SkipWrites AS INSERT dbo.AuditEvents(Message) VALUES(N'Demo write procedure');
GO
CREATE PROCEDURE dbo.SkipDynamicSql AS EXEC sys.sp_executesql N'SELECT COUNT_BIG(*) AS N FROM dbo.Products';
GO
CREATE PROCEDURE dbo.SkipOutputParameter @Count bigint OUTPUT AS SELECT @Count=COUNT_BIG(*) FROM dbo.Customers;
GO
CREATE PROCEDURE dbo.SkipConditional @Mode int=1 AS IF @Mode=1 SELECT COUNT_BIG(*) AS N FROM dbo.Products; ELSE SELECT COUNT_BIG(*) AS N FROM dbo.Customers;
GO
CREATE PROCEDURE dbo.SkipEncrypted WITH ENCRYPTION AS SELECT COUNT_BIG(*) AS N FROM dbo.Products;
GO
CREATE PROCEDURE dbo.NoExecutionEvidence AS SELECT SUM(Amount) AS TotalAmount FROM dbo.Payments;
GO
CREATE PROCEDURE dbo.MapCycleA AS EXEC dbo.MapCycleB;
GO
CREATE PROCEDURE dbo.MapCycleB AS EXEC dbo.MapCycleA;
GO
EXEC sys.sp_updatestats;
GO
