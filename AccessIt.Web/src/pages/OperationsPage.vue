<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { message } from 'ant-design-vue'
import api from '../services/api'
import type { Job, SyncConflict, HikiotIssueBatch } from '../types'
import { useAuthStore } from '../stores/auth'
import { canManageAccess } from '../utils/permissions'

const auth = useAuthStore()
const jobs = ref<Job[]>([])
const conflicts = ref<SyncConflict[]>([])
const hikiotBatches = ref<HikiotIssueBatch[]>([])
const loading = ref(false)
const activeTab = ref('jobs')

const editable = () => canManageAccess(auth.role)
const pagination = { pageSize: 10, showSizeChanger: true, pageSizeOptions: ['10', '20', '50', '100'], showQuickJumper: true, showTotal: (total: number) => `共 ${total} 项` }

async function load() {
  loading.value = true
  try {
    const [j, c, h] = await Promise.all([
      api.get<Job[]>('/jobs'),
      api.get<SyncConflict[]>('/sync-conflicts', { params: { resolution: 'Pending' } }),
      api.get<HikiotIssueBatch[]>('/hikiot-issue-batches')
    ])
    jobs.value = j.data
    conflicts.value = c.data
    hikiotBatches.value = h.data
  } catch (error: any) {
    message.error(error?.response?.data?.message || '加载运行数据失败')
  } finally {
    loading.value = false
  }
}

async function retryJob(jobId: string) {
  try {
    await api.post(`/jobs/${jobId}/retry`, undefined, { loadingText: '正在提交重试请求…' })
    message.success('任务重试请求提交成功')
    await load()
  } catch (error: any) {
    message.error(error?.response?.data?.message || '任务重试失败')
  }
}

async function resolve(id: string, resolution: string) {
  try {
    await api.post(`/devices/sync-conflicts/${id}/resolve`, { resolution }, { loadingText: '正在处理同步冲突…' })
    message.success('冲突已成功处理')
    await load()
  } catch (error: any) {
    message.error(error?.response?.data?.message || '冲突处理失败')
  }
}

onMounted(load)
</script>

