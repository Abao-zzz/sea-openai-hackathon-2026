---
theme: default
title: 沒人敢碰的 SQL，從此有人接手。
titleTemplate: '%s · 肥宅快樂水'
aspectRatio: 16/9
canvasWidth: 980
colorSchema: light
transition: fade
clickAnimation: fade-in
fonts:
  provider: none
  sans: Noto Sans TC
  serif: Noto Serif TC
  mono: JetBrains Mono
  local:
    - Noto Sans TC
    - Noto Serif TC
    - JetBrains Mono
lineNumbers: false
download: false
drawings:
  persist: false
exportFilename: pitch-deck
favicon: /favicon.svg
defaults:
  layout: pitch
layout: pitch
pagenum: false
centered: true
---

<div class="title-grid">
<div class="title-block">
<h1 class="title-hero"><span class="rv" style="display:block">沒人敢碰的 SQL，</span><span class="rv" style="display:block;--d:220">從此有人接手。</span></h1>
<div class="title-sub rv" style="--d:600">一個會自己說「不」的 SP 優化 agent。
驗證通過，才交給人套用。</div>
<div class="rule rv grow" style="--d:900"></div>
<div class="title-team rv" style="--d:1100">肥宅快樂水</div>
<div class="title-event rv" style="--d:1200">2026 Sea x OpenAI Regional Codex Hackathon TW</div>
</div>
<div class="title-aside">
<div class="fan">
<img class="sticker fan-left rv zoom" style="--d:1400;--rot:-9deg" src="/img/meme-3.jpg" alt="梗圖：the first rule of IT — if it works don't touch it?">
<img class="sticker fan-right rv zoom" style="--d:1650;--rot:8deg" src="/img/meme-2.jpg" alt="梗圖：水管接好了，水卻沒流進去">
<img class="sticker fan-front rv zoom" style="--d:1950;--rot:-2deg" src="/img/meme.jpg" alt="梗圖：If it works, don't touch it.">
</div>
</div>
</div>

<!--
大家好，我們是肥宅快樂水。今天帶來的是一個會自己說「不」的 SP 優化 agent。候選通過驗證後，才交給人確認套用。
-->

---
eyebrow: 01 · PROBLEM · 問題
---

# 企業資料庫裡，沒人敢碰的 SQL

<div class="s3-body">
<div class="grid-3 problem-stats">
<StatCard class="rv" style="--d:200"><template #value><CountUp :to="500" suffix="+" :delay="500" /></template>Stored Procedure</StatCard>
<StatCard class="rv" style="--d:380"><template #value>？支</template>半年未使用</StatCard>
<StatCard class="rv" style="--d:560"><template #value>2–3 hr</template>SP 層層相依</StatCard>

<StatCard class="rv" style="--d:1900"><template #value>0</template>測試</StatCard>
<StatCard class="rv" style="--d:2150"><template #value>沒有</template>版本控制</StatCard>
<StatCard class="rv" style="--d:2400"><template #value>已離職</template>開發者</StatCard>
</div>
</div>

<!--
我們過去在銀行、電商、零售與保險的專案裡，一間公司就可能有 500 多支 Stored Procedure。

其中有多少支已經半年沒人用？沒人查過，也沒人敢刪。畫面上的問號代表數量未知，不代表已經確認它們沒人使用。

最複雜的那一支，跑一次可能要 2 到 3 小時。裡面 SP 套 SP，層層相依，沒人拆得動，也沒人敢拆。

再加上沒有測試、沒有版本控制，寫的人已經離職。大家不敢改，因為不知道改了會影響誰。

備註：數字為團隊過往專案經驗。沒有歷史執行紀錄，不等於可以判定為 dead code。
-->

---
eyebrow: 02 · BEFORE AGENTS · 開發者怎麼做
---

# 改一支 SP，先追半天。

<ManualWorkflow />

<!--
在 AI agent 之前，開發者通常先跑 SP、看執行計畫，再搭配實際耗時與 logical reads 找出瓶頸，判斷 SQL 可以怎麼改。改完還要重跑，確認結果一致、效能是否改善；沒有改善就再找原因。圖是流程示意，不代表特定 SP 的量測結果。「追半天」描述排查往返的感受，不是固定工時。接下來帶到：AI 可以加快改寫，但仍需要證明改得對、真的有改善。
-->

