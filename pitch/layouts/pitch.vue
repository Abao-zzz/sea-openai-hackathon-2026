<script setup lang="ts">
// 每頁固定結構：眉標（左上）→ 標題 → 內容 → 結語（.close，貼底）→ 頁碼（右下）。
// eyebrow / pagenum / centered 都從該頁 frontmatter 讀。
// pagenum 從 frontmatter 物件讀而不是宣告成 boolean prop：Vue 會把缺席的 boolean prop 轉成 false。
//
// 進頁節奏（entrance）：root 上掛三種 mode class
//   anim    現場放映且此頁為當前頁 → .rv 元素依 --d（ms）延遲上浮
//   pending 現場放映但不是當前頁   → .rv 先隱藏，切到此頁時 mode 變 anim、動畫重播
//   static  PDF 匯出 / 總覽 / 下一頁預覽 → 全部直接顯示最終狀態，無動畫
import { computed } from 'vue'
import { useIsSlideActive, useSlideContext } from '@slidev/client'
import { useLive } from '../composables/useLive'

const props = defineProps<{
  eyebrow?: string
  centered?: boolean
  frontmatter?: Record<string, any>
}>()

const { $page } = useSlideContext()
const active = useIsSlideActive()
const live = useLive()

const mode = computed(() => {
  if (!live.value) return 'static'
  return active.value ? 'anim' : 'pending'
})

// 章節號：從眉標前導數字取（'08 · TODAY & NEXT · 收尾' → 8；S1 無眉標 → 0 → 不畫 running rule）。
// 寫成 .pitch 根上的 --step / --steps，眉標右端的光譜 running rule 依此揭露 n/8。
const step = computed(() => Number.parseInt(props.eyebrow ?? '', 10) || 0)
</script>

<template>
  <div class="slidev-layout pitch" :class="[mode, { centered: props.centered }]" :style="{ '--step': step, '--steps': 10 }">
    <Eyebrow v-if="props.eyebrow" :step="step">{{ props.eyebrow }}</Eyebrow>
    <slot />
    <div v-if="props.frontmatter?.pagenum !== false" class="pagenum">{{ $page }}</div>
  </div>
</template>
