<script setup lang="ts">
// S7 的一列。sign: eq（＝ 綠）| ne（≠ 紅），符號用 SVG 線條畫，v-click 出現時 draw-on。
// copilot / auto: 該模式是否會跑到這一層（✓ 或 —）。
defineProps<{
  name: string
  sub: string
  desc: string
  sign: 'eq' | 'ne'
  note: string
  copilot?: boolean
  auto?: boolean
}>()
</script>

<template>
  <div class="tier-grid tier-row">
    <div>
      <div class="tier-name">{{ name }}</div>
      <div class="tier-sub">{{ sub }}</div>
    </div>
    <div class="tier-desc">{{ desc }}</div>
    <div class="tier-sign" :class="sign">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" aria-hidden="true">
        <path d="M5 9.5h14M5 14.5h14" pathLength="1" />
        <path v-if="sign === 'ne'" d="M15 4.5 9 19.5" pathLength="1" class="slash" />
      </svg>
    </div>
    <div class="tier-note">{{ note }}</div>
    <div v-for="(on, i) in [copilot, auto]" :key="i" class="tier-flag">
      <svg v-if="on" class="on" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-label="✓">
        <path d="M5 12.5l4.5 4.5L19 7.5" pathLength="1" />
      </svg>
      <svg v-else class="off" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-label="—">
        <path d="M7 12h10" pathLength="1" />
      </svg>
    </div>
  </div>
</template>