---
eyebrow: 03 · WHY NOT SOLVED · 為什麼 AI 還沒解決
---

# AI 早就會改。錯的方式有兩種。

<div class="grid-2 wrongs">
<div class="card rv" style="--d:200">
<div class="card-kicker">錯法一 · 改壞語意</div>
<div class="diff">
<div class="diff-line ctx"><span class="g"> </span><span>-- 找出 Region 為空的客戶</span></div>
<div class="diff-line del"><span class="g">-</span><span>WHERE ISNULL(c.Region, '') = ''</span></div>
<div class="diff-line add"><span class="g">+</span><span><TypeIn text="WHERE c.Region = ''" :delay="900" /></span></div>
</div>
<div class="failure-result">
<div class="failure-headline">漏了資料</div>
<div class="failure-evidence">NULL 列消失，卻不報錯</div>
</div>
</div>
<div class="card rv" style="--d:1500">
<div class="card-kicker">錯法二 · 看起來更快，其實沒快</div>
<div class="diff">
<div class="diff-line ctx"><span class="g"> </span><span>-- 找出某一天的訂單</span></div>
<div class="diff-line del"><span class="g">-</span><span>WHERE CONVERT(date, CreatedAt) = @d</span></div>
<div class="diff-line add"><span class="g">+</span><span><TypeIn text="WHERE CreatedAt >= @d&#10;  AND CreatedAt < DATEADD(day, 1, @d)" :delay="2100" /></span></div>
</div>
<div class="failure-result">
<div class="failure-headline">讀取量沒變</div>
<div class="failure-evidence">無索引 <span class="failure-metric">49 → 49</span> <span class="failure-unit">logical reads</span></div>
</div>
</div>
</div>

<div class="close xl rv" style="--d:3600">缺的不是生成，是證明。</div>

<!--
第一個例子把 ISNULL 拿掉，可能更利於索引查找，但會遺漏 Region 為 NULL 的列。第二個改成日期半開區間，實際效益仍依索引與資料而定。49 與 49 是 handoff 的測試案例值。模型可以提案，資料庫驗證才能決定是否採用。
-->

---
eyebrow: 04 · SOLUTION · 我們的解法
clicks: 3
---

# 讓 Agent 接手 SP 優化流程。

<SolutionSteps />


<!--
連接 DB 後，先盤點目前有哪些 SP，並查看哪些成本最高，再透過 SP 呼叫追蹤看清上下游，選擇優化對象。這裡像 Function Trace，呈現的是呼叫相依關係，不代表已支援執行期追蹤。掃描只讀 catalog、DMV 與 Query Store，跳過項目都保留理由。優化先過六關安全預檢，再於唯讀 Database Snapshot 執行候選。通過後先建立版本，再由人確認套用。驗證階段不修改正式 SP，明確套用時才寫回定義。
-->

---
eyebrow: 05 · DEMO
---

<DemoStage class="rv" style="--d:300" />

<!--
先看掃描結果，再看 SP 呼叫追蹤。接著看沒有索引的改寫被退回，再看結果一致、logical reads 下降的案例。通過候選先成為未套用版本，人確認後才套用，最後展示還原。此頁以影片呈現，錄影尚未加入，目前保留影片框。
-->

---
eyebrow: 06 · VERIFICATION · 驗證方式
---

# 三層驗證，才有依據。

<div class="proof-list">
<div class="proof-row rv" style="--d:200"><span class="proof-index">01</span><div class="proof-prompt">SQL 語法<mark>改了什麼？</mark></div><div class="proof-method">抽象語法樹比對<span>AST Comparison</span></div></div>
<div class="proof-row rv" style="--d:450"><span class="proof-index">02</span><div class="proof-prompt">結果<mark>一致？</mark></div><div class="proof-method">小資料對照<span>模擬環境測試</span></div></div>
<div class="proof-row rv" style="--d:700"><span class="proof-index">03</span><div class="proof-prompt">真的<mark>更快？</mark></div><div class="proof-method">效能量測<span>Logical reads · 執行計畫</span></div></div>
</div>

