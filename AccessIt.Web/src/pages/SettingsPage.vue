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
const syncLoading = ref(false)

const departmentOptions = computed(() => departments.value.map(item => ({
  value: item.departmentNo,
  label: `${item.path || item.name} (${item.departmentNo})`
})))

async function load() {
  try {
    const connResult = await api.get<ConnectionStatus>('/hikiot/connection')
    status.value = connResult.data
    selectedDepartment.value = status.value.defaultDepartmentNo || undefined
    if (status.value.isAuthorized) {
      const deptResult = await api.get<Department[]>('/hikiot/departments')
      departments.value = deptResult.data
    }
  } catch (error: any) {
    message.error(error?.response?.data?.message || '读取连接状态或部门列表失败')
  }
}

async function authorize() {
  loading.value = true
  try {
    const { data } = await api.post('/hikiot/connection/authorize')
    window.location.href = data.authorizationUrl
  } catch (error: any) {
    message.error(error?.response?.data?.message || '无法启动 HIKIoT 授权流')
  } finally {
    loading.value = false
  }
}

async function saveDefaultDepartment() {
  if (!selectedDepartment.value) return
  try {
    await api.put('/hikiot/connection/default-department', { departmentNo: selectedDepartment.value })
    status.value.defaultDepartmentNo = selectedDepartment.value
    message.success('默认同步部门保存成功')
  } catch (error: any) {
    message.error(error?.response?.data?.message || '保存部门失败')
  }
}

async function syncSources() {
  syncLoading.value = true
  try {
    const { data } = await api.post('/people/sync-sources')
    message.success(`外部通讯录同步完成：海康新增 ${data.hikiot.created}、更新 ${data.hikiot.updated}；钉钉新增 ${data.dingTalk.created}、更新 ${data.dingTalk.updated}`)
    await load()
  } catch (error: any) {
    message.error(error?.response?.data?.message || '外部组织目录同步异常')
  } finally {
    syncLoading.value = false
  }
}

onMounted(load)
</script>

<template>
  <section class="premium-container">
    <a-page-header title="系统设置" sub-title="在此配置 AccessIt 平台与钉钉和 HIKIoT 开放平台的第三方服务连接" class="glass-header" />
    
    <a-row :gutter="[16, 16]">
      <a-col :xs="24" :lg="12">
        <a-card title="HIKIoT 开放平台授权" class="glass-card">
          <a-descriptions :column="1" size="small" class="custom-descriptions">
            <a-descriptions-item label="接口连接状态">
              <a-tag :color="status.isAuthorized ? 'success' : 'error'" class="glow-tag">
                {{ status.isAuthorized ? 'API 已授权' : '连接中断/未授权' }}
              </a-tag>
            </a-descriptions-item>
            <a-descriptions-item label="绑定团队 No.">
              <span class="value-highlight" v-if="status.teamNo">{{ status.teamNo }}</span>
              <span class="muted-text" v-else>未绑定团队</span>
            </a-descriptions-item>
            <a-descriptions-item label="凭证过期时间">
              <span v-if="status.userTokenExpiresAtUtc">{{ new Date(status.userTokenExpiresAtUtc).toLocaleString() }}</span>
              <span class="muted-text" v-else>无有效期数据</span>
            </a-descriptions-item>
            <a-descriptions-item v-if="status.lastError" label="最近通讯异常" class="error-msg">
              {{ status.lastError }}
            </a-descriptions-item>
          </a-descriptions>
          <div class="card-action-bar">
            <a-button type="primary" :loading="loading" class="btn-primary" @click="authorize">
              {{ status.isAuthorized ? '重置并重新关联团队' : '开始接入授权' }}
            </a-button>
          </div>
        </a-card>
      </a-col>

      <a-col :xs="24" :lg="12">
        <a-card title="同步默认落脚组织" class="glass-card">
          <p class="section-desc">同步正式员工到 HIKIoT 团队架构时，若缺少所属部门，会被默认下发分配到此部门下运行。</p>
          <div class="form-row">
            <a-select 
              v-model:value="selectedDepartment" 
              style="width: 100%; max-width: 320px;" 
              :disabled="!status.isAuthorized" 
              :options="departmentOptions" 
              placeholder="请选择落脚团队部门..." 
              class="custom-select"
            />
            <a-button type="primary" :disabled="!selectedDepartment" class="btn-primary" @click="saveDefaultDepartment">
              保存更改
            </a-button>
          </div>
        </a-card>
      </a-col>

      <a-col :span="24">
        <a-card title="手动同步第三方组织" class="glass-card">
          <p class="section-desc">自动匹配钉钉和 HIKIoT 侧组织人员。优先根据第三方映射 ID 匹配，兜底姓名重合匹配，合并组织变动资料。此操作不会引发海康物理设备端凭证自动擦除。</p>
          <a-button type="primary" class="btn-primary" :loading="syncLoading" @click="syncSources">
            手动触发布局同步
          </a-button>
        </a-card>
      </a-col>
    </a-row>
  </section>
</template>

<style scoped>
.premium-container {
  padding: 16px;
  background: linear-gradient(135deg, #f5f7fa 0%, #c3cfe2 100%);
  min-height: 100vh;
}
.glass-header {
  background: rgba(255, 255, 255, 0.7);
  backdrop-filter: blur(10px);
  border-radius: 12px;
  margin-bottom: 20px;
  box-shadow: 0 4px 16px rgba(0,0,0,0.05);
}
.glass-card {
  background: rgba(255, 255, 255, 0.65);
  backdrop-filter: blur(10px);
  border-radius: 16px;
  border: 1px solid rgba(255,255,255,0.4);
  box-shadow: 0 8px 32px rgba(31, 38, 135, 0.05);
}
.custom-descriptions {
  margin-bottom: 16px;
}
.glow-tag {
  box-shadow: 0 2px 8px rgba(0,0,0,0.05);
  border-radius: 4px;
}
.value-highlight {
  font-weight: 600;
  color: #1890ff;
}
.muted-text {
  color: #bfbfbf;
}
.error-msg {
  color: #ff4d4f;
}
.card-action-bar {
  margin-top: 16px;
}
.section-desc {
  color: #595959;
  font-size: 13px;
  margin-bottom: 16px;
  line-height: 1.5;
}
.form-row {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  align-items: center;
}
.btn-primary {
  box-shadow: 0 2px 8px rgba(24,144,255,0.2);
  border-radius: 6px;
}
.custom-select :deep(.ant-select-selector) {
  border-radius: 6px !important;
}
</style>
