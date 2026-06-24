<script setup lang="ts">
import { computed, h, ref } from 'vue'
import { useRouter, RouterView } from 'vue-router'
import { LogoutOutlined, TeamOutlined, UserAddOutlined, ApiOutlined } from '@ant-design/icons-vue'
import { useAuthStore } from '../stores/auth'

const router = useRouter()
const auth = useAuthStore()
const collapsed = ref(false)

const menus = computed(() => [
  { key: '/dashboard', icon: () => h(TeamOutlined), label: '人员' },
  { key: '/visitors', icon: () => h(UserAddOutlined), label: '访客' },
  { key: '/hikiot', icon: () => h(ApiOutlined), label: '海康连接' },
])

function leave() {
  auth.logout()
  router.push('/login')
}
</script>

<template>
  <a-layout class="shell">
    <a-layout-sider v-model:collapsed="collapsed" collapsible breakpoint="lg" class="sider">
      <div class="brand"><router-link to="/">开一个门</router-link></div>
      <a-menu theme="dark" mode="inline" :selected-keys="[router.currentRoute.value.path]" :items="menus" @click="({ key }: { key: string }) => router.push(key)" />
    </a-layout-sider>
    <a-layout>
      <a-layout-header class="header">
        <a-space>
          <span class="username">{{ auth.user?.name }}</span>
          <a-button type="link" size="small" @click="leave"><LogoutOutlined />退出登录</a-button>
        </a-space>
      </a-layout-header>
      <a-layout-content class="content">
        <div class="page-canvas"><RouterView /></div>
      </a-layout-content>
    </a-layout>
  </a-layout>
</template>

<style scoped>
.shell, .sider { min-height: 100vh; }
.brand { height: 64px; display: flex; align-items: center; justify-content: center; overflow: hidden; color: #fff; font-size: 18px; font-weight: 700; }
.brand a { color: inherit; text-decoration: none; white-space: nowrap; }
.header { display: flex; align-items: center; justify-content: flex-end; padding: 0 24px; background: #fff; }
.username { color: #333; font-weight: 500; }
.content { margin: 16px; }
.page-canvas { min-height: calc(100vh - 132px); padding: 24px; background: #fff; }
@media (max-width: 700px) { .content { margin: 0; } .page-canvas { min-height: calc(100vh - 64px); padding: 16px; } }
</style>
