<script setup lang="ts">
const steps = [
  { title: '掃描', text: '盤點現有 SP，找出成本最高的項目。' },
  { title: 'SP 呼叫追蹤', text: '看清上下游關聯，再選擇改善對象。' },
  { title: '優化驗證', text: 'Agent 提案並驗證，未通過就退回。' },
  { title: '版本控制', text: '人確認才套用，保留版本可還原。' },
]
</script>

<template>
  <div class="grid-2 gates solution-steps">
    <div v-for="(step, index) in steps" :key="step.title" class="card">
      <div class="gate-head"><span class="gate-no">{{ index + 1 }}</span><span class="card-title step-title step-marked" :style="{ '--step-wash': `var(--step-wash-${index + 1})` }">{{ step.title }}</span></div>
      <div class="card-body">{{ step.text }}</div>
    </div>
  </div>
</template>