<!--
驗證設計分三層：AST comparison 用來檢查結構差異與禁止操作，本身不證明語意等價。模擬環境的小資料測試，以相同資料及參數執行原版與候選，比較欄位、列數與值，涵蓋 NULL、重複值及日期邊界；只支持已測案例的一致性。效能以可比較的資料、參數與環境量測 logical reads、耗時，並檢查執行計畫。Query Store 提供歷史基準，其 runtime stats 是按計畫及時間區間彙總，不能把不同工作負載的平均值直接當成同次測試結果。受控實跑可用 STATISTICS IO 與 Actual Execution Plan。小資料的效能不能直接外推正式規模，索引被使用也不代表效能改善。這是團隊確認的驗證設計，具體完成範圍仍需與產品實作核對。

Microsoft 參考：https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-query-store-runtime-stats-transact-sql
https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-query-store-plan-transact-sql
https://learn.microsoft.com/en-us/sql/t-sql/statements/set-statistics-io-transact-sql
-->

---
eyebrow: 07 · VERDICT · 判定與建議
---

# 程式執行驗證，Agent 解釋結果。

<VerificationHandoff />

<!--
驗證由 rule-based code 執行：程式執行 AST 比對、結果比對與效能量測，依明確規則輸出檢查結果、量測值與判定。這些驗證結果再交給 Agent，由它解釋通過或退回的原因，提出下一步建議。Agent 的解釋不能覆寫驗證判定；證據不足時應明確指出缺少什麼。Agent 可以協調工作流程，但驗證結果來自程式執行，而不是模型憑文字判斷。最後仍由人確認套用。
-->

---
eyebrow: 08 · VALUE · 應用價值
---

# 省下工時，也讓技術債有機會被處理。


<div class="value-examples">
<section class="value-example rv" style="--d:200"><div class="value-example-label">開發者人工投入 · 團隊經驗估算</div><div class="value-example-change">40 <small>小時</small><span>→</span>2 <small>小時</small></div><div class="value-example-result">工時可減少 <mark>95%</mark></div></section>
<section class="value-example rv" style="--d:400"><div class="value-example-label">SQL 調校參考</div><div class="value-example-change" style="font-size:2.65rem">28,281<span>→</span>20</div><div class="value-example-result">單表 logical reads 降低 <mark>99.93%</mark></div></section>
</div>
<div class="value-possibilities">
<div class="rv" style="--d:650"><div class="value-old">陳年 SP，沒人敢碰</div><div class="value-new">有依據地<mark>開始改善</mark></div></div>
<div class="rv" style="--d:800"><div class="value-old">複雜 SP，層層相依</div><div class="value-new">看清關聯，<mark>評估重構</mark></div></div>
<div class="rv" style="--d:950"><div class="value-old">沒有紀錄，難以追溯</div><div class="value-new">納入版本，<mark>保留退路</mark></div></div>
</div>

<!--
工時依團隊提供的產業經驗評估：同一 SP 調整任務，人工投入由 40 小時降為 2 小時，節省 38 小時，減少 (40-2)/40 = 95%。這是團隊經驗估算，不是本次 hackathon 實測；人工工時不等於包含資料庫等待的總完成時間。效能引用 Jim Evans 在 WideWorldImporters 範例資料庫的公開調校案例：訂單編號欄位為 nvarchar，查詢原先傳入整數。新增索引後仍需將參數改成匹配的字串型別；Invoices 表 logical reads 從 28,281 降為 20，降低約 99.93%，另有 Orders 表 692 reads。此為該表的讀取量，不是整支 SP 總量，也不是我們產品的實測成果或正式零售客戶案例。故事：每次查一張訂單，舊 SQL 都可能多讀不必要的資料；索引存在仍不保證用得好。Agent 提出改寫，驗證程式檢查結果與讀取量，再讓開發者確認。索引新增屬案例條件，不代表產品已能自動新增索引。來源：https://www.mssqltips.com/sqlservertip/6157/performance-tuning-sql-server-query-without-execution-plan/ 。三項能力對應前面的痛點：相依追蹤與驗證證據讓陳年 SP 有機會被評估及修改；複雜 SP 可先釐清相依、評估逐步重構，不能宣稱已自動完成任意 SP 重構；版本控制從開始納管後保存 diff、套用及還原紀錄，無法補回從未保存的歷史。
-->

---
eyebrow: 09 · CODEX · 應用深度
---

# 從想法到上台，Codex 全程參與。

