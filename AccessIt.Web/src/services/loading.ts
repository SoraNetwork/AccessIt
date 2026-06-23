import { computed, ref } from 'vue'

const pendingRequests = ref(0)
const loadingText = ref('正在加载…')

export const isGlobalLoading = computed(() => pendingRequests.value > 0)
export const globalLoadingText = computed(() => loadingText.value)

export function beginGlobalLoading(text?: string) {
  pendingRequests.value += 1
  if (text) loadingText.value = text
}

export function endGlobalLoading() {
  pendingRequests.value = Math.max(0, pendingRequests.value - 1)
  if (pendingRequests.value === 0) loadingText.value = '正在加载…'
}
