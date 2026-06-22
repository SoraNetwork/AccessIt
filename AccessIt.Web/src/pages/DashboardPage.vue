<script setup lang="ts">
import { onMounted, ref } from 'vue'
import api from '../services/api'
import type { Device, Job } from '../types'

const devices = ref<Device[]>([])
const jobs = ref<Job[]>([])
const connection = ref<{ isAuthorized: boolean; needsReauthorization: boolean; teamNo?: string }>({ isAuthorized: false, needsReauthorization: true })

async function load() {
  const [deviceResult, jobResult, connectionResult] = await Promise.allSettled([
    api.get<Device[]>('/devices'),
    api.get<Job[]>('/jobs?status=Failed'),
    api.get('/hikiot/connection')
  ])
  if (deviceResult.status === 'fulfilled') devices.value = deviceResult.value.data
  if (jobResult.status === 'fulfilled') jobs.value = jobResult.value.data
  if (connectionResult.status === 'fulfilled') connection.value = connectionResult.value.data
}

onMounted(load)
</script>

<template>
  <section>
    <a-page-header title="工作台" sub-title="开一个门 · AccessIt" />
    <a-card>
      <a-descriptions :column="1" bordered size="small">
        <a-descriptions-item label="HIKIoT 授权">
          <a-tag :color="connection.isAuthorized ? 'success' : 'warning'">{{ connection.isAuthorized ? '已连接' : '待授权' }}</a-tag>
          <span v-if="connection.teamNo">团队：{{ connection.teamNo }}</span>
          <span v-else>请在系统设置完成团队授权</span>
        </a-descriptions-item>
        <a-descriptions-item label="已纳管设备">{{ devices.length }} 台</a-descriptions-item>
        <a-descriptions-item label="失败下发任务">{{ jobs.length }} 个</a-descriptions-item>
      </a-descriptions>
      <a-divider />
      <a-steps direction="vertical" size="small" :current="connection.isAuthorized ? 2 : 0">
        <a-step title="配置钉钉与 HIKIoT 凭证" />
        <a-step title="完成 HIKIoT 团队授权" />
        <a-step title="同步设备后新增人员" />
      </a-steps>
    </a-card>
  </section>
</template>
