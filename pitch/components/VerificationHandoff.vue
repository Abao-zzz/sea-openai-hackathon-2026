<template>
  <div class="verification-spread">
    <section class="verification-half rv" style="--d:200">
      <div class="verification-role">Rule-based code</div>
      <h2>跑出<mark>驗證結果</mark></h2>
      <div class="verification-report">
        <div class="verification-report-heading">驗證報告</div>
        <div class="verification-report-row"><span>AST 比對</span><span>結構差異</span></div>
        <div class="verification-report-row"><span>結果比對</span><span>資料差異</span></div>
        <div class="verification-report-row"><span>效能量測</span><span>Reads · 耗時</span></div>
        <div class="verification-report-footer">輸出判定與依據</div>
      </div>
    </section>
    <div class="verification-bridge rv" style="--d:500" aria-label="將驗證報告交給 Agent">→</div>
    <section class="verification-half verification-reader rv" style="--d:750">
      <div class="verification-role">Agent</div>
      <h2>說清<mark>原因與建議</mark></h2>
      <div class="verification-reading">
        <div class="verification-reading-row"><span>判定</span><strong>為什麼通過或退回？</strong></div>
        <div class="verification-reading-row"><span>依據</span><strong>哪些結果支持判定？</strong></div>
        <div class="verification-reading-row"><span>建議</span><strong>下一步可以怎麼做？</strong></div>
      </div>
    </section>
  </div>
</template>
