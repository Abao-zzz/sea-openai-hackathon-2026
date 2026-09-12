// 「現在是不是現場放映」的唯一判斷，layout 與 CountUp / TypeIn / BarPair 共用。
// 現場 = 一般放映或 presenter 視圖，且不是 print mode。
// 不能用 $nav.isPrintMode：匯出時 Slidev 給每張 slide 的是 fixed nav，上面沒有這個欄位，
// 會誤判成現場而讓 PDF 抓到動畫中途。這裡改用全域 useNav()（看 URL 的 ?print）。
import { computed } from 'vue'
import { useNav, useSlideContext } from '@slidev/client'

export function useLive() {
  const { $renderContext } = useSlideContext()
  const { isPrintMode } = useNav()
  return computed(() => {
    const ctx = $renderContext.value
    return (ctx === 'slide' || ctx === 'presenter') && !isPrintMode.value
  })
}
