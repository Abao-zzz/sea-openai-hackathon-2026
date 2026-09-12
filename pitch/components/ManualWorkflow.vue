<template>
  <div class="manual-workflow">
    <div class="manual-steps">
      <div class="manual-step rv" style="--d:200">
        <div class="manual-visual">
          <svg viewBox="0 0 220 120" role="img" aria-label="執行計畫分支示意">
            <path d="M65 60H100M100 25V95M100 25H145M100 95H145" />
            <rect x="15" y="43" width="50" height="34" rx="6" />
            <rect x="145" y="8" width="55" height="34" rx="6" />
            <rect x="145" y="78" width="55" height="34" rx="6" />
          </svg>
        </div>
        <div class="manual-label"><span>01</span>看執行計畫</div>
      </div>
      <span class="manual-arrow" aria-hidden="true">→</span>
      <div class="manual-step rv" style="--d:500">
        <div class="manual-visual">
          <svg viewBox="0 0 220 120" role="img" aria-label="定位耗時最多的節點示意">
            <path d="M25 28H90M25 60H160M25 92H65" class="manual-bars" />
            <circle cx="144" cy="58" r="33" class="manual-lens" />
            <path d="M168 82L193 107" class="manual-handle" />
          </svg>
        </div>
        <div class="manual-label"><span>02</span>找出瓶頸</div>
      </div>
      <span class="manual-arrow" aria-hidden="true">→</span>
      <div class="manual-step rv" style="--d:800">
        <div class="manual-visual manual-code" aria-label="SQL 改寫示意">
          <span>SELECT …</span>
          <span class="manual-old">WHERE f(x) = …</span>
          <span class="manual-new">WHERE x ≥ …</span>
        </div>
        <div class="manual-label"><span>03</span>改寫 SQL</div>
      </div>
    </div>
    <div class="manual-loop rv" style="--d:1200">
      <svg viewBox="0 0 800 70" preserveAspectRatio="none" aria-hidden="true"><path d="M675 0V35Q675 50 660 50H140Q125 50 125 35V5M117 15L125 5L133 15" /></svg>
      <span>改完，<mark>再跑一次。</mark></span>
    </div>
  </div>
</template>
