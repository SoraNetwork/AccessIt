<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { message } from 'ant-design-vue'
import api from '../services/api'
import type { Device } from '../types'
import { useAuthStore } from '../stores/auth'
import { canManageAccess } from '../utils/permissions'

const auth = useAuthStore()
const devices = ref<Device[]>([])
const loading = ref(false)
const reason = ref('')
const target = ref<Device | null>(null)
const showOpen = ref(false)

const pagination = { pageSize: 10, showSizeChanger: true, pageSizeOptions: ['10', '20', '50', '100'], showQuickJumper: true, showTotal: (total: number) => `共 ${total} 台` }
const columns = [
  { title: '设备分组', dataIndex: 'groupName', key: 'groupName' },
  { title: '设备序列号', dataIndex: 'deviceSerial', key: 'deviceSerial' },
  { title: '底层硬件能力集', key: 'capability' },
  { title: '全天通行模板 (Plan Template)', key: 'templateStatus' },
  { title: '最近同步时间', dataIndex: 'lastSyncedAtUtc', key: 'lastSyncedAtUtc' },
  { title: '操作选项', key: 'action' }
]
const editable = () => canManageAccess(auth.role)

async function load() {
  loading.value = true
  try {
    const result = await api.get<Device[]>('/devices')
    devices.value = result.data
  } catch (error: any) {
    message.error(error?.response?.data?.message || '加载设备列表失败')
  } finally {
    loading.value = false
  }
}

async function discover() {
  try {
    await api.post('/devices/discover', undefined, { loadingText: '正在同步 HIKIoT 设备分组与能力集…' })
    message.success('外部设备组与能力集同步完成')
    await load()
  } catch (error: any) {
    message.error(error?.response?.data?.message || '同步设备组失败')
  }
}

function confirmOpen(device: Device) {
  target.value = device
  reason.value = ''
  showOpen.value = true
}

async function open() {
  if (!target.value || !reason.value) {
    message.warning('请填写开门事由')
    return
  }
  try {
    const { data } = await api.post(`/devices/${target.value.id}/open`, { reason: reason.value }, { loadingText: '正在向设备发送开门指令…' })
    if (data.succeeded) {
      message.success('开门指令发送成功，设备响应通过')
    } else {
      message.error(`指令发送失败: ${data.message}`)
    }
  } catch (error: any) {
    message.error(error?.response?.data?.message || '指令执行失败')
  } finally {
    showOpen.value = false
    target.value = null
  }
}

async function sync(device: Device) {
  try {
    await api.post(`/devices/${device.id}/sync`, undefined, { loadingText: '正在读取设备人员并比对差异…' })
    message.success('人员信息同步触发成功，请到任务监控查看同步差异')
  } catch (error: any) {
    message.error(error?.response?.data?.message || '同步失败')
  }
}

onMounted(load)
</script>

<template>
  <section class="premium-container">
    <a-page-header title="设备管理" sub-title="管理已纳管门禁机硬件，并在本系统与 HIKIoT 控制中心之间同步配置" class="glass-header">
      <template #extra>
        <a-button v-if="editable()" type="primary" class="btn-primary" :loading="loading" @click="discover">
          同步 HIKIoT 侧分组
        </a-button>
      </template>
    </a-page-header>

    <a-card class="glass-card">
      <a-table 
        :columns="columns" 
        :data-source="devices" 
        :loading="loading" 
        :pagination="pagination" 
        :row-key="(d: Device) => d.id" 
        :scroll="{ x: 960 }"
        size="middle"
        class="custom-table"
      >
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'groupName'">
            <strong>{{ record.groupName || '未分组' }}</strong>
          </template>

          <template v-else-if="column.key === 'deviceSerial'">
            <span class="serial-text">{{ record.deviceSerial }}</span>
          </template>

          <template v-else-if="column.key === 'capability'">
            <a-space wrap class="tag-space">
              <a-tag :color="record.supportsUserInfo ? 'blue' : 'default'" class="glow-tag">人员</a-tag>
              <a-tag :color="record.supportsCardInfo ? 'cyan' : 'default'" class="glow-tag">物理卡</a-tag>
              <a-tag :color="record.supportsFace ? 'purple' : 'default'" class="glow-tag">人脸识别</a-tag>
              <a-tag :color="record.supportsPurePassword ? 'orange' : 'default'" class="glow-tag">纯密码</a-tag>
              <a-tag :color="record.supportsRemoteOpen ? 'pink' : 'default'" class="glow-tag">远程控制</a-tag>
              <a-tag :color="record.supportsUserRightPlanTemplate ? 'green' : 'default'" class="glow-tag">通行模板</a-tag>
            </a-space>
          </template>

          <template v-else-if="column.key === 'templateStatus'">
            <div v-if="record.supportsUserRightPlanTemplate" class="template-info">
              <template v-if="record.hasAllDayTemplate">
                <a-tag color="success" class="glow-tag">就绪 (Id: {{ record.allDayTemplateId || '8' }})</a-tag>
              </template>
              <template v-else>
                <a-tag color="warning" class="glow-tag">待初始化</a-tag>
              </template>
            </div>
            <span class="muted-text" v-else>该机型无模板要求</span>
          </template>

          <template v-else-if="column.key === 'lastSyncedAtUtc'">
            <span v-if="record.lastSyncedAtUtc">{{ new Date(record.lastSyncedAtUtc).toLocaleString() }}</span>
            <span class="muted-text" v-else>尚未比对</span>
          </template>

          <template v-else-if="column.key === 'action'">
            <a-space v-if="editable()" wrap>
              <a-button size="small" class="btn-sync" @click="sync(record)">比对差异</a-button>
              <a-button v-if="record.supportsRemoteOpen" danger size="small" class="btn-open" @click="confirmOpen(record)">
                远程开门
              </a-button>
            </a-space>
            <span class="muted-text" v-else>-</span>
          </template>
        </template>
      </a-table>
    </a-card>

    <!-- Remote Open Confirm Modal -->
    <a-modal v-model:open="showOpen" title="远程开门指令确认" @ok="open" class="premium-modal">
      <a-alert type="warning" show-icon message="安全警示：向物理门禁终端下发开门脉冲属于高危审计动作，请妥善填写原因。" style="margin-bottom: 20px;" />
      <a-form layout="vertical">
        <a-form-item label="本次开门是由 / 授权事由" required>
          <a-textarea v-model:value="reason" :rows="3" placeholder="例如：员工临时遗忘带卡，核对身份后予以开门..." />
        </a-form-item>
      </a-form>
    </a-modal>
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
.custom-table :deep(.ant-table) {
  background: transparent;
}
.custom-table :deep(.ant-table-thead > tr > th) {
  background: rgba(0, 0, 0, 0.03);
  font-weight: 600;
}
.serial-text {
  font-family: monospace;
  font-weight: 500;
}
.glow-tag {
  box-shadow: 0 2px 8px rgba(0,0,0,0.05);
  border-radius: 4px;
}
.tag-space {
  gap: 4px;
}
.template-info {
  display: flex;
  align-items: center;
}
.muted-text {
  color: #bfbfbf;
}
.btn-sync {
  border-radius: 6px;
  border-color: #1890ff;
  color: #1890ff;
}
.btn-sync:hover {
  background: rgba(24, 144, 255, 0.05);
}
.btn-open {
  border-radius: 6px;
  background: #ff4d4f;
  color: white;
}
.btn-open:hover {
  background: #ff7875;
  border-color: #ff7875;
}
.btn-primary {
  box-shadow: 0 2px 8px rgba(24,144,255,0.2);
}
</style>
