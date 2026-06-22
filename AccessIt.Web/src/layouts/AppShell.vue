<script setup lang="ts">
import { computed, h, ref } from 'vue'
import { useRouter, RouterView } from 'vue-router'
import { AppstoreOutlined, AuditOutlined, ControlOutlined, LogoutOutlined, SettingOutlined, TeamOutlined, UnlockOutlined, UserOutlined } from '@ant-design/icons-vue'
import { useAuthStore } from '../stores/auth'
import { canManageSystem } from '../utils/permissions'

const router = useRouter()
const auth = useAuthStore()
const collapsed = ref(false)
const menus = computed(() => {
  const items = [
    { key: '/dashboard', icon: () => h(AppstoreOutlined), label: '工作台' },
    { key: '/people', icon: () => h(TeamOutlined), label: '人员管理' },
    { key: '/devices', icon: () => h(UnlockOutlined), label: '设备管理' },
    { key: '/operations', icon: () => h(ControlOutlined), label: '同步与任务' },
    { key: '/audit', icon: () => h(AuditOutlined), label: '审计日志' }
  ]
  if (canManageSystem(auth.role)) {
    items.push(
      { key: '/settings', icon: () => h(SettingOutlined), label: '系统设置' },
      { key: '/users', icon: () => h(UserOutlined), label: '系统用户' }
    )
  }
  return items
})

function leave() {
  auth.logout()
  router.push('/login')
}

function onMenuClick(info: { key: string | number }) {
  router.push(String(info.key))
}
</script>

<template>
  <a-layout class="shell">
    <a-layout-sider v-model:collapsed="collapsed" collapsible breakpoint="lg" class="sider">
      <div class="brand"><router-link to="/dashboard">开一个门</router-link></div>
      <a-menu theme="dark" mode="inline" :selected-keys="[router.currentRoute.value.path]" :items="menus" @click="onMenuClick" />
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
