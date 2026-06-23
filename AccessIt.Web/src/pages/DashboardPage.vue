<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import { useRouter } from 'vue-router'
import { message } from 'ant-design-vue'
import api from '../services/api'
import type { Device, HikiotIssueBatch, Job } from '../types'

const router = useRouter()
const devices = ref<Device[]>([])
const jobs = ref<Job[]>([])
const authorityBatches = ref<HikiotIssueBatch[]>([])
const connection = ref<{ isAuthorized: boolean; needsReauthorization: boolean; teamNo?: string }>({ isAuthorized: false, needsReauthorization: true })
const loading = ref(false)

async function load() {
  loading.value = true
  try {
    const [deviceResult, jobResult, issueResult, connectionResult] = await Promise.allSettled([
      api.get<Device[]>('/devices'),
      api.get<Job[]>('/jobs', { params: { status: 'Failed' } }),
      api.get<HikiotIssueBatch[]>('/hikiot-issue-batches'),
      api.get('/hikiot/connection')
    ])
    if (deviceResult.status === 'fulfilled') devices.value = deviceResult.value.data
    if (jobResult.status === 'fulfilled') jobs.value = jobResult.value.data
    if (issueResult.status === 'fulfilled') authorityBatches.value = issueResult.value.data
    if (connectionResult.status === 'fulfilled') connection.value = connectionResult.value.data
  } catch (error: any) {
    message.error('无法完整加载工作台数据')
  } finally {
    loading.value = false
  }
}

const pendingBatchesCount = computed(() => {
  return authorityBatches.value.filter(x => x.status === 'Submitted' || x.status === 'Pending').length
})

const failedBatchesCount = computed(() => {
  return authorityBatches.value.filter(x => x.status === 'Failed').length
})

const failedJobsCount = computed(() => {
  return jobs.value.length
})

function navigateTo(path: string) {
  router.push(path)
}

onMounted(load)
</script>

