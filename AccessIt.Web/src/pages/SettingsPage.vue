<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { message } from 'ant-design-vue'
import api from '../services/api'

interface ConnectionStatus {
  isAuthorized: boolean
  needsReauthorization: boolean
  teamNo?: string | null
  defaultDepartmentNo?: string | null
  userTokenExpiresAtUtc?: string | null
  lastError?: string | null
}
interface Department { departmentNo: string; name: string; path?: string | null; personCount: number }

const status = ref<ConnectionStatus>({ isAuthorized: false, needsReauthorization: true })
const departments = ref<Department[]>([])
const selectedDepartment = ref<string>()
const loading = ref(false)
const departmentOptions = computed(() => departments.value.map(item => ({
  value: item.departmentNo,
  label: `${item.path || item.name} (${item.departmentNo})`
})))

async function load() {
  status.value = (await api.get('/hikiot/connection')).data
  selectedDepartment.value = status.value.defaultDepartmentNo || undefined
  if (status.value.isAuthorized) departments.value = (await api.get('/hikiot/departments')).data
}
async function authorize() {
  loading.value = true
  try {
    const { data } = await api.post('/hikiot/connection/authorize')
    window.location.href = data.authorizationUrl
  } catch { message.error('无法开始 HIKIoT 授权') } finally { loading.value = false }
}
async function saveDefaultDepartment() {
  if (!selectedDepartment.value) return
  await api.put('/hikiot/connection/default-department', { departmentNo: selectedDepartment.value })
  status.value.defaultDepartmentNo = selectedDepartment.value
  message.success('默认团队部门已保存')
}
async function syncSources() {
  const { data } = await api.post('/people/sync-sources')
  message.success(`同步完成：HIKIoT 新增 ${data.hikiot.created}、更新 ${data.hikiot.updated}；钉钉新增 ${data.dingTalk.created}、更新 ${data.dingTalk.updated}`)
}
onMounted(load)
</script>

<template>
  <section>
    <a-page-header title="系统设置" sub-title="钉钉身份与 HIKIoT 团队连接" />
    <a-row :gutter="16">
      <a-col :xs="24" :lg="12">
        <a-card title="HIKIoT 团队授权">
          <a-descriptions :column="1">
            <a-descriptions-item label="状态"><a-tag :color="status.isAuthorized ? 'green' : 'red'">{{ status.isAuthorized ? '已连接' : '待授权' }}</a-tag></a-descriptions-item>
            <a-descriptions-item label="团队编号">{{ status.teamNo || '-' }}</a-descriptions-item>
            <a-descriptions-item label="Token 到期">{{ status.userTokenExpiresAtUtc ? new Date(status.userTokenExpiresAtUtc).toLocaleString() : '-' }}</a-descriptions-item>
            <a-descriptions-item v-if="status.lastError" label="最近错误">{{ status.lastError }}</a-descriptions-item>
          </a-descriptions>
          <a-space>
            <a-button type="primary" :loading="loading" @click="authorize">{{ status.isAuthorized ? '重新授权' : '开始授权' }}</a-button>
          </a-space>
        </a-card>
      </a-col>
      <a-col :xs="24" :lg="12">
        <a-card title="团队人员发布默认部门">
          <p>正式员工发布到 HIKIoT 团队时会使用此固定部门；未配置时发布会被阻止。</p>
          <a-space>
            <a-select v-model:value="selectedDepartment" style="min-width: 280px" :disabled="!status.isAuthorized" :options="departmentOptions" placeholder="选择团队部门" />
            <a-button :disabled="!selectedDepartment" @click="saveDefaultDepartment">保存</a-button>
          </a-space>
        </a-card>
      </a-col>
      <a-col :span="24" style="margin-top:16px">
        <a-card title="外部目录同步">
          <p>同步仅手动执行。HIKIoT 和钉钉目录按外部 ID 优先、姓名兜底合并，不会删除 HIKIoT 团队成员或自动授予设备权限。</p>
          <a-button @click="syncSources">同步钉钉与 HIKIoT 团队目录</a-button>
        </a-card>
      </a-col>
    </a-row>
  </section>
</template>
