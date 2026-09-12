# SQL Performance Agent · Pitch Deck

肥宅快樂水參加 **2026 Sea x OpenAI Regional Codex Hackathon TW** 的簡報，以 Slidev 製作，共 12 頁。採繁體中文、Dia 風格排版，支援講者模式、Demo 影片及螢光筆進場動畫。

## 啟動簡報

需要 Node.js 與 npm。首次使用，在 repository 根目錄執行：

```sh
cd pitch
npm run setup
npm run dev
```

`setup` 會安裝依賴並複製本機字型。之後只需在 `pitch/` 執行 `npm run dev`。

- [放映模式](http://localhost:3030/1)
- [講者模式](http://localhost:3030/presenter/1)
- [Demo 頁](http://localhost:3030/6)

以方向鍵換頁，按 `P` 開啟講者模式、`O` 查看總覽。HL 在進入各頁時由左往右畫出，完成後保留；第五頁四個 HL 同時畫出，不需額外點擊。系統啟用減少動態效果時，HL 直接顯示。

## 簡報結構

| 頁碼 | 內容 |
| --- | --- |
| 1 | 封面與產品介紹 |
| 2 | 企業既有 SP 的痛點 |
| 3 | 開發者原本的排查與優化流程 |
| 4 | AI 改寫可能改變結果或沒有改善效能 |
| 5 | 掃描、呼叫追蹤、優化驗證、版本控制 |
| 6 | Demo 影片 |
| 7 | 驗證方式 |
| 8 | 規則程式驗證、Agent 解釋與建議 |
| 9 | 應用價值 |
| 10 | Codex 在本次開發中的應用深度 |
| 11 | 已實作原型與後續方向 |
| 12 | Q&A |

## 放入或替換 Demo 影片

將錄影命名為 `demo.mp4`，放到：

```text
pitch/public/video/demo.mp4
```

第 6 頁會讀取此檔案。點中央播放鈕開始播放，之後顯示原生影片控制列；離開該頁會自動暫停。影片以完整比例呈現，不裁切畫面。

替換後重新整理頁面；若仍顯示舊內容，或是首次加入檔案，重新啟動 `npm run dev`。使用瀏覽器可播放的 MP4 編碼。

影片檔已被 Git 忽略，**clone repository 不會取得影片**，換電腦展示前需另行複製。PDF 不包含可播放影片，Demo 頁會呈現靜態預留畫面。

## 修改內容與講稿

| 檔案／目錄 | 用途 |
| --- | --- |
| `slides.md` | 投影片內容；各頁 HTML 註解是講者模式使用的 notes |
| `NOTES.md` | 獨立閱讀的講稿，修改 notes 時需手動同步 |
| `style.css` | 全域排版、配色與 HL 動畫 |
| `layouts/pitch.vue` | 頁面框架、頁碼、進場與靜態模式 |
| `components/` | 流程、驗證、Demo 等元件 |
| `public/` | 圖片、字型與影片資產 |
| `references/dia/` | Dia 設計參考與色彩、字體規格 |

目前視覺風格已定案，內容更新沿用既有排版與 HL 配色。講者模式使用 `slides.md` 的 notes，不會自動讀取 `NOTES.md`。

產品功能與完成範圍請對照根目錄的 [README](../README.md)、[WORK-IN-PROGRESS](../WORK-IN-PROGRESS.md) 及實作。簡報講稿保留了效益數字、引用案例及 token 用量的來源與計算前提；更新數字時也需同步更新其依據。驗證結論限於既定測試資料與參數，正式套用仍需人工確認。

## 匯出

在 `pitch/` 執行：

```sh
# 靜態 PDF
npm run export

# 講稿 PDF
npm run export:notes

# 可互動的網站版本
npm run build
```

- 簡報 PDF：`pitch-deck.pdf`
- 講稿 PDF：`pitch-notes.pdf`
- 網站輸出：`dist/`

PDF 與總覽直接呈現完整 HL，不播放進場動畫。PDF 匯出需能啟動本機伺服器與 Chromium。`dist/` 需透過 HTTP server 提供，不能直接雙擊 HTML；建置前先放入要展示的影片。

內容修改後需重新匯出，既有 PDF 不會隨開發預覽自動更新。
