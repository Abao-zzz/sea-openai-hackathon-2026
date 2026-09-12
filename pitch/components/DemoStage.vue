<script setup lang="ts">
import { ref, watch } from 'vue'
import { useIsSlideActive } from '@slidev/client'
import { useLive } from '../composables/useLive'
const live = useLive()
const active = useIsSlideActive()
const video = ref<HTMLVideoElement | null>(null)
const failed = ref(false)
const started = ref(false)
async function play() {
  if (!video.value) return
  try { await video.value.play(); started.value = true } catch { started.value = false }
}
// No request for a missing recording. Add the file and restart the preview.
const recordings = import.meta.glob('/public/video/demo.mp4', { query: '?url', import: 'default', eager: true })
const hasVideo = Object.keys(recordings).length > 0
watch(active, value => { if (!value) video.value?.pause() })
</script>

<template>
  <div class="demo-stage">
    <div class="demo-window">
      <video v-if="hasVideo && live && !failed" ref="video" :controls="started" playsinline preload="metadata" aria-label="Demo 影片" @play="started = true" @error="failed = true">
        <source :src="'/video/demo.mp4'" type="video/mp4">
      </video>
      <div v-else class="demo-placeholder" />
      <button v-if="!started || failed" class="demo-play" :disabled="!hasVideo || !live || failed" :aria-label="hasVideo ? '播放 Demo 影片' : 'Demo 影片尚未加入'" @click.stop="play">
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M8 5v14l11-7z" fill="currentColor" /></svg>
      </button>
    </div>
  </div>
</template>
