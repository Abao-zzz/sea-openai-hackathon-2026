<script setup lang="ts">
// 眉標。step：章節號（由 layout 解析），> 0 時在右端畫光譜 running rule；
// 揭露量由 .pitch 根上的 --step / --steps 驅動（見 style.css「Running rule」）。
defineProps<{ step?: number }>()
</script>

<template>
  <div class="eyebrow">
    <span class="eyebrow-text"><slot /></span>
    <span v-if="step" class="rule running" aria-hidden="true" />
  </div>
</template>
