# AI SQL Performance Engineer

> 先驗證，再套用。讓工程師依據可檢查的證據，決定是否採用 AI 提出的 SQL 優化。

## 一、產品說明

AI SQL Performance Engineer 是面向 SQL Server 開發者與資料庫維運人員的效能優化 Agent，協助分析、改寫與驗證既有預存程序（SP）。

產品以「先驗證，再套用」為核心，將依賴分析、AI 改寫、實測驗證與版本管理整合為可操作的流程，讓工程師在保留決策權的前提下，逐步改善既有 SQL。

## 二、解決的問題

企業長期累積的 SP，常有執行緩慢、呼叫關係複雜、缺少測試與版本紀錄等問題。工程師不僅要找出瓶頸，也必須確認修改不會改變商業結果，因此需要投入大量時間閱讀程式、手動比對與評估風險。

直接採用 AI 產生的改寫，可能出現結果不一致或效能未改善的情況。我們要解決的是：如何找出值得優化的目標，提供可檢查的驗證證據，再交由工程師決定是否採用。

## 三、所開發的解決方案

### 1. 找出值得優化的目標

系統先建立 SP 與 View 的依賴關係地圖，結合歷史執行資訊找出高成本目標；對含寫入、動態 SQL 或缺乏驗證依據的項目，先跳過並記錄原因。

### 2. 產生候選並執行安全預檢

符合條件的目標由 AI 產生改寫候選，通過唯讀性、可編譯性與輸出欄位等預檢後，在唯讀資料庫快照中實際執行。

### 3. 用實測證據判斷是否通過

系統比較原版與候選版在既定資料及參數下的欄位、列數、結果雜湊與邏輯讀取量，結果不一致或讀取量未降低就退回。

驗證結論限定於本次使用的資料、參數與執行環境，不代表涵蓋所有可能的商業情境。

### 4. 人工確認後套用與追蹤

通過驗證後建立候選版本，提供差異檢視與驗證紀錄，經人工確認才能正式套用，並支援還原與稽核追蹤。

**操作流程：** 依賴地圖與成本掃描 → AI 改寫 → 安全預檢 → Snapshot 實測 → 差異與證據審查 → 人工確認套用 → 版本、還原與稽核。

## 四、對應的開發方向

本作品以 SQL Server 效能優化為核心，將 AI 的自主執行能力、完整工作流程與資料庫專業結合，對應以下三項開發方向。

### 自主且具適應能力的 AI

Agent 可在設定的範圍與安全限制內，自主完成目標分析、SQL 改寫、檢查與實測，依驗證結果判定候選版本是否通過，無須工程師逐步操作。遇到不符合條件或驗證失敗的案例，系統會停止該項處理並保留原因；正式套用仍需人工確認。

目前著重於可控、可驗證的自主執行，後續可利用累積的驗證紀錄與失敗原因，發展能調整優化策略的適應能力。

### AI 原生產品與營運

以 AI 重新設計 SQL 效能優化的工作方式，串起「找出高成本目標、提出改寫、實測驗證、人工核准與版本管理」的完整流程。

工程師可減少反覆閱讀程式、整理資訊與手動比對的工作，將重心轉向檢視驗證證據、評估風險與決定是否採用，讓大量既有 SQL 的優化更容易批次執行、追蹤與管理。

### 深度領域 AI

依照資料庫工程師的實際工作需求，納入 Stored Procedure／View 依賴關係、唯讀性檢查、結果一致性與邏輯讀取量等判斷依據。候選版本必須在既定測試資料與參數下，通過結果比對且降低讀取量，才能進入後續採用流程。

搭配差異檢視、版本紀錄與還原機制，讓專業人員能根據可檢查的結果與可追溯的紀錄，判斷 AI 提出的修改是否值得採用。

## 五、Codex 在作品中的協助

Codex 在本次作品中的主要貢獻，是協助我們將產品構想與流程設計，落實為可操作、可執行、可驗證的產品原型。我們透過 Codex 協助功能實作、介面與後端串接、測試及錯誤修正，逐步將 SQL 分析、AI 改寫、結果驗證與版本管理等功能，整合成實際可運作的流程。

團隊負責定義產品需求、使用流程、安全邊界與驗收標準，Codex 則協助將這些設計轉化為程式碼，並依測試結果持續調整。

透過這樣的協作，我們讓產品從文字規劃走向可實際展示與測試的 Demo，進一步檢驗功能是否符合使用情境，以及哪些環節仍需要改善。

## 開發與驗收狀態

本 repository 目前保存 **5.0.0.4 驗收版原始碼**。本作品為可展示與測試的產品原型，尚未完成全部正式驗收，不應將產品流程說明視為所有驗收項目皆已通過。待驗收項目詳見 [WORK-IN-PROGRESS.md](WORK-IN-PROGRESS.md)。

<details>
<summary>開發者資訊：環境、目錄、建置、執行與測試</summary>

### 環境

- Windows、SSMS 22（預設安裝路徑）、.NET 10 SDK。
- SQL Server 2025（17.x），資料庫 compatibility level 170。
- VSIX：net472；LocalService：net10.0。

### 目錄

- `src/SsmsAiSqlAssistant`：SSMS VSIX，包含 AI 優化、Snapshot Dry Run、SP 地圖、掃描與管理功能。
- `tests/Stage2.Tests`：歷次階段累積的 VSIX 核心及 SQL 整合測試。
- `LocalService`：服務原始碼、測試與 API/UI 驗證工具。
- `demo`：隔離示範資料庫與測試查詢。
- `pitch`：原 repository 簡報資料。

### 建置與執行

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

### 測試

```powershell
dotnet test LocalService/LocalService.slnx -c Release
dotnet test tests/Stage2.Tests/Stage2.Tests.csproj -c Release --filter FullyQualifiedName~SafetyTests
```

完整 VSIX 測試包含真實 SQL Server 整合測試，需要先檢查各測試使用的隔離資料庫與權限；`demo` 提供階段五示範及掃描 fixture 腳本。請勿直接在正式資料庫執行測試或示範建置。

既有驗證紀錄：VSIX 核心回歸 90 項、LocalService 105 項通過；套用按鈕修改後安全測試 27 項通過。最新 5.0.0.4 UI 調整已建置並透過 SSMS 22 VSIXInstaller 安裝確認，尚未重新執行完整 UI 驗收。本次 Git 備份沒有重新執行回歸測試。

### 最近調整與限制

- 上方狀態卡提供「建立 Snapshot / Dry Run」及「套用 SQL」按鈕。
- Snapshot 按鈕提高底色與文字對比；安全門檻保持不變。
- 輸出欄位契約保留原有位置。
- Standard、全新機安裝、完整 MEF/UI、治理排程與部分安全邊界驗收仍待完成，詳見 `WORK-IN-PROGRESS.md`。
- 本機執行資料、安裝記錄、SQL plans、金鑰、第三方 DLL 與建置產物不納入提交。

</details>

