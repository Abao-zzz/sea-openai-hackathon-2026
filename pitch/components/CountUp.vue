<script setup lang="ts">
// 動態滾動數字。進入該頁時從 from 滾到 to（easeOutExpo），離開後重置，下次再進來會重播。
// 匯出 PDF（print mode）或非現場情境直接顯示最終值，保證 PDF = 動畫最終狀態。
import { onUnmounted, ref, watch } from 'vue'
import { useIsSlideActive } from '@slidev/client'
import { useLive } from '../composables/useLive'

const props = withDefaults(defineProps<{
  to: number
  from?: number
  duration?: number   // ms
  delay?: number      // ms，多張卡錯開用
  prefix?: string
  suffix?: string
  decimals?: number
}>(), { from: 0, duration: 1400, delay: 0, prefix: '', suffix: '', decimals: 0 })

const live = useLive()
const active = useIsSlideActive()
const value = ref(props.to)

let raf = 0
let timer: ReturnType<typeof setTimeout> | undefined

function cancel() {
  cancelAnimationFrame(raf)
  if (timer) clearTimeout(timer)
}

function run() {
  cancel()
  if (!live.value) { value.value = props.to; return }
  value.value = props.from
  timer = setTimeout(() => {
    const start = performance.now()
    const step = (now: number) => {
      const t = Math.min(1, (now - start) / props.duration)
      const eased = t >= 1 ? 1 : 1 - Math.pow(2, -10 * t)
      value.value = props.from + (props.to - props.from) * eased
      if (t < 1) raf = requestAnimationFrame(step)
      else value.value = props.to
    }
    raf = requestAnimationFrame(step)
  }, props.delay)
}

watch(active, (a) => { if (a) run(); else { cancel(); value.value = props.to } }, { immediate: true })
onUnmounted(cancel)

const fmt = (v: number) => v.toLocaleString('en-US', {
  minimumFractionDigits: props.decimals,
  maximumFractionDigits: props.decimals,
})
</script>

<template>
  <span class="countup">{{ prefix }}{{ fmt(value) }}{{ suffix }}</span>
</template>
