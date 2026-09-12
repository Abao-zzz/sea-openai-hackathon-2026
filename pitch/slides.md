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

# 能用了，技術債卻留下來。

<div class="value-estimate rv" style="--d:200">團隊過往經驗</div>
<div class="value-dashboard">
<div class="value-main rv" style="--d:350"><div class="value-caption">過去 · 一次救急修正</div><div class="value-big">近 5<span style="font-size:1.3rem"> 個工作日</span></div><div class="value-history">反覆修改、重跑 → 改到能用<br>轉做新功能 → <strong>優化被擱置</strong></div></div>
<div class="value-side rv" style="--d:650"><div class="value-caption">現在 · 人工操作集中為</div><div class="value-medium value-step-count">3 <span>個步驟</span></div><div class="value-actions"><span>01　送出任務</span><span>02　檢查結果</span><span>03　<mark>確認套用</mark></span></div></div>
</div>

<!--
根據團隊過往經驗，通常在業務卡住或出問題後才修改 SP。每次修正可能接近一週工作日，本頁以近 5 個工作日表達同一個約略週期，不是精確量測，也不是五天連續人工投入。反覆修改、重新執行 SP，先做到當下能用；後續優先開發新功能，往往沒有時間繼續優化，技術債因而留下來。新流程把人工互動整理成三步：送出任務、檢查結果、確認套用；Agent 負責反覆改寫與驗證。三步是操作步驟數，不能解讀成三分鐘或保證消除全部技術債。目前沒有新流程的實測完成時間，不宣稱節省百分比。
-->

---
eyebrow: 09 · CODEX
---

# Codex 與 OpenAI 的分工

<div class="grid-2 rv" style="--d:200">
<div class="card"><div class="card-kicker">開發時</div><div class="card-title">Codex 提案，測試與 review 把關</div><div class="card-body">程式碼需要通過檢查，才能成為產品的一部分。</div></div>
<div class="card"><div class="card-kicker">產品運行時</div><div class="card-title">OpenAI 產生 SQL 候選</div><div class="card-body">平台執行預檢與 Snapshot 驗證，由人決定是否套用。</div></div>
</div>
<div class="card mt-3 rv" style="--d:1300">
<div class="card-kicker">Runtime · OpenAI 只出現在一格</div>
<div class="chips">
<Chip class="rv" style="--d:1500">掃描</Chip><FlowArrow class="rv" style="--d:1600" />
<Chip class="rv" style="--d:1700">呼叫追蹤</Chip><FlowArrow class="rv" style="--d:1800" />
<Chip tone="ink" class="rv" style="--d:1900">產生候選 · OpenAI</Chip><FlowArrow class="rv" style="--d:2000" />
<Chip class="rv" style="--d:2100">六關預檢</Chip><FlowArrow class="rv" style="--d:2200" />
<Chip class="rv" style="--d:2300">Snapshot Dry Run</Chip><FlowArrow class="rv" style="--d:2400" />
<Chip class="rv" style="--d:2500">套用</Chip>
</div>
<div class="runtime-line mt-2 rv" style="--d:2800">下一步：Codex 透過 MCP 走同一道門。沒有 <span class="mono red bold strike" style="--d:3400">apply</span>。</div>
</div>

<div class="close sm rv" style="--d:3700">我們對 Codex 的要求，和對這個 agent 一樣：它提案，測試與 review 判決。</div>


<!--
Codex 用於產品開發，程式碼經測試與 review。產品運行時，OpenAI 只負責產生 SQL 候選，掃描、地圖、Dry Run 與套用不呼叫模型。產生候選會送出唯讀 SP body 與一組參數值，plan XML 留在本機。MCP 是下一步，目前不宣稱已完成。
-->

---
eyebrow: "10 · TODAY & NEXT · 收尾"
---

# Agent 負責驗證，人決定寫回

<div class="grid-2">
<div class="card rv" style="--d:200">
<div class="card-title">它決定的</div>
<ul class="list done">
<li class="rv" style="--d:450">選哪些 SP、跳過哪些、為什麼</li>
<li class="rv" style="--d:600">用哪幾組參數證據、改成什麼</li>
<li class="rv" style="--d:750">六關過不過、Snapshot 過不過、差多少</li>
<li class="rv" style="--d:900">批次 Top N：逐支自己跑，有預算、有停止條件，通過的排進待審</li>
</ul>
</div>
<div class="card rv" style="--d:1200">
<div class="card-title muted">人決定的</div>
<ul class="list todo muted">
<li class="rv" style="--d:1450">要不要開下一道門：掃描、預檢或批次、套用</li>
<li class="rv" style="--d:1600">通過的版本要不要進、進了要不要退</li>
<li class="rv" style="--d:1750">每次寫回都需要確認，批次通過也只排進待審</li>
</ul>
</div>
</div>

<div class="kicker mt-4 rv" style="--d:2100">今天真的跑起來的</div>
<div class="chips mt-2">
<Chip class="rv" style="--d:2250">SP 呼叫追蹤</Chip>
<Chip class="rv" style="--d:2400">SP 掃描</Chip>
<Chip class="rv" style="--d:2550">SP 優化與批次驗證</Chip>
<Chip class="rv" style="--d:2700">SP 版本控制 · 進版 / 退版 / 稽核</Chip>
</div>
<div class="muted tiny mt-2 rv" style="--d:3000">下一步：獨立 worker、更多 SQL Server 版本實測、Codex MCP 入口</div>

<div class="close final">
<div class="rule rv grow" style="--d:3400"></div>
<span class="rv" style="--d:3600">沒人敢碰的 SQL，從此有人接手。</span>
</div>

<!--
Agent 決定選誰、跳過誰、用什麼參數與候選是否通過。批次驗證有預算與停止條件，通過項目進待審，不自動套用。人決定是否啟動下一階段，以及是否進版或退版。目前 SSMS 關閉後工作不繼續，獨立 worker 是下一步。沒人敢碰的 SQL，從此有人接手。謝謝。
-->
