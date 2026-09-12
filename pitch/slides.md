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

# 先證明，再動手。

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
eyebrow: 06 · HOW IT VERIFIES · 它為什麼說不
---

# 它退回了一支「看起來更好」的改寫。

<div class="lead rv" style="--d:150">同一段改寫，資料庫條件不同，判決就不同。</div>

<div class="kicker mt-5 rv" style="--d:1500">六關安全預檢</div>
<div class="chips mt-2">
<Chip class="rv" style="--d:1600">兩版皆唯讀</Chip>
<Chip class="rv" style="--d:1700">有實質變更</Chip>
<Chip class="rv" style="--d:1800">可編譯</Chip>
<Chip class="rv" style="--d:1900">輸出欄位契約一致</Chip>
<Chip class="rv" style="--d:2000">plan warning 消失</Chip>
<Chip tone="red" class="rv flash" style="--d:2100">estimated cost 改善 ≥ 5%</Chip>
</div>

<div class="kicker mt-4 rv" style="--d:2600">同一個改寫，四種索引，兩過兩不過</div>
<div class="matrix mt-2">
<div class="mrow rv" style="--d:2750"><span>CreatedAt 沒有索引</span><span class="mono">49 → 49</span><Chip tone="red">不採用</Chip></div>
<div class="mrow rv" style="--d:2900"><span>只在 included columns</span><span class="mono">45 → 45</span><Chip tone="red">不採用</Chip></div>
<div class="mrow rv" style="--d:3050"><span>單欄 index key</span><span class="mono">32 → 3</span><Chip tone="green">採用</Chip></div>
<div class="mrow rv" style="--d:3200"><span>複合索引 leading key</span><span class="mono">45 → 3</span><Chip tone="green">採用</Chip></div>
</div>

<div class="close sm rv" style="--d:3800">判決裡沒有 LLM。</div>
<div class="placeholder-note">資料來源：handoff 測試案例記錄。logical reads 為測試值，非現場結果。</div>

<!--
六關預檢不執行候選：檢查唯讀、實質變更、可編譯、輸出契約、plan warning 與 estimated cost。成本估計改善不能取代實跑。下方是 handoff 的索引測試案例，數值為 logical reads。哪一關退回必須以該次結果為準，不由 reads 反推預檢失敗原因。
-->

---
eyebrow: 07 · WHY IT CAN BE TRUSTED · 通過的怎麼通過
---

# 通過的那一支，是怎麼通過的。

<div class="grid-2 cols-3-2">
<div class="stack">
<div class="card rv" style="--d:200">
<div class="card-title">結果一致，要在 Snapshot 實跑</div>
<div class="card-body">在指定資料與參數上，比欄位、列數與結果 hash。候選跑三輪，任一組不一致就退回。</div>
</div>
<div class="card rv" style="--d:500">
<div class="card-title">留下來，像一次 commit</div>
<div class="card-body">先變成版本，再套用。有 diff、有 migration、退得回去。</div>
</div>
</div>
<div class="card rv" style="--d:800">
<div class="card-kicker">變快，是量出來的 · median logical reads</div>
<BarPair :before="82" :after="3" :delay="1200" />
<div class="tiny soft mt-2">handoff 測試案例：82 降至 3，非現場結果</div>
</div>
</div>

<div class="close sm rv" style="--d:2600">預檢只是估計。真的少讀，是 Snapshot 說的。</div>

<!--
結果一致只涵蓋當次資料與選定的參數，不能宣稱所有輸入都等價。候選跑三輪，原版一到三輪，有精確 Query Store baseline 時原版只跑一次。比較欄位簽章、列數與具型別正規化的結果 hash。82 降到 3 是 handoff 的測試案例值。Snapshot 需要建庫權限、消耗 CPU 與 IO，設有上限並在結束後清理。套用前保留版本、migration 與還原紀錄。
-->

---
eyebrow: 08 · CODEX
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
eyebrow: "08 · TODAY & NEXT · 收尾"
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
