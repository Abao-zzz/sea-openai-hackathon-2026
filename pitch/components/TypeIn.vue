<script setup lang="ts">
// 打字效果。進頁後（delay ms）逐字打出 text，打完游標消失；離開頁面重置，再進來重播。
// 用「隱形完整字串 + 絕對定位的已打字串」讓版面寬度從一開始就固定，箭頭定位才不會漂。
// print mode（PDF 匯出）直接顯示完整字串。
import { onUnmounted, ref, watch } from 'vue'
import { useIsSlideActive } from '@slidev/client'
import { useLive } from '../composables/useLive'

const props = withDefaults(defineProps<{
  text: string
  speed?: number   // ms / 字
  delay?: number   // ms
}>(), { speed: 38, delay: 500 })

const live = useLive()
const active = useIsSlideActive()
const shown = ref(props.text)
const typing = ref(false)
let timer: ReturnType<typeof setTimeout> | undefined

function cancel() { if (timer) clearTimeout(timer); typing.value = false }

function run() {
  cancel()
  if (!live.value) { shown.value = props.text; return }
  shown.value = ''
  typing.value = true
  let i = 0
  const tick = () => {
    i++
    shown.value = props.text.slice(0, i)
    if (i < props.text.length) timer = setTimeout(tick, props.speed)
    else timer = setTimeout(() => { typing.value = false }, 900)
  }
  timer = setTimeout(tick, props.delay)
}

watch(active, (a) => { if (a) run(); else { cancel(); shown.value = props.text } }, { immediate: true })
onUnmounted(cancel)
</script>

<template>
  <span class="typein">
    <span class="typein-ghost" aria-hidden="true">{{ text }}</span>
    <span class="typein-live">{{ shown }}<span v-if="typing" class="caret" /></span>
  </span>
</template>
