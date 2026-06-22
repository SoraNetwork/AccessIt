<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { getDingTalkBrowserRedirectUri } from '../utils/dingtalkAuth'

declare global {
  interface Window {
    dd?: {
      ready: (fn: () => void) => void
      runtime?: { permission?: { requestAuthCode: (args: { corpId: string }) => Promise<{ code: string }> } }
    }
  }
}

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const loading = ref(false)
const error = ref('')

async function finish(code: string, inApp = false) {
  loading.value = true
  error.value = ''
  try {
    await auth.loginWithCode(code, inApp)
    await router.replace('/dashboard')
  } catch (exception: any) {
    error.value = exception.response?.data || '钉钉登录失败，请检查应用配置和权限。'
  } finally {
    loading.value = false
  }
}

function loginWeb() {
  const appKey = import.meta.env.VITE_DINGTALK_APP_KEY
  if (!appKey) {
    error.value = '尚未配置 VITE_DINGTALK_APP_KEY。'
    return
  }
  const redirectUri = encodeURIComponent(getDingTalkBrowserRedirectUri(window.location.origin))
  window.location.href = `https://login.dingtalk.com/oauth2/auth?redirect_uri=${redirectUri}&response_type=code&client_id=${appKey}&scope=openid&state=accessit`
}

onMounted(async () => {
  const code = route.query.code
  if (typeof code === 'string' && code) {
    await finish(code)
    return
  }

  const corpId = import.meta.env.VITE_DINGTALK_CORP_ID
  if (/DingTalk/i.test(navigator.userAgent) && corpId && window.dd?.runtime?.permission) {
    window.dd.ready(async () => {
      try {
        const result = await window.dd!.runtime!.permission!.requestAuthCode({ corpId })
        await finish(result.code, true)
      } catch {
        error.value = '钉钉工作台免登失败。'
      }
    })
  }
})
</script>

<template>
  <main class="login-container">
    <a-card class="login-card" title="开一个门">
      <p class="login-subtitle">请使用钉钉账号登录</p>
      <a-alert v-if="error" class="login-alert" type="error" :message="error" show-icon />
      <a-button type="primary" block size="large" :loading="loading" @click="loginWeb">使用钉钉登录</a-button>
    </a-card>
  </main>
</template>

<style scoped>
.login-container { display: flex; justify-content: center; align-items: center; min-height: 100vh; padding: 16px; background: #f0f2f5; }
.login-card { width: 450px; max-width: 100%; text-align: center; }
.login-subtitle { margin-bottom: 24px; color: rgb(0 0 0 / 45%); }
.login-alert { margin-bottom: 16px; text-align: left; }
</style>
