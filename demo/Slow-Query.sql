SELECT CustomerId, SUM(Amount) AS TotalAmount FROM dbo.Orders WHERE DATEDIFF(day, CONVERT(date,'2025-01-01'), OrderDate)=0 GROUP BY CustomerId;
