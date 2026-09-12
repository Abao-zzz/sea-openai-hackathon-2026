<script setup lang="ts">
// demo 影片的章節藥丸。點一顆就把同一頁的 <video> 跳到該秒數並播放。
// chapters: [{ label, t（秒）, tone?: 'red' | 'green' | 'ink' }]
// 影片播放時，目前所在章節的藥丸會變黑（顏色變化，不是動作）。
import { getCurrentInstance, onBeforeUnmount, onMounted, ref } from 'vue'

const props = defineProps<{
  chapters: { label: string; t: number; tone?: 'red' | 'green' | 'ink' }[]
}>()

const current = ref(-1)
let video: HTMLVideoElement | null = null
const inst = getCurrentInstance()

function findVideo() {
  const root = (inst?.proxy?.$el as HTMLElement | null)?.closest('.slidev-layout')
  return root?.querySelector('video') ?? null
}

function onTime() {
  if (!video) return
  const t = video.currentTime
  let idx = -1
  props.chapters.forEach((c, i) => { if (t >= c.t) idx = i })
  current.value = idx
}

function seek(i: number) {
  video ??= findVideo()
  if (!video) return
  video.currentTime = props.chapters[i].t
  video.play().catch(() => {})
}

onMounted(() => {
  video = findVideo()
  video?.addEventListener('timeupdate', onTime)
})
onBeforeUnmount(() => video?.removeEventListener('timeupdate', onTime))
</script>

<template>
  <div class="chapters">
    <template v-for="(c, i) in chapters" :key="i">
      <FlowArrow v-if="i > 0" />
      <button
        type="button"
        class="chip chapter"
        :class="[c.tone ?? 'line', { on: i === current }]"
        @click="seek(i)"
      >{{ c.label }}</button>
    </template>
  </div>
</template>
