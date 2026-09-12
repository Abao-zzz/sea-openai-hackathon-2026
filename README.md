# SQL Performance Agent

> AI 可以提案，但必須先證明結果一致、效能真的改善，才交給人決定是否套用。

## 一、產品說明

SQL Performance Agent 是面向 SQL Server 開發者與資料庫維運人員的 SP 效能優化 Agent。

產品整合在既有工作流程中，從盤點 Stored Procedure、追蹤上下游相依、提出改寫、執行驗證，到建立版本與還原紀錄，讓「找問題、改 SQL、驗證結果、決定是否套用」形成完整流程。

核心原則是：**AI 可以提案，但必須先證明結果一致、效能真的改善，才交給人決定是否套用。**

## 二、解決的問題

企業資料庫常累積大量歷史 SP，可能存在層層相依、缺乏測試、沒有版本控制，甚至原開發者已離職等情況。過去工程師要先看執行計畫、找瓶頸、改寫 SQL，再重新執行驗證，反覆確認才敢修改。

AI 雖然已能快速產生改寫，但仍可能出現兩種問題：一是改壞原本語意卻不報錯，二是寫法看起來更漂亮，實際讀取量卻沒有改善。因此真正缺少的不是「生成」，而是「能否安全採用的證明」。

## 三、所開發的解決方案

系統將 SP 優化拆成四個步驟：

1. **掃描現有 SP**：找出優先處理的高成本項目。
2. **建立 SP 呼叫追蹤**：先看清上下游關聯，再決定改善對象。
3. **提出改寫並驗證**：由 Agent 提出 SQL 改寫，交由程式執行驗證，未通過就退回。
4. **建立版本與人工套用**：通過後先建立版本，由人確認才套用，並保留還原能力。

驗證採三層方式：

- **結構檢查**：透過 AST 解析、結構檢查與修改比對，確認 SQL 是否符合允許的改寫範圍。
- **結果比對**：在資料庫快照中，比較原版與候選版的欄位、列數與結果雜湊是否一致。
- **效能驗證**：以 logical reads 與執行計畫檢查判定是否達到改善條件，並呈現 CPU 與耗時等資訊供審查。

驗證結果由程式規則產生，Agent 的角色是把結果、依據與下一步建議解釋清楚，而不是由模型自己判定自己是否正確。驗證結論限定於本次使用的資料、參數與執行環境。

**操作流程：** 高成本掃描 → SP 呼叫追蹤 → AI 改寫 → 結構與結果驗證 → 效能比對 → 建立版本 → 人工確認套用與還原。

## 四、對應的開發方向

### 自主 AI 的發展方向

目前 Agent 已參與 SP 掃描、改寫與驗證，並依驗證結果提供通過或退回的判定依據；正式套用仍保留人工確認。

現階段屬於半自動化工作流，未來會進一步完善批次、排程與更高程度的自主執行。

### AI 原生產品與營運

本作品重新設計 SP 優化流程，讓 AI 直接參與發現問題、提出改寫、驗證結果與整理判定依據，工程師則把重心放在審查證據與決定是否套用。

### 深度領域 AI

產品專注 SQL Server 與 Stored Procedure 場景，把 AST、結果比對、logical reads、執行計畫、SP 相依關係與版本管理等資料庫專業能力整合進工作流，讓 AI 的輸出能符合實際資料庫工程的操作方式。

目前原型已實作掃描與呼叫追蹤、AI 改寫與驗證、版本紀錄與還原。批次與排程已有部分實作，後續將持續完善與驗收；更多 SQL Server 環境驗收與 Codex MCP 整合也列為下一步。

## 五、Codex 在作品中的協助

Codex 在本次作品中從產品構想到實作與呈現全程參與。前期協助團隊討論需求、推敲解法與拆解功能；開發階段協助完成 Agent 與 SSMS Extension，將掃描、呼叫追蹤、改寫、驗證與版本流程落成可操作原型；後期也協助使用 Slidev 製作簡報、調整文案、版面與互動。

團隊負責產品方向、驗證邏輯、安全邊界與最終審查，Codex 則協助把設計快速轉化為可執行成果。透過這樣的協作，我們讓原本停留在構想與流程設計的產品，實際成為可以操作、展示與測試的原型。

依團隊統計，本次 hackathon 合計使用約 **1.83 億 tokens**，也反映 Codex 在整體開發流程中的高度參與。

## 開發與驗收狀態

本 repository 目前保存 **5.0.0.19 驗收版原始碼**。本作品為可展示與測試的產品原型，尚未完成全部正式驗收，不應將產品流程說明視為所有驗收項目皆已通過。待驗收項目詳見 [WORK-IN-PROGRESS.md](WORK-IN-PROGRESS.md)。

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

驗證紀錄按修改範圍執行，並非本版本全部功能已完成驗收：交易索引相關 9 項測試通過；後續簡化顯示與無建議狀態的 3 項相關測試通過。5.0.0.19 連線焦點修正已建置成功（0 錯誤、6 警告）並透過 SSMS 22 VSIXInstaller 安裝；實機焦點切換操作待使用者確認。本次 Git 備份沒有重跑完整回歸測試。

### 最近調整與限制

- SP 新舊 SQL 並排比較；每支可讀取定義的 SP 自動建立初始版本，版本下拉選單可查閱歷史，還原會新增版本。
- SP 呼叫地圖標示呼叫下一層 SP 的原始碼行號。
- AI 優化與 SP 掃描會提供資料表 schema、既有索引資訊，支援產生索引／統計資訊建議 SQL。
- 支援的非唯一、非叢集 CREATE INDEX 建議會在原畫面自動執行交易測試：原版三次、交易內建立索引後三次，最後 rollback。效能比較放在建議 SQL 與複製按鈕上方。
- 結果一致、計畫符合檢查且 median logical reads 降低至少 5%，才開放「確認建立索引」。只有人工確認才永久提交；提交前重新檢查資料庫、結構、索引狀態與測試有效期限。
- 交易測試直接在來源資料庫鎖住目標資料表，並非獨立資料庫隔離；可能阻塞其他工作。效能證據僅限所測查詢、資料與參數，未涵蓋寫入成本或所有參數。
- AI 沒有新建議時僅顯示「目前沒有進一步優化建議」，不誤稱實測通過。只有完成結果一致的前後實測，且目前版本讀取量不高於建議版本時，才顯示「目前已是本次測試中的最優版本」。
- 瀏覽物件總管其他資料庫時不再由背景輪詢誤判查詢連線變更；回到編輯器才檢查連線。
- Standard、全新機安裝、完整 MEF/UI、治理排程與部分安全邊界驗收仍待完成，詳見 `WORK-IN-PROGRESS.md`。
- 本機執行資料、安裝記錄、SQL plans、金鑰、第三方 DLL 與建置產物不納入提交。

</details>