<div class="codex-with-history">
<div class="codex-depth">
<div class="codex-depth-row rv" style="--d:200"><div class="codex-depth-title"><mark>一起想</mark></div><div class="codex-depth-work">討論與設計</div><div class="codex-depth-detail">釐清需求，推敲解法</div></div>
<div class="codex-depth-row rv" style="--d:450"><div class="codex-depth-title"><mark>一起做</mark></div><div class="codex-depth-work">Agent ＋ SSMS Extension</div><div class="codex-depth-detail">把設計落成產品</div></div>
<div class="codex-depth-row rv" style="--d:700"><div class="codex-depth-title"><mark>一起呈現</mark></div><div class="codex-depth-work">Codex ＋ Slidev</div><div class="codex-depth-detail">文案、版面、互動，逐頁迭代</div></div>
<div class="codex-depth-close rv" style="--d:1100">這份簡報，也由 Codex 協作完成。</div>
</div>
<aside class="codex-history rv" style="--d:850">
<div class="history-label">本次 hackathon 合計使用</div>
<div class="history-total"><small>≈</small><b>1.83</b><span>億</span></div>
<div class="history-token-unit">tokens</div>
<CodexActivity />

</aside>
</div>


<!--
這頁回答評審對 Codex 應用深度的問題，說明本次 hackathon 的開發過程。團隊使用 Codex 討論問題、梳理需求與設計方案；Agent 與 SSMS extension 的開發也使用 Codex。Pitch deck 則以 Codex 搭配 Slidev 製作，透過逐頁討論、瀏覽器標註與預覽反覆調整文案、元件、互動與版面。重點是 Codex 參與從討論與設計、產品實作到成果表達的多個階段，團隊負責提供產業脈絡與做決策。本 task 本機 token_count 紀錄截至 2026-09-12 14:21（台北）累計 42,562,315 tokens：input 42,498,882（其中 cached input 41,693,184）、output 63,433。快取輸入包含在 input 中，不另行加總；多輪上下文重讀會累加，並非獨立內容量、計費金額或團隊總用量。未量測程式碼生成占比與節省時間。團隊提供目前開發用量 117,000,000 tokens，完成時開發用量以 1.2 倍估算為 140,400,000，再加此簡報 task 的固定時間點 42,562,315，共 182,962,315（約 1.83 億）。目前兩者合計 159,562,315（約 1.60 億）。此計算依開發用量不含本簡報 task 的前提；開發數字由使用者提供，未另行稽核，簡報含快取輸入。本頁談開發工具的使用，不混同產品運行時的模型分工。
-->

---
eyebrow: "10 · TODAY & NEXT · 收尾"
---

<div class="final-spread">
<div class="final-statement rv" style="--d:150"><h1>沒人敢碰的 SQL，<br>從此<mark>有人接手。</mark></h1><div class="final-values">發現 · 修改 · 驗證 · 可回溯</div><div class="final-human">Agent 推進流程，人確認套用。</div></div>
<div class="final-roadmap">
<section class="rv" style="--d:400"><h2>今天</h2><div class="final-roadmap-label">已實作的原型</div><p>掃描與呼叫追蹤<br>AI 改寫與驗證<br>版本紀錄與還原</p></section>
<section class="rv" style="--d:650"><h2>下一步</h2><p>Codex MCP 整合<br>批次與排程驗收<br>更多 SQL Server 環境</p></section>
</div>
</div>

<!--
收尾回到核心價值：Agent 串起發現問題、提出修改、執行驗證與保留可回溯紀錄；驗證由規則程式執行，套用仍由人確認。今天完成的是開發原型：SP 掃描與呼叫追蹤、AI 改寫與 Snapshot 驗證、版本及套用歷史。依 repository 的 README 與 WORK-IN-PROGRESS，目前為 5.0.0.4 驗收版，不能等同完成正式發佈；批次 AI 真實證據、完整 SSMS UI、治理排程、Standard 與全新機安裝等驗收仍有待完成。今天時間有限，後续希望完成 Codex MCP 與 SQL Performance Agent 整合，補齊批次／排程端對端驗收，以及更多 SQL Server 環境驗證。已有 LocalService 實作，不再把整個獨立服務寫成尚未開發。最後：沒人敢碰的 SQL，從此有人接手。謝謝。
-->

---
centered: true
pagenum: false
---

<div class="qa-page">
<div class="qa-title">Q<span>&</span>A</div>
<div class="qa-rule"></div>
<div class="qa-team">肥宅快樂水</div>
</div>

<!--
進入問答環節，停留在此頁回答評審問題。
-->
