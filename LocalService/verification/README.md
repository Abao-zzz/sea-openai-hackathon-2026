# 額外驗證工具

從 LocalService 專案根目錄執行。先啟動 publish（本機 port 46217），並使用隔離 SSMS_AI_SQL_DATA_DIR；verify-ui 會用 SELECT 1 AS value 呼叫 analyze，建立真實本機規則歷史紀錄。請以無 OpenAI key 的測試程序執行。

```powershell
npm install --prefix verification
npx --prefix verification playwright install chromium
node verification/verify-ui.cjs
python -m pip install -r verification/requirements.txt
python verification/verify-openapi.py
python verification/verify-startup.py
```

verify-startup 自行啟動／停止 port 46218 的程序，使用合成測試 key，從不呼叫 OpenAI。它驗證缺檔、key health、console 隱私與 port 衝突。

verify-ui 產生 screenshot output 與 layout report；沒有 golden baseline 也不會把這些圖自動核准為 baseline。Windows DPI 未由工具變更。
