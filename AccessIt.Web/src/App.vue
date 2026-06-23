<script setup lang="ts">
import { globalLoadingText, isGlobalLoading } from './services/loading'
</script>

<template>
  <router-view />
  <div v-if="isGlobalLoading" class="global-loading" role="status" aria-live="polite">
    <a-spin size="large" />
    <span>{{ globalLoadingText }}</span>
    <small>请勿关闭此页面</small>
  </div>
</template>

<style scoped>
.global-loading { position: fixed; inset: 0; z-index: 3000; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 12px; color: #fff; background: rgb(0 0 0 / 52%); font-size: 16px; }
.global-loading small { color: rgb(255 255 255 / 75%); }
</style>
