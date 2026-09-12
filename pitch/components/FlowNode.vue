<script setup lang="ts">
// 流程節點。放在 .flow 容器裡橫排；第一個節點的箭頭由 CSS 隱藏。
// dark: 黑底白字（強調節點）。
defineProps<{ dark?: boolean }>()
</script>

<template>
  <div class="flow-node" :class="{ dark }">
    <FlowArrow />
    <div class="flow-box"><slot /></div>
  </div>
</template>
