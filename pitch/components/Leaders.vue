<script setup lang="ts">
// 註解引線：從 from 元素右緣拉一條折線（→ 中線 ↕ → ）到 to 元素左緣，兩端各一個小圓點。
// 覆蓋在父容器（需 position: relative）上，座標用 offsetTop/offsetLeft 累加（排版座標，不受 transform 影響）。
// 可搭配 v-click / v-after：CSS 在 .slidev-vclick-hidden 移除時才畫線。
// 顏色中性（--soft），語意色留給右邊的說明文字。draw-on 由 CSS 依 .pitch 的 anim / static 決定。
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useIsSlideActive } from '@slidev/client'

const props = defineProps<{
  pairs: { from: string, to: string }[]   // CSS selector，相對於父容器
}>()

const svg = ref<SVGSVGElement | null>(null)
const size = ref({ w: 0, h: 0 })
const lines = ref<{ d: string, x1: number, y1: number, x2: number, y2: number }[]>([])
const active = useIsSlideActive()

// 用 offsetLeft / offsetTop 沿 offsetParent 鏈累加到父容器：這是排版座標，
// 不受進場動畫的 transform（上浮位移）和 Slidev 的整頁縮放影響，量出來才不會偏。
function pos(el: HTMLElement, root: HTMLElement) {
  let x = 0, y = 0
  let node: HTMLElement | null = el
  while (node && node !== root) {
    x += node.offsetLeft
    y += node.offsetTop
    node = node.offsetParent as HTMLElement | null
  }
  return { x, y, w: el.offsetWidth, h: el.offsetHeight }
}

function compute() {
  const el = svg.value
  const root = el?.parentElement
  if (!root || !root.offsetWidth) return
  size.value = { w: root.offsetWidth, h: root.offsetHeight }
  const f = (n: number) => n.toFixed(1)
  lines.value = props.pairs.flatMap(({ from, to }) => {
    const a = root.querySelector<HTMLElement>(from)
    const b = root.querySelector<HTMLElement>(to)
    if (!a || !b) return []
    const ra = pos(a, root)
    const rb = pos(b, root)
    const x1 = ra.x + ra.w + 6, y1 = ra.y + ra.h / 2
    const x2 = rb.x - 8, y2 = rb.y + rb.h / 2
    const xm = x1 + (x2 - x1) * 0.5
    return [{ d: `M${f(x1)} ${f(y1)} H${f(xm)} V${f(y2)} H${f(x2)}`, x1, y1, x2, y2 }]
  })
}

let ro: ResizeObserver | undefined
onMounted(() => {
  compute()
  document.fonts?.ready.then(compute)
  ro = new ResizeObserver(compute)
  if (svg.value?.parentElement) ro.observe(svg.value.parentElement)
  window.addEventListener('resize', compute)
})
onBeforeUnmount(() => { ro?.disconnect(); window.removeEventListener('resize', compute) })
watch(active, (a) => { if (a) requestAnimationFrame(compute) })
</script>

<template>
  <svg
    ref="svg"
    class="leaders"
    data-waitfor="g:first-of-type > path"
    :viewBox="`0 0 ${size.w} ${size.h}`"
    :width="size.w"
    :height="size.h"
    fill="none"
    stroke="var(--soft)"
    stroke-width="1.5"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
  >
    <g v-for="(l, i) in lines" :key="i" :style="{ '--i': i }">
      <path :d="l.d" pathLength="1" />
      <circle :cx="l.x1" :cy="l.y1" r="3" fill="var(--soft)" stroke="none" />
      <circle :cx="l.x2" :cy="l.y2" r="3" fill="var(--soft)" stroke="none" />
    </g>
  </svg>
</template>
