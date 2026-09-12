# SSMS AI SQL 效能助手

目前原始碼版本：**5.0.0.4 驗收版**。本次提交保存現有開發成果；尚未完成全部正式驗收，也不包含正式發佈套件。

## 環境

- Windows、SSMS 22（預設安裝路徑）、.NET 10 SDK。
- SQL Server 2025（17.x），資料庫 compatibility level 170。
- VSIX：net472；LocalService：net10.0。

## 目錄

- `src/SsmsAiSqlAssistant`：SSMS VSIX，包含 AI 優化、Snapshot Dry Run、SP 地圖、掃描與管理功能。
- `tests/Stage2.Tests`：歷次階段累積的 VSIX 核心及 SQL 整合測試。
- `LocalService`：服務原始碼、測試與 API/UI 驗證工具。
- `demo`：隔離示範資料庫與測試查詢。
- `pitch`：原 repository 簡報資料。

## 建置與執行

在 repository 根目錄執行：

```powershell
dotnet build LocalService/LocalService.slnx -c Release
dotnet run --project LocalService/src/LocalService/LocalService.csproj -c Release
```

服務預設位址為 `http://127.0.0.1:46217`。AI 功能需要在本機設定 `OPENAI_API_KEY` 與 `OPENAI_MODEL`，或使用設定工具配置；不要將金鑰提交到 Git。未配置金鑰時只提供 local-rules，不產生 AI 候選。

另開終端建置 VSIX 驗收包：

```powershell
dotnet build src/SsmsAiSqlAssistant/SsmsAiSqlAssistant.csproj -t:Rebuild -c Release -p:Stage5AcceptanceBuild=true
```

產物位於 `src/SsmsAiSqlAssistant/bin/Release/net472/Alyvo.SsmsAiSqlAssistant.vsix`。儲存查詢並關閉 SSMS 後，使用 SSMS 22 安裝目錄下的 `VSIXInstaller.exe` 安裝。此旗標僅產生驗收包。

## 測試

```powershell
dotnet test LocalService/LocalService.slnx -c Release
dotnet test tests/Stage2.Tests/Stage2.Tests.csproj -c Release --filter FullyQualifiedName~SafetyTests
```

完整 VSIX 測試包含真實 SQL Server 整合測試，需要先檢查各測試使用的隔離資料庫與權限；`demo` 提供階段五示範及掃描 fixture 腳本。請勿直接在正式資料庫執行測試或示範建置。

既有驗證紀錄：VSIX 核心回歸 90 項、LocalService 105 項通過；套用按鈕修改後安全測試 27 項通過。最新 5.0.0.4 UI 調整已建置並透過 SSMS 22 VSIXInstaller 安裝確認，尚未重新執行完整 UI 驗收。本次 Git 備份沒有重新執行回歸測試。

## 最近調整與限制

- 上方狀態卡提供「建立 Snapshot / Dry Run」及「套用 SQL」按鈕。
- Snapshot 按鈕提高底色與文字對比；安全門檻保持不變。
- 輸出欄位契約保留原有位置。
- Standard、全新機安裝、完整 MEF/UI、治理排程與部分安全邊界驗收仍待完成，詳見 `WORK-IN-PROGRESS.md`。
- 本機執行資料、安裝記錄、SQL plans、金鑰、第三方 DLL 與建置產物不納入提交。
