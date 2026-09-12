<script setup lang="ts">
// S8 前後長條圖。純 inline SVG，不用圖表函式庫。兩柱皆黑，數值標在柱頂。
// 進頁後（delay ms）柱子淡入、數值同步滾動（Dia：改變顏色與內容，不改變位置）；PDF 匯出 / 非現場直接畫最終狀態。
import { computed, onUnmounted, ref, watch } from 'vue'
import { useIsSlideActive } from '@slidev/client'
import { useLive } from '../composables/useLive'

const props = withDefaults(defineProps<{
  before: number
  after: number
  beforeLabel?: string
  afterLabel?: string
  delay?: number
  duration?: number
}>(), {
  beforeLabel: '改寫前',
  afterLabel: '改寫後',
  delay: 600,
  duration: 1100,
})

const W = 320
const H = 196
const base = 158     // 基線 y
const top = 34       // 最高柱頂 y
const barW = 76

const live = useLive()
const active = useIsSlideActive()
const progress = ref(1)
let raf = 0
let timer: ReturnType<typeof setTimeout> | undefined

function cancel() { cancelAnimationFrame(raf); if (timer) clearTimeout(timer) }
function run() {
  cancel()
  if (!live.value) { progress.value = 1; return }
  progress.value = 0
  timer = setTimeout(() => {
    const start = performance.now()
    const step = (now: number) => {
      const t = Math.min(1, (now - start) / props.duration)
      progress.value = 1 - Math.pow(1 - t, 3)   // easeOutCubic
      if (t < 1) raf = requestAnimationFrame(step)
    }
    raf = requestAnimationFrame(step)
  }, props.delay)
}
watch(active, (a) => { if (a) run(); else { cancel(); progress.value = 1 } }, { immediate: true })
onUnmounted(cancel)

const max = computed(() => Math.max(props.before, props.after, 1))
const h = (v: number) => Math.max(3, (v / max.value) * (base - top))
const fmt = (v: number) => Math.round(v * progress.value).toLocaleString('en-US')
const bars = computed(() => [
  { x: 52, v: props.before, label: props.beforeLabel },
  { x: 192, v: props.after, label: props.afterLabel },
])
</script>

<template>
  <div class="barpair">
    <svg :viewBox="`0 0 ${W} ${H}`" role="img" :aria-label="`${beforeLabel} ${before}，${afterLabel} ${after}`">
      <line x1="20" :x2="W - 20" :y1="base" :y2="base" stroke="var(--line)" stroke-width="1.5" />
      <g v-for="b in bars" :key="b.label">
        <rect :x="b.x" :y="base - h(b.v)" :width="barW" :height="h(b.v)" rx="4" fill="var(--ink)" :opacity="progress" />
        <text :x="b.x + barW / 2" :y="base - h(b.v) - 9" text-anchor="middle" class="bar-value">{{ fmt(b.v) }}</text>
        <text :x="b.x + barW / 2" :y="base + 22" text-anchor="middle" class="bar-label">{{ b.label }}</text>
      </g>
    </svg>
  </div>
</template>
