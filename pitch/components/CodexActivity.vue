<script setup lang="ts">
const levels = [3, 3, 2, 4, 4, 3, 1, 1, 1, 1, 3, 3, 1, 0, 1, 1, 2, 0, 2, 0, 1, 1, 1, 2, 1, 0, 1, 2, 1, 1, 1, 1, 1, 2, 2, 0, 0, 1, 0, 2, 1, 0, 1, 1, 2, 1, 1, 1, 1, 1, 1, 1, 0, 1, 2, 1, 0, 2, 1, 0, 1, 2, 2, 0, 0, 1, 1, 0, 1, 2]
</script>
<template>
<div class="codex-activity" role="img" aria-label="本次簡報 task 的互動密度，依時間分段"><span v-for="(level, i) in levels" :key="i" :class="`activity-level-${level}`" /></div>
</template>
