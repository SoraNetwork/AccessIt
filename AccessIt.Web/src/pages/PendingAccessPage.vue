<script setup lang="ts">
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'

const router = useRouter()
const auth = useAuthStore()

function signOut() {
  auth.logout()
  router.replace('/login')
}
</script>

<template>
  <main class="pending">
    <a-result status="403" title="等待门禁管理员授权" sub-title="你的钉钉账号已登录，但尚未被授予“开一个门”的系统角色。请联系超级管理员开通权限。">
      <template #extra><a-button type="primary" @click="signOut">退出登录</a-button></template>
    </a-result>
  </main>
</template>

<style scoped>
.pending { min-height: 100vh; display: grid; place-items: center; padding: 24px; background: #f5f5f5; }
</style>
