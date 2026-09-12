<script setup lang="ts">
// 藥丸標籤。tone: line（預設，白底細框）| red | green | ink
defineProps<{ tone?: 'line' | 'red' | 'green' | 'ink' }>()
</script>

<template>
  <span class="chip" :class="tone ?? 'line'"><slot /></span>
</template>