<template>
  <section class="premium-container">
    <a-page-header title="工作台" sub-title="快速概览门禁下发状态、核心指标，并进行高频系统操作" class="glass-header">
      <template #extra>
        <a-button type="primary" class="btn-primary" :loading="loading" @click="load">刷新数据</a-button>
      </template>
    </a-page-header>

    <a-row :gutter="[16, 16]" class="stat-cards-row">
      <!-- Connection Status Card -->
      <a-col :xs="24" :sm="12" :md="6">
        <a-card class="glass-stat-card card-blue" @click="navigateTo('/settings')">
          <div class="stat-label">HIKIoT 终端状态</div>
          <div class="stat-value connection-text">
            {{ connection.isAuthorized ? '已接入' : '待授权' }}
          </div>
          <div class="stat-desc">
            <span v-if="connection.teamNo">团队: {{ connection.teamNo }}</span>
            <span v-else>请在系统设置中绑定</span>
          </div>
        </a-card>
      </a-col>

      <!-- Managed Devices Card -->
      <a-col :xs="24" :sm="12" :md="6">
        <a-card class="glass-stat-card card-cyan" @click="navigateTo('/devices')">
          <div class="stat-label">已纳管设备</div>
          <div class="stat-value">{{ devices.length }} <span class="unit">台</span></div>
          <div class="stat-desc">点击前往设备控制中心</div>
        </a-card>
      </a-col>

      <!-- Pending Issue Batches -->
      <a-col :xs="24" :sm="12" :md="6">
        <a-card class="glass-stat-card card-purple" @click="navigateTo('/operations')">
          <div class="stat-label">处理中授权 (HIKIoT)</div>
          <div class="stat-value">
            {{ pendingBatchesCount }} 
            <span class="unit">批</span>
          </div>
          <div class="stat-desc warning-desc" v-if="failedBatchesCount > 0">
            ⚠ {{ failedBatchesCount }} 个批次异常失败
          </div>
          <div class="stat-desc" v-else>所有已提交批次下发中</div>
        </a-card>
      </a-col>

      <!-- Failed Visitor Jobs -->
      <a-col :xs="24" :sm="12" :md="6">
        <a-card class="glass-stat-card card-red" @click="navigateTo('/operations')">
          <div class="stat-label">访客下发失败任务</div>
          <div class="stat-value error-value">
            {{ failedJobsCount }} 
            <span class="unit">项</span>
          </div>
          <div class="stat-desc error-desc" v-if="failedJobsCount > 0">需要管理员核验与重试</div>
          <div class="stat-desc" v-else>没有挂起的失败任务</div>
        </a-card>
      </a-col>
    </a-row>

    <a-row :gutter="16" class="details-row">
      <!-- Quick Action / Guide -->
      <a-col :xs="24" :lg="16">
        <a-card class="glass-card flex-card" title="门禁系统初始化与配置引导">
          <div class="guide-intro">
            欢迎使用 <strong>开一个门 · AccessIt</strong>。系统采用全新的 HIKIoT 互联架构，员工凭证在本地修改后，将自动通过 HIKIoT 团队接口进行网络同步，不影响本地设备直连和访客流程。
          </div>
          <a-steps direction="vertical" size="small" :current="connection.isAuthorized ? 2 : 0" class="custom-steps">
            <a-step title="配置钉钉与 HIKIoT 基础密钥" description="在【系统设置】内写入钉钉应用、HIKIoT API 的凭证，打通底层连接。" />
            <a-step title="完成 HIKIoT 团队服务授权" description="通过 OAuth2 完成租户级管理员授权，绑定相应的 HIKIoT 团队空间。" />
            <a-step title="同步设备并下发正式员工凭证" description="在【设备管理】同步门禁机，并在【人员管理】创建或导入员工进行下发。" />
          </a-steps>
        </a-card>
      </a-col>

      <!-- System Quick Entrance -->
      <a-col :xs="24" :lg="8">
        <a-card class="glass-card flex-card" title="便捷功能入口">
          <div class="quick-link-list">
            <a-button class="quick-link-btn" block @click="navigateTo('/people')">
              👥 人员与凭证发布
            </a-button>
            <a-button class="quick-link-btn" block @click="navigateTo('/devices')">
              📷 门禁能力集同步
            </a-button>
            <a-button class="quick-link-btn" block @click="navigateTo('/operations')">
              ⚡ 下发队列与冲突处理
            </a-button>
            <a-button class="quick-link-btn" block @click="navigateTo('/audit')">
              🛡 门禁开门审计日志
            </a-button>
          </div>
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
.stat-cards-row {
  margin-bottom: 20px;
}
.glass-stat-card {
  background: rgba(255, 255, 255, 0.65);
  backdrop-filter: blur(10px);
  border-radius: 16px;
  border: 1px solid rgba(255,255,255,0.4);
  box-shadow: 0 8px 32px rgba(31, 38, 135, 0.05);
  cursor: pointer;
  transition: all 0.25s cubic-bezier(0.02, 0.01, 0.47, 1);
}
.glass-stat-card:hover {
  transform: translateY(-4px);
  box-shadow: 0 12px 36px rgba(31, 38, 135, 0.12);
}
.stat-label {
  font-size: 13px;
  color: #8c8c8c;
  font-weight: 500;
  margin-bottom: 8px;
}
.stat-value {
  font-size: 28px;
  font-weight: 700;
  color: #262626;
  line-height: 1.2;
  margin-bottom: 4px;
}
.stat-value .unit {
  font-size: 14px;
  font-weight: 500;
  color: #8c8c8c;
  margin-left: 2px;
}
.stat-desc {
  font-size: 12px;
  color: #8c8c8c;
}
.connection-text {
  color: #1890ff;
}
.error-value {
  color: #ff4d4f;
}
.error-desc {
  color: #ff4d4f;
}
.warning-desc {
  color: #faad14;
}
.card-blue:hover {
  border-left: 4px solid #1890ff;
}
.card-cyan:hover {
  border-left: 4px solid #13c2c2;
}
.card-purple:hover {
  border-left: 4px solid #722ed1;
}
.card-red:hover {
  border-left: 4px solid #ff4d4f;
}
.glass-card {
  background: rgba(255, 255, 255, 0.65);
  backdrop-filter: blur(10px);
  border-radius: 16px;
  border: 1px solid rgba(255,255,255,0.4);
  box-shadow: 0 8px 32px rgba(31, 38, 135, 0.05);
  margin-bottom: 20px;
}
.flex-card {
  height: 100%;
}
.guide-intro {
  color: #595959;
  font-size: 14px;
  line-height: 1.5;
  margin-bottom: 20px;
}
.custom-steps {
  margin-top: 10px;
}
.quick-link-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.quick-link-btn {
  height: 40px;
  font-weight: 500;
  border-radius: 8px;
  text-align: left;
  padding-left: 16px;
  transition: all 0.2s;
  background: rgba(255, 255, 255, 0.8);
}
.quick-link-btn:hover {
  background: #1890ff;
  color: white;
  border-color: #1890ff;
  transform: translateX(2px);
}
.btn-primary {
  box-shadow: 0 2px 8px rgba(24,144,255,0.2);
}
</style>
