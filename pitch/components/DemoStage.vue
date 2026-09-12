<script setup lang="ts">
import { computed, ref } from 'vue'
import { useLive } from '../composables/useLive'
const live = useLive()
const video = ref<HTMLVideoElement | null>(null)
const selected = ref(0)
const chapters = [
  { label: '掃描', t: 0, title: '盤點現有 SP，找出高成本項目', detail: '讀取 catalog、DMV 與 Query Store，保留每一筆跳過理由。' },
  { label: 'SP 呼叫追蹤', t: 15, title: '先看這支 SP 會牽動誰', detail: '追蹤上游與下游呼叫。無法解析的 dynamic SQL 會標明未解析。' },
  { label: '退回一支', t: 30, title: '改寫看似合理，驗證仍可能退回', detail: 'CreatedAt 沒索引的測試案例，logical reads 為 49 與 49。', tone: 'red' },
  { label: '放行一支', t: 50, title: '結果一致，而且實測讀取量下降', detail: '檢查欄位、列數與結果 hash，再比較 median logical reads。', tone: 'green' },
  { label: '進版', t: 75, title: '先建立版本，再由人確認套用', detail: '保留 diff 與 up/down migration，套用前檢查定義是否已被修改。' },
  { label: '退版', t: 85, title: '還原舊版，同時留下紀錄', detail: '進版與退版都記錄操作者、時間與原因。' },
]
const current = computed(() => chapters[selected.value])
// Vite checks local files at build time, so a missing recording makes no network request.
const recordings = import.meta.glob('/public/video/demo.mp4', { query: '?url', import: 'default', eager: true })
const hasVideo = Object.keys(recordings).length > 0
function seek(i: number) {
  selected.value = i
  if (video.value) {
    video.value.currentTime = chapters[i].t
    video.value.play().catch(() => {})
  }
}
function sync() {
  if (!video.value) return
  selected.value = Math.max(0, chapters.findLastIndex(c => video.value!.currentTime >= c.t))
}
</script>

<template>
  <div class="demo-stage">
    <div v-if="hasVideo && live" class="demo-window">
      <video ref="video" controls muted playsinline poster="/video/demo-poster.svg" @timeupdate="sync">
        <source :src="'/video/demo.mp4'" type="video/mp4">
      </video>
    </div>
    <div v-else class="demo-outline">
      <div class="card-kicker">{{ hasVideo ? 'SSMS DEMO' : 'SSMS 演示流程' }}</div>
      <template v-if="live">
        <div class="demo-step" :class="current.tone">0{{ selected + 1 }}</div>
        <h2>{{ current.title }}</h2>
        <div class="lead">{{ current.detail }}</div>
      </template>
      <template v-else>
        <h2>看牽連，驗證改寫，保留版本</h2>
        <div class="lead">先演示退回案例，再演示通過案例。<br>每次進版與退版，都由人確認。</div>
      </template>
      <div v-if="!hasVideo" class="tiny soft demo-disclosure">流程示意，非產品執行畫面。錄影尚未提供。</div>
    </div>
    <div class="chapters">
      <button v-for="(chapter, i) in chapters" :key="chapter.label" type="button" class="chip chapter" :class="[chapter.tone, { on: live && i === selected }]" :aria-pressed="live && i === selected" @click="seek(i)">{{ chapter.label }}</button>
    </div>
  </div>
</template>
