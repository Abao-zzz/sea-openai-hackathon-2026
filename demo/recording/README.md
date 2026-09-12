# 錄影用資料庫

僅供本機示範資料庫 AlyvoFullTest20260912，不可在正式資料庫使用。

全新環境依序執行 Create-FullTest.sql、Prepare-Video-Demo.sql、Reset-And-New-Rewrite.sql。第一支建立約 185 萬筆資料；第二支刪除該示範庫既有使用者 SP，改為精簡錄影案例；第三支建立分行的 Demo02 並保留 Demo00 索引案例。

已有 Demo00 / Demo02 時，使用 Reset-Demo00-Demo02.sql 重新錄影：移除指定的客戶日期索引、還原兩支 SP 原始定義，並分別執行 80 / 20 次補充統計。此腳本會覆寫這兩支 SP 的修改，但保留應用程式版本歷史。

- Demo00_IndexCustomerOrders：新增 CustomerId、OrderDate 索引的案例。
- Demo02_DailySalesReport：將 DATEDIFF 改成日期範圍，使用既有索引，不需新索引。

重新整理掃描及地圖後，重新產生 AI 候選。測試結果以當次實測為準。
