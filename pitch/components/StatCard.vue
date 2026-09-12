<script setup lang="ts">
// 數字卡。大字用 value prop，或用 <template #value> 放動態內容（CountUp、擺動符號）；
// 預設 slot = 小字說明。
// tone: ink（預設）| muted | green | red — 只影響大字顏色。
// bare: 無卡片底色（S6 / S9 的橫排數字）。small: 大字 28px（S9）。
defineProps<{
  value?: string
  tone?: 'ink' | 'muted' | 'green' | 'red'
  bare?: boolean
  small?: boolean
}>()
</script>

<template>
  <div class="stat" :class="[tone ?? 'ink', { bare, small }]">
    <div class="stat-value"><slot name="value">{{ value }}</slot></div>
    <div class="stat-label"><slot /></div>
  </div>
</template>
