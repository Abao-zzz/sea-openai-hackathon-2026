# 階段五：尚未完成最終驗收

目前僅供開發與驗收，不是正式發佈。

- VSIX net472，LocalService net10.0。
- 90 項 VSIX 核心回歸通過；105 項 LocalService 測試通過，CSP 修正另有 4 項通過。
- 三種 collation 的 SQL Server 2025 Developer 實测完成；Standard 與全新機驗收尚未執行。
- 驗收版 5.0.0.4 已透過 SSMS 22 VSIXInstaller 升級；仍須實機 MEF / UI 驗收。
- 沒有產生正式 VSIX、publish package、正式安裝腳本或 release SHA-256。

實作涵蓋 Snapshot 預算／殘留阻擋／sparse file 檢查、串流 multiset fingerprint、多結果集、collation、精確 Query Store 1+3 路徑、actual plan 本機保存、SQLite 分析與套用歷史、服務端歷史／用量／治理 UI、連線中唯讀排程、變更稽核同步、設定視窗、示範資料。

尚待完成：SSMS 新功能實機操作與完整畫面驗收、5 支真實 AI 批次通過證據、治理排程端對端、全部安全邊界測試、安裝 repair/conflict/uninstall/全新機矩陣及 DPI/theme 驗收。未通過的項目不得以既有測試或舊截圖取代。

舊驗收資料庫 dbo.CostlyDailyOrders 已由使用者套用為優化版本。階段五回歸使用新建 AlyvoStage5SpScanAcceptance，未回改該使用者版本。

## 5.0.0.19 更新

目前備份版本包含 SP 版本、呼叫行號、schema/index AI 建議與來源資料庫交易索引測試。各次有針對性建置與測試，但不取代上述完整驗收。最新焦點判斷修正已建置並安裝，實機操作待確認；仍為驗收版，不是正式發佈。
