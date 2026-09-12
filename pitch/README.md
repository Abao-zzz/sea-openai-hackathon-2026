# 肥宅快樂水：SP 優化 Agent 簡報

以 handoff 的九頁 Slidev 快照為基礎。保留 Dia 視覺、繁體中文內容、進場動畫與講者模式。

## 開啟

```sh
cd pitch
npm run setup
npm run dev
```

放映：http://localhost:3030 。方向鍵換頁，按 P 進講者模式，O 看總覽。

- `slides.md`：九頁內容與 speaker notes。
- `style.css`：設計與排版。
- `NOTES.md`：可獨立閱讀的講稿。

## 匯出

```sh
npm run build
npm run export
```

PDF 產生在 `pitch-deck.pdf`。HTML 建置在 `dist/`，需以 HTTP server 提供，不能直接雙擊 index.html。

## Demo

目前未提供影片，第 5 頁顯示可切換六步的演示流程。放入 `public/video/demo.mp4` 並重啟開發伺服器，即顯示影片。章節時間位於 `components/DemoStage.vue`。

## 下一輪需補的證據

- 當天 SSMS 演示錄影與章節時間。
- 實跑結果取代目前清楚標記的 handoff 測試案例值。
- Codex 開發的可核實案例或統計。第一版不使用未記錄的 task 數與 review 數。

產品行為依交接包描述，這個 repository 尚無產品原始碼可供交叉驗證。驗證一致性只涵蓋當次資料與參數，正式 SP 於人工確認套用後才修改。