<template>
  <section class="premium-container">
    <a-page-header title="运行监控" sub-title="实时监控访客直连设备下发状态、海康标准授权批次以及本地-设备同步冲突" class="glass-header">
      <template #extra>
        <a-button type="primary" class="btn-primary" :loading="loading" @click="load">刷新数据</a-button>
      </template>
    </a-page-header>

    <div class="glass-card-wrapper">
      <a-card class="glass-card">
        <a-tabs v-model:activeKey="activeTab" class="custom-tabs">
          <!-- Visitor Device Jobs -->
          <a-tab-pane key="jobs" tab="访客设备任务 (Job Queue)">
            <a-table 
              :data-source="jobs" 
              :loading="loading" 
              :pagination="pagination" 
              :row-key="(j: Job) => j.id" 
              :scroll="{ x: 800 }"
              size="middle"
              class="custom-table"
            >
              <a-table-column title="任务步骤" data-index="type" key="type" />
              <a-table-column title="当前状态" data-index="status" key="status">
                <template #default="{ text }">
                  <a-tag :color="text === 'Succeeded' ? 'success' : text === 'Failed' ? 'error' : text === 'Cancelled' ? 'default' : 'processing'" class="glow-tag">
                    {{ text === 'Succeeded' ? '成功' : text === 'Failed' ? '失败' : text === 'Cancelled' ? '已取消' : '执行中/重试中' }}
                  </a-tag>
                </template>
              </a-table-column>
              <a-table-column title="尝试次数" data-index="attemptCount" key="attemptCount" />
              <a-table-column title="最近下发 TraceID" data-index="traceId" key="traceId">
                <template #default="{ text }">
                  <span v-if="text" class="trace-id">{{ text }}</span>
                  <span class="muted-text" v-else>-</span>
                </template>
              </a-table-column>
              <a-table-column title="异常信息 / 说明" data-index="failureMessage" key="failureMessage">
                <template #default="{ text, record }">
                  <span v-if="text" class="failed-reason">
                    <span v-if="record.failureCode" class="error-code">[{{ record.failureCode }}]</span>
                    {{ text }}
                  </span>
                  <span class="success-reason" v-else>正常</span>
                </template>
              </a-table-column>
              <a-table-column title="操作" key="action" v-if="editable()">
                <template #default="{ record }">
                  <a-button 
                    v-if="record.status === 'Failed' || record.status === 'Cancelled'" 
                    size="small" 
                    type="primary" 
                    class="action-btn"
                    @click="retryJob(record.id)"
                  >
                    重试任务
                  </a-button>
                  <span v-else class="muted-text">-</span>
                </template>
              </a-table-column>
            </a-table>
          </a-tab-pane>

          <!-- HIKIoT Issue Batches -->
          <a-tab-pane key="hikiot" tab="海康下发批次 (HIKIoT Issue Batches)">
            <a-table 
              :data-source="hikiotBatches" 
              :loading="loading" 
              :pagination="pagination" 
              :row-key="(h: HikiotIssueBatch) => h.id" 
              :scroll="{ x: 800 }"
              size="middle"
              class="custom-table"
            >
              <a-table-column title="批次号" data-index="batchNo" key="batchNo" />
              <a-table-column title="设备序列号" data-index="deviceSerial" key="deviceSerial">
                <template #default="{ text }">
                  <strong>{{ text || '全部关联设备' }}</strong>
                </template>
              </a-table-column>
              <a-table-column title="执行状态" data-index="status" key="status">
                <template #default="{ text }">
                  <a-tag :color="text === 'Succeeded' ? 'success' : text === 'Failed' ? 'error' : 'warning'" class="glow-tag">
                    {{ text === 'Succeeded' ? '成功' : text === 'Failed' ? '失败' : '处理中 (Submitted/Pending)' }}
                  </a-tag>
                </template>
              </a-table-column>
              <a-table-column title="创建时间" data-index="createdAtUtc" key="createdAtUtc">
                <template #default="{ text }">
                  {{ new Date(text).toLocaleString() }}
                </template>
              </a-table-column>
              <a-table-column title="最新校对时间" data-index="checkedAtUtc" key="checkedAtUtc">
                <template #default="{ text }">
                  {{ text ? new Date(text).toLocaleString() : '等待核验' }}
                </template>
              </a-table-column>
              <a-table-column title="错误日志" data-index="failureReason" key="failureReason">
                <template #default="{ text }">
                  <span v-if="text" class="failed-reason">{{ text }}</span>
                  <span class="success-reason" v-else>无异常</span>
                </template>
              </a-table-column>
            </a-table>
          </a-tab-pane>

          <!-- Sync Conflicts -->
          <a-tab-pane key="conflicts" tab="待确认同步差异">
            <a-table 
              :data-source="conflicts" 
              :loading="loading" 
              :pagination="pagination" 
              :row-key="(c: SyncConflict) => c.id" 
              :scroll="{ x: 700 }"
              size="middle"
              class="custom-table"
            >
              <a-table-column title="人员编号" data-index="employeeNo" key="employeeNo" />
              <a-table-column title="冲突字段" data-index="fieldName" key="fieldName">
                <template #default="{ text }">
                  <a-tag color="blue">{{ text }}</a-tag>
                </template>
              </a-table-column>
              <a-table-column title="本地系统值 (AccessIt)" data-index="localValue" key="localValue">
                <template #default="{ text }">
                  <span class="value-local">{{ text || '(空 / 未设置)' }}</span>
                </template>
              </a-table-column>
              <a-table-column title="物理设备回传值" data-index="remoteValue" key="remoteValue">
                <template #default="{ text }">
                  <span class="value-remote">{{ text || '(未找到匹配值)' }}</span>
                </template>
              </a-table-column>
              <a-table-column title="决策操作" key="action" v-if="editable()">
                <template #default="{ record }">
                  <a-space wrap>
                    <a-button size="small" class="btn-keep-local" @click="resolve(record.id, 'KeepLocal')">
                      保留本地并下发
                    </a-button>
                    <a-button size="small" class="btn-keep-device" @click="resolve(record.id, 'KeepDevice')">
                      同步设备值到本地
                    </a-button>
                  </a-space>
                </template>
              </a-table-column>
            </a-table>
          </a-tab-pane>
        </a-tabs>
      </a-card>
    </div>
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
.glow-tag {
  box-shadow: 0 2px 8px rgba(0,0,0,0.05);
  border-radius: 4px;
}
.trace-id {
  font-family: monospace;
  font-size: 11px;
  background: #e6f7ff;
  color: #1890ff;
  padding: 2px 6px;
  border-radius: 4px;
}
.failed-reason {
  color: #ff4d4f;
  font-size: 12px;
}
.error-code {
  font-weight: bold;
  margin-right: 4px;
}
.success-reason {
  color: #52c41a;
  font-size: 12px;
}
.muted-text {
  color: #bfbfbf;
}
.action-btn {
  border-radius: 6px;
  transition: all 0.2s;
}
.action-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 2px 6px rgba(24,144,255,0.3);
}
.btn-keep-local {
  background-color: #1890ff;
  border-color: #1890ff;
  color: white;
  border-radius: 6px;
}
.btn-keep-local:hover {
  background-color: #40a9ff;
  border-color: #40a9ff;
  color: white;
}
.btn-keep-device {
  background-color: #52c41a;
  border-color: #52c41a;
  color: white;
  border-radius: 6px;
}
.btn-keep-device:hover {
  background-color: #73d13d;
  border-color: #73d13d;
  color: white;
}
.value-local {
  color: #1890ff;
  font-weight: 500;
}
.value-remote {
  color: #52c41a;
  font-weight: 500;
}
.btn-primary {
  box-shadow: 0 2px 8px rgba(24,144,255,0.2);
}
.custom-tabs :deep(.ant-tabs-nav-list) {
  font-weight: 600;
}
</style>
