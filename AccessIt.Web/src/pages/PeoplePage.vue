<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { message, Modal } from 'ant-design-vue'
import api from '../services/api'
import type { Card, Device, Person, PersonKind, DeviceGrant } from '../types'
import { useAuthStore } from '../stores/auth'
import { canManageAccess } from '../utils/permissions'

interface DirectoryUser { dingTalkUserId: string; name: string; mobile?: string | null }

const auth = useAuthStore()
const people = ref<Person[]>([])
const devices = ref<Device[]>([])
const directoryUsers = ref<DirectoryUser[]>([])
const kind = ref<PersonKind>('Employee')
const keyword = ref('')
const loading = ref(false)
const showCreate = ref(false)
const showEdit = ref(false)
const showCard = ref(false)
const showPassword = ref(false)
const showQr = ref(false)
const selected = ref<Person | null>(null)
const editingCard = ref<Card | null>(null)
const passwordPerson = ref<Person | null>(null)
const shareUrl = ref('')
const shareId = ref('')
const form = reactive({ name: '', dingTalkUserId: '', mobile: '', beginTime: '', endTime: '', maxOpenDoorTime: 0, deviceIds: [] as string[] })
const editForm = reactive({ name: '', dingTalkUserId: '', mobile: '', beginTime: '', endTime: '', maxOpenDoorTime: 0, deviceIds: [] as string[] })
const cardForm = reactive({ cardNo: '', isVirtual: false })
const passwordForm = reactive({ password: '', confirmPassword: '' })
const qrForm = reactive({ deviceId: '', expireMinutes: 60, maxOpenTimes: 1 })
const editable = computed(() => canManageAccess(auth.role))
const employeeOptions = computed(() => directoryUsers.value.map(user => ({ value: user.dingTalkUserId, label: `${user.name}${user.mobile ? ` · ${user.mobile}` : ''}` })))
const deviceOptions = computed(() => devices.value.filter(device => device.supportsUserInfo).map(device => ({ value: device.id, label: `${device.groupName || '未分组'} · ${device.deviceSerial}` })))
const cardModalTitle = computed(() => editingCard.value ? '编辑卡片' : '新增卡片')
const pagination = { pageSize: 10, showSizeChanger: true, pageSizeOptions: ['10', '20', '50', '100'], showQuickJumper: true, showTotal: (total: number) => `共 ${total} 人` }

const columns = [
  { title: '人员编号', dataIndex: 'employeeNo' },
  { title: '姓名', dataIndex: 'name' },
  { title: '类型', dataIndex: 'kind' },
  { title: '来源', key: 'source', responsive: ['lg'] },
  { title: '有效期', key: 'validity', responsive: ['md'] },
  { title: '凭证', key: 'credentials', responsive: ['md'] },
  { title: '状态', dataIndex: 'status' },
  { title: 'HIKIoT 团队', key: 'hikiot', responsive: ['md'] },
  { title: '操作', key: 'action' }
]

const grantColumns = [
  { title: '设备分组 / 序列号', key: 'device' },
  { title: '授权状态', key: 'status' },
  { title: '下发阶段 (HIKIoT)', key: 'hikiotStatus' },
  { title: '批次号', key: 'batchNo' },
  { title: '异常说明', key: 'reason' }
]

async function load() {
  loading.value = true
  try {
    const [personResult, deviceResult] = await Promise.all([
      api.get<Person[]>('/people', { params: { kind: kind.value, keyword: keyword.value || undefined } }),
      api.get<Device[]>('/devices')
    ])
    people.value = personResult.data
    devices.value = deviceResult.data
    directoryUsers.value = editable.value ? (await api.get<DirectoryUser[]>('/directory-users')).data : []
  } catch (error: any) {
    message.error(error?.response?.data?.message || '数据加载失败')
  } finally {
    loading.value = false
  }
}

function resetForm() { Object.assign(form, { name: '', dingTalkUserId: '', mobile: '', beginTime: '', endTime: '', maxOpenDoorTime: 0, deviceIds: [] }) }

function create(targetKind: PersonKind) { kind.value = targetKind; resetForm(); showCreate.value = true }

function fillFromDirectory(target: typeof form | typeof editForm, userId: string) {
  const user = directoryUsers.value.find(item => item.dingTalkUserId === userId)
  if (user) { target.name = user.name; target.mobile = user.mobile || '' }
}

function toDateInput(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '' : new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
}

async function submit() {
  if (!form.name) return message.warning('请填写姓名')
  if (kind.value === 'Visitor' && (!form.beginTime || !form.endTime || !form.deviceIds.length)) return message.warning('请填写访客有效期并选择设备')
  if (kind.value === 'Employee' && !/^1[3-9]\d{9}$/.test(form.mobile.trim())) return message.warning('新增员工并自动发布到 HIKIoT 团队需要有效的 11 位中国大陆手机号')
  
  try {
    const payload = kind.value === 'Employee'
      ? { name: form.name, dingTalkUserId: form.dingTalkUserId || null, mobile: form.mobile || null, deviceIds: [] }
      : { name: form.name, mobile: form.mobile || null, beginTime: new Date(form.beginTime).toISOString(), endTime: new Date(form.endTime).toISOString(), maxOpenDoorTime: form.maxOpenDoorTime, deviceIds: form.deviceIds }
    await api.post(kind.value === 'Employee' ? '/people/employees' : '/people/visitors', payload, { loadingText: kind.value === 'Employee' ? '正在创建团队成员、配置权限并核验下发状态…' : '正在创建访客并下发设备…' })
    message.success(kind.value === 'Employee' ? '员工已发布到 HIKIoT 团队，并已提交标准门禁权限下发' : '访客已创建并已自动下发到授权设备')
    showCreate.value = false
    await load()
  } catch (error: any) {
    message.error(error?.response?.data?.message || '保存失败')
  }
}

function openEdit(person: Person) {
  selected.value = person
  Object.assign(editForm, { name: person.name, dingTalkUserId: person.dingTalkUserId || '', mobile: person.mobile || '', beginTime: toDateInput(person.enableBeginTime), endTime: toDateInput(person.enableEndTime), maxOpenDoorTime: person.maxOpenDoorTime, deviceIds: person.deviceGrants.filter(x => x.isActive).map(x => x.accessDeviceId) })
  showEdit.value = true
}

async function saveEdit() {
  if (!selected.value || !editForm.name) return message.warning('请填写姓名')
  try {
    if (selected.value.kind === 'Employee') {
      await api.put(`/people/${selected.value.id}/employee`, { name: editForm.name, dingTalkUserId: editForm.dingTalkUserId || null, mobile: editForm.mobile || null }, { loadingText: '正在更新团队资料、配置权限并核验下发状态…' })
    } else {
      if (!editForm.beginTime || !editForm.endTime || !editForm.deviceIds.length) return message.warning('请填写访客有效期并选择设备')
      await api.put(`/people/${selected.value.id}/visitor`, { beginTime: new Date(editForm.beginTime).toISOString(), endTime: new Date(editForm.endTime).toISOString(), maxOpenDoorTime: editForm.maxOpenDoorTime, mobile: editForm.mobile || null, deviceIds: editForm.deviceIds }, { loadingText: '正在更新访客并自动下发…' })
    }
    message.success(selected.value.kind === 'Employee' ? '人员资料已保存，HIKIoT 标准权限已自动更新并下发' : '人员资料已保存并自动下发')
    showEdit.value = false
    await load()
  } catch (error: any) {
    message.error(error?.response?.data?.message || '修改保存失败')
  }
}

async function syncSources() {
  try {
    const { data } = await api.post('/people/sync-sources', undefined, { loadingText: '正在同步钉钉与 HIKIoT 团队目录，人数较多时可能需要几分钟…' })
    message.success(`同步完成：HIKIoT 新增 ${data.hikiot.created}、更新 ${data.hikiot.updated}；钉钉新增 ${data.dingTalk.created}、更新 ${data.dingTalk.updated}`)
    await load()
  } catch (error: any) {
    message.error(error?.response?.data?.message || '目录同步失败')
  }
}

function openCard(person: Person) { selected.value = person; editingCard.value = null; Object.assign(cardForm, { cardNo: '', isVirtual: person.kind === 'Visitor' }); showCard.value = true }

function openEditCard(person: Person, card: Card) { selected.value = person; editingCard.value = card; Object.assign(cardForm, { cardNo: card.cardNo, isVirtual: card.isVirtual }); showCard.value = true }

async function saveCard() {
  if (!selected.value || !cardForm.cardNo) return message.warning('请输入卡号')
  const card = editingCard.value
  const employee = selected.value.kind === 'Employee'
  try {
    if (card) await api.put(`/people/${selected.value.id}/cards/${card.id}`, cardForm, { loadingText: employee ? '正在替换团队卡片并核验权限下发状态…' : '正在安全替换访客卡片…' })
    else await api.post(`/people/${selected.value.id}/cards`, cardForm, { loadingText: employee ? '正在发布团队卡片并核验权限下发状态…' : '正在保存访客卡片…' })
    message.success(employee ? (card ? '团队卡片已替换，标准权限已自动更新' : '团队卡片已发布，标准权限已自动更新') : (card ? '卡片已更新并自动下发' : '卡片已保存并自动下发'))
    showCard.value = false
    await load()
  } catch (error: any) {
    message.error(error?.response?.data?.message || '卡片保存失败')
  }
}

async function uploadFace(person: Person, file: File) {
  try {
    const data = new FormData(); data.append('file', file)
    await api.post(`/people/${person.id}/face`, data, { headers: { 'Content-Type': 'multipart/form-data' }, loadingText: person.kind === 'Employee' ? '正在上传人脸、更新团队凭证并核验下发状态…' : '正在上传并下发访客人脸…' })
    message.success(person.kind === 'Employee' ? '人脸已发布到团队，标准权限已自动更新' : '人脸已保存并自动下发')
    await load()
  } catch (error: any) {
    message.error(error?.response?.data?.message || '人脸上传失败')
  }
  return false
}

function openPassword(person: Person) { passwordPerson.value = person; Object.assign(passwordForm, { password: '', confirmPassword: '' }); showPassword.value = true }

async function savePassword() {
  if (!passwordPerson.value) return
  if (passwordForm.password.length < 4 || passwordForm.password.length > 8) return message.warning('密码必须为 4–8 位')
  if (passwordForm.password !== passwordForm.confirmPassword) return message.warning('两次输入的密码不一致')
  try {
    await api.put(`/people/${passwordPerson.value.id}/password`, { password: passwordForm.password }, { loadingText: '正在加密保存密码并下发到支持纯密码的设备…' })
    message.success('密码已加密保存，并已下发到支持纯密码认证的设备')
    showPassword.value = false
  } catch (error: any) {
    message.error(error?.response?.data?.message || '密码设置失败')
  }
}

function publishToHikiot(person: Person) {
  const existsInTeam = Boolean(person.hikiotPersonNo)
  Modal.confirm({ title: existsInTeam ? `重新发布 ${person.name} 的权限？` : `加入 HIKIoT 团队并下发设备？`, content: existsInTeam ? '该人员已在 HIKIoT 团队内，不会重复加入；将重新发布团队资料、凭证与 AccessIt 管理的标准权限。' : '将创建团队成员，并向全部已纳管、支持人员管理的设备授予默认门禁权限。', onOk: async () => {
    try {
      const { data } = await api.post(`/people/${person.id}/hikiot/publish`, undefined, { loadingText: '正在发布团队成员、配置权限并核验下发状态…' })
      const issue = data.authorityIssue
      if (issue?.failures?.length) message.warning(`已提交 ${data.deviceCount} 台设备，但有 ${issue.failures.length} 项需要处理：${issue.failures[0].message}`)
      else message.success(issue?.pendingCount ? `已提交到 ${data.deviceCount} 台设备，${issue.pendingCount} 台仍在海康处理中` : `已完成 ${issue?.confirmedCount ?? data.deviceCount} 台设备的权限核验`)
      await load()
    } catch (error: any) {
      message.error(error?.response?.data?.message || '发布至HIKIoT失败')
    }
  } })
}

function removeFromHikiot(person: Person) {
  Modal.confirm({ title: `从 HIKIoT 团队移除 ${person.name}？`, content: '将先撤销 AccessIt 管理的设备权限并确认，再移除团队成员；海康管理员自行维护的例外权限不会被覆盖。', okType: 'danger', onOk: async () => {
    try {
      await api.delete(`/people/${person.id}/hikiot`, { loadingText: '正在撤销设备权限并移除团队成员…' });
      message.success('设备权限已撤销，人员已从 HIKIoT 团队移除');
      await load()
    } catch (error: any) {
      message.error(error?.response?.data?.message || '移出HIKIoT失败')
    }
  } })
}

function openQr(person: Person) { selected.value = person; qrForm.deviceId = person.deviceGrants[0]?.accessDeviceId || ''; shareUrl.value = ''; shareId.value = ''; showQr.value = true }

async function createQr() {
  if (!selected.value) return
  try {
    const { data } = await api.post('/visitor-qr', { visitorId: selected.value.id, ...qrForm }, { loadingText: '正在准备虚拟卡并生成访客二维码…' })
    shareUrl.value = data.shareUrl
    shareId.value = data.share.id
    message.success('二维码已生成')
  } catch (error: any) {
    message.error(error?.response?.data?.message || '二维码生成失败，请检查设备状态后重试')
  }
}

function viewQr() { if (shareUrl.value) window.open(shareUrl.value, '_blank', 'noopener') }

async function notifyHost() {
  if (!shareId.value) return;
  try {
    await api.post(`/visitor-qr/${shareId.value}/notify-host`, undefined, { loadingText: '正在发送钉钉通知…' });
    message.success('已发送钉钉工作通知')
  } catch (error: any) {
    message.error(error?.response?.data?.message || '通知发送失败')
  }
}

async function revokeQr() {
  if (!shareId.value) return;
  try {
    await api.post(`/visitor-qr/${shareId.value}/revoke`, undefined, { loadingText: '正在撤销二维码分享…' });
    message.success('二维码分享已撤销');
    shareUrl.value = ''
  } catch (error: any) {
    message.error(error?.response?.data?.message || '撤销二维码失败')
  }
}

function remove(person: Person) {
  Modal.confirm({ title: `删除 ${person.name}`, content: person.kind === 'Visitor' ? '将撤销二维码分享，并按人脸、卡片、人员顺序自动清理设备资料。' : '将撤销 AccessIt 管理的标准设备权限并执行设备凭证清理；确认后再移除海康通讯录成员。', okType: 'danger', onOk: async () => {
    try {
      await api.delete(`/people/${person.id}`, { loadingText: '正在撤销权限并清理设备资料…' });
      message.success('权限清理已完成或已记录失败状态');
      await load()
    } catch (error: any) {
      message.error(error?.response?.data?.message || '删除失败')
    }
  } })
}

onMounted(load)
</script>

<template>
  <section class="premium-container">
    <a-page-header title="人员管理" sub-title="员工变更自动发布到 HIKIoT 团队并走标准权限下发；访客保留设备直连二维码流程" class="glass-header">
      <template #extra>
        <a-space v-if="editable" class="page-actions" wrap>
          <a-button class="btn-secondary" @click="syncSources">同步外部目录</a-button>
          <a-button class="btn-secondary" @click="create('Employee')">新增正式员工</a-button>
          <a-button type="primary" class="btn-primary" @click="create('Visitor')">新增访客</a-button>
        </a-space>
      </template>
    </a-page-header>
    
    <a-card class="glass-card">
      <div class="card-filters-wrapper">
        <a-space class="filters" wrap>
          <a-segmented v-model:value="kind" :options="[{ label: '正式员工', value: 'Employee' }, { label: '访客', value: 'Visitor' }]" @change="load" class="custom-segmented" />
          <a-input-search v-model:value="keyword" class="search-input" placeholder="输入姓名或编号搜索..." allow-clear @search="load" />
        </a-space>
      </div>

      <a-table 
        :columns="columns" 
        :data-source="people" 
        :loading="loading" 
        :pagination="pagination" 
        :row-key="(person: Person) => person.id" 
        :scroll="{ x: 980 }" 
        size="middle"
        class="custom-table"
      >
        <!-- Expanded Row Render for device grants status -->
        <template #expandedRowRender="{ record }">
          <div class="nested-table-container">
            <h4 class="nested-title">设备授权与下发链路状态</h4>
            <a-table 
              :columns="grantColumns" 
              :data-source="record.deviceGrants" 
              :pagination="false" 
              size="small" 
              :row-key="(g: DeviceGrant) => g.accessDeviceId"
              class="nested-table"
            >
              <template #bodyCell="{ column: gCol, record: gRec }">
                <template v-if="gCol.key === 'device'">
                  <div class="device-cell">
                    <span class="device-group">{{ gRec.accessDevice?.groupName || '默认分组' }}</span>
                    <span class="device-serial">{{ gRec.accessDevice?.deviceSerial }}</span>
                  </div>
                </template>
                <template v-else-if="gCol.key === 'status'">
                  <a-badge :status="gRec.isActive ? 'success' : 'default'" :text="gRec.isActive ? '激活授权' : '禁用/未授权'" />
                </template>
                <template v-else-if="gCol.key === 'hikiotStatus'">
                  <a-space wrap>
                    <a-tag v-if="gRec.hikiotIsSending" color="processing">下发中...</a-tag>
                    <a-tag v-else-if="gRec.hikiotInfoStatus === 2" color="success">下发成功</a-tag>
                    <a-tag v-else-if="gRec.hikiotInfoStatus === 3" color="warning">待删除/已注销</a-tag>
                    <a-tag v-else-if="gRec.hikiotInfoStatus !== null && gRec.hikiotInfoStatus !== undefined" color="default">状态码: {{ gRec.hikiotInfoStatus }}</a-tag>
                    <a-tag v-else color="default">未发布到该设备</a-tag>
                    <a-tag v-if="gRec.hikiotIsSupported === false" color="error">凭证不支持</a-tag>
                  </a-space>
                </template>
                <template v-else-if="gCol.key === 'batchNo'">
                  <span class="batch-no-text" v-if="gRec.hikiotIssueBatchNo">{{ gRec.hikiotIssueBatchNo }}</span>
                  <span class="muted-text" v-else>-</span>
                </template>
                <template v-else-if="gCol.key === 'reason'">
                  <span class="failed-reason" v-if="gRec.hikiotLastFailedReason">{{ gRec.hikiotLastFailedReason }}</span>
                  <span class="success-reason" v-else>正常</span>
                </template>
              </template>
            </a-table>
          </div>
        </template>

        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'source'">
            <a-space>
              <a-tag v-if="record.dingTalkUserId" color="blue" class="glow-tag">钉钉同步</a-tag>
              <a-tag v-if="record.hikiotPersonNo" color="green" class="glow-tag">HIKIoT</a-tag>
              <span v-if="!record.dingTalkUserId && !record.hikiotPersonNo" class="muted-text">本地录入</span>
            </a-space>
          </template>
          <template v-else-if="column.key === 'validity'">
            <span v-if="record.permanentValid" class="validity-badge permanent">永久有效</span>
            <span v-else class="validity-badge temporary">
              {{ new Date(record.enableBeginTime).toLocaleDateString() }} 至 {{ new Date(record.enableEndTime).toLocaleDateString() }}
            </span>
          </template>
          <template v-else-if="column.key === 'credentials'">
            <div class="credentials-badge-container">
              <span>卡片 <strong>{{ record.cards.length }}</strong> / 人脸 <strong>{{ record.faceAssets.length }}</strong></span>
              <div v-if="record.cards.length" class="credential-list">
                <a-button v-for="card in record.cards" :key="card.id" type="link" size="small" class="card-btn" @click="openEditCard(record, card)">
                  💳 {{ card.cardNo }}
                </a-button>
              </div>
            </div>
          </template>
          <template v-else-if="column.key === 'hikiot'">
            <div class="hikiot-info" v-if="record.kind === 'Employee' && record.hikiotPersonNo">
              <span class="hikiot-no">ID: {{ record.hikiotPersonNo }}</span>
              <span class="hikiot-sub">部门: {{ record.hikiotDepartmentNo || '-' }}</span>
              <span class="hikiot-sub">职务: {{ record.hikiotJobPosition || '-' }}</span>
            </div>
            <span v-else-if="record.kind === 'Employee'" class="warning-text">未录入海康</span>
            <span v-else class="muted-text">-</span>
          </template>
          <template v-else-if="column.key === 'action'">
            <a-space v-if="editable" class="actions" wrap>
              <a-button size="small" class="action-btn" @click="openEdit(record)">编辑</a-button>
              <a-button size="small" class="action-btn" @click="openCard(record)">加卡</a-button>
              <a-upload :show-upload-list="false" :before-upload="(file: File) => uploadFace(record, file)">
                <a-button size="small" class="action-btn">上传人脸</a-button>
              </a-upload>
              <a-button size="small" class="action-btn" @click="openPassword(record)">纯密码</a-button>
              <a-button v-if="record.kind === 'Employee'" size="small" type="primary" class="action-btn publish-btn" @click="publishToHikiot(record)">
                {{ record.hikiotPersonNo ? '重新下发' : '加入团队' }}
              </a-button>
              <a-button v-if="record.kind === 'Employee' && record.hikiotPersonNo" size="small" danger class="action-btn" @click="removeFromHikiot(record)">
                注销
              </a-button>
              <a-button v-if="record.kind === 'Visitor'" size="small" class="action-btn qr-btn" @click="openQr(record)">
                二维码
              </a-button>
              <a-button danger size="small" class="action-btn delete-btn" @click="remove(record)">删除</a-button>
            </a-space>
          </template>
        </template>
      </a-table>
    </a-card>

    <!-- Create Modal -->
    <a-modal v-model:open="showCreate" :title="kind === 'Employee' ? '新增正式员工' : '新增访客'" @ok="submit" class="premium-modal">
      <a-form layout="vertical">
        <a-form-item v-if="kind === 'Employee'" label="关联钉钉通讯录（可选）">
          <a-select v-model:value="form.dingTalkUserId" allow-clear show-search option-filter-prop="label" placeholder="搜索钉钉账户以自动填充数据" :options="employeeOptions" @change="(id: string) => fillFromDirectory(form, id)" />
        </a-form-item>
        <a-form-item label="姓名" required>
          <a-input v-model:value="form.name" placeholder="请输入姓名" />
        </a-form-item>
        <a-form-item label="手机号" required>
          <a-input v-model:value="form.mobile" placeholder="用于生成海康团队成员凭证" />
        </a-form-item>
        <template v-if="kind === 'Visitor'">
          <a-form-item label="启用时间" required>
            <input v-model="form.beginTime" type="datetime-local" class="native-input">
          </a-form-item>
          <a-form-item label="停用时间" required>
            <input v-model="form.endTime" type="datetime-local" class="native-input">
          </a-form-item>
          <a-form-item label="允许验证开门次数 (0表示不限)">
            <a-input-number v-model:value="form.maxOpenDoorTime" :min="0" :max="255" style="width: 100%;" />
          </a-form-item>
          <a-form-item label="授权下发设备" required>
            <a-select v-model:value="form.deviceIds" mode="multiple" placeholder="请选择访客可以通行的门禁设备" :options="deviceOptions" />
          </a-form-item>
        </template>
      </a-form>
    </a-modal>

    <!-- Edit Modal -->
    <a-modal v-model:open="showEdit" :title="selected?.kind === 'Employee' ? '编辑员工资料' : '编辑访客资料'" @ok="saveEdit" class="premium-modal">
      <a-form layout="vertical">
        <a-form-item v-if="selected?.kind === 'Employee'" label="关联钉钉通讯录">
          <a-select v-model:value="editForm.dingTalkUserId" allow-clear show-search option-filter-prop="label" :options="employeeOptions" @change="(id: string) => fillFromDirectory(editForm, id)" />
        </a-form-item>
        <a-form-item label="姓名" required>
          <a-input v-model:value="editForm.name" :disabled="selected?.kind === 'Visitor'" />
        </a-form-item>
        <a-form-item label="手机号">
          <a-input v-model:value="editForm.mobile" />
        </a-form-item>
        <template v-if="selected?.kind === 'Visitor'">
          <a-form-item label="启用时间" required>
            <input v-model="editForm.beginTime" type="datetime-local" class="native-input">
          </a-form-item>
          <a-form-item label="停用时间" required>
            <input v-model="editForm.endTime" type="datetime-local" class="native-input">
          </a-form-item>
          <a-form-item label="允许开门次数">
            <a-input-number v-model:value="editForm.maxOpenDoorTime" :min="0" :max="255" style="width: 100%;" />
          </a-form-item>
          <a-form-item label="授权门禁设备" required>
            <a-select v-model:value="editForm.deviceIds" mode="multiple" :options="deviceOptions" />
          </a-form-item>
        </template>
        <a-alert v-if="selected?.kind === 'Employee'" type="info" show-icon message="钉钉/HIKIoT同步时，本系统的本地修改会被覆盖为外部组织架构数据。" />
      </a-form>
    </a-modal>

    <!-- Card Modal -->
    <a-modal v-model:open="showCard" :title="cardModalTitle" @ok="saveCard" class="premium-modal">
      <a-form layout="vertical">
        <a-form-item label="物理卡号 / 虚拟介质卡号" required>
          <a-input v-model:value="cardForm.cardNo" placeholder="请输入卡片背面的物理ID" autofocus />
        </a-form-item>
        <a-form-item>
          <a-checkbox v-model:checked="cardForm.isVirtual">声明为虚拟卡（专为访客动态二维码设计的标识）</a-checkbox>
        </a-form-item>
        <a-alert v-if="editingCard" type="info" show-icon :message="selected?.kind === 'Employee' ? '修改员工卡号将删除海康侧旧标识，重新激活新卡的标准门禁授权。' : '修改访客卡号将触发门禁机的卡数据覆盖下发机制。'" />
      </a-form>
    </a-modal>

    <!-- Password Modal -->
    <a-modal v-model:open="showPassword" title="设置设备门禁密码" @ok="savePassword" class="premium-modal">
      <a-form layout="vertical">
        <a-alert type="warning" show-icon message="纯数字密码：长度必须在 4 - 8 位之间。密码直连写入设备以独立运行，不占用海康团队凭证。" style="margin-bottom: 16px;" />
        <a-form-item label="新门禁密码" required>
          <a-input-password v-model:value="passwordForm.password" inputmode="numeric" maxlength="8" placeholder="请输入 4 - 8 位数字密码" />
        </a-form-item>
        <a-form-item label="确认新密码" required>
          <a-input-password v-model:value="passwordForm.confirmPassword" inputmode="numeric" maxlength="8" placeholder="请再次输入新密码" />
        </a-form-item>
      </a-form>
    </a-modal>

    <!-- Qr Modal -->
    <a-modal v-model:open="showQr" title="生成设备级访客二维码" :footer="null" class="premium-modal">
      <a-form layout="vertical">
        <a-form-item label="分发设备">
          <a-select v-model:value="qrForm.deviceId" :options="selected?.deviceGrants.map(grant => ({ value: grant.accessDeviceId, label: grant.accessDevice?.deviceSerial || grant.accessDeviceId }))" />
        </a-form-item>
        <a-form-item label="二维码有效时间(分钟)">
          <a-input-number v-model:value="qrForm.expireMinutes" :min="5" :max="10080" style="width: 100%;" />
        </a-form-item>
        <a-form-item label="最多次数">
          <a-input-number v-model:value="qrForm.maxOpenTimes" :min="1" :max="255" style="width: 100%;" />
        </a-form-item>
        <a-space wrap class="qr-action-space">
          <a-button type="primary" @click="createQr">构建下发二维码</a-button>
          <a-button v-if="shareUrl" class="btn-success" @click="viewQr">浏览器预览</a-button>
          <a-button v-if="shareUrl" class="btn-info" @click="notifyHost">发送钉钉消息给接待人</a-button>
          <a-button v-if="shareUrl" danger @click="revokeQr">撤销分享</a-button>
        </a-space>
        <a-alert v-if="shareUrl" class="share" type="success" :message="`分享凭证 URL: ${shareUrl}`" />
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
.card-filters-wrapper {
  margin-bottom: 20px;
}
.custom-segmented {
  background: rgba(0, 0, 0, 0.05);
  border-radius: 8px;
  padding: 2px;
}
.search-input {
  width: 320px;
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
.validity-badge {
  font-size: 12px;
  padding: 4px 8px;
  border-radius: 4px;
  display: inline-block;
}
.validity-badge.permanent {
  background-color: #e6f7ff;
  color: #1890ff;
  border: 1px solid #91d5ff;
}
.validity-badge.temporary {
  background-color: #fff7e6;
  color: #fa8c16;
  border: 1px solid #ffd591;
}
.credentials-badge-container {
  font-size: 13px;
}
.credential-list {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  margin-top: 6px;
}
.card-btn {
  padding: 0 4px;
  background: rgba(24, 144, 255, 0.08);
  border-radius: 4px;
  height: auto;
  font-size: 12px;
}
.hikiot-info {
  display: flex;
  flex-direction: column;
  line-height: 1.3;
}
.hikiot-no {
  font-weight: 500;
  color: #262626;
}
.hikiot-sub {
  font-size: 11px;
  color: #8c8c8c;
}
.warning-text {
  color: #faad14;
}
.muted-text {
  color: #bfbfbf;
}
.actions {
  min-width: 260px;
}
.action-btn {
  border-radius: 6px;
  transition: all 0.2s;
}
.action-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 2px 6px rgba(0,0,0,0.1);
}
.publish-btn {
  background-color: #52c41a;
  border-color: #52c41a;
}
.publish-btn:hover, .publish-btn:focus {
  background-color: #73d13d;
  border-color: #73d13d;
}
.qr-btn {
  color: #722ed1;
  border-color: #722ed1;
  background-color: rgba(114, 46, 209, 0.05);
}
.qr-btn:hover {
  color: #9254de;
  border-color: #9254de;
}
.delete-btn:hover {
  background-color: #ff4d4f;
  color: white;
}
.native-input {
  box-sizing: border-box;
  width: 100%;
  height: 36px;
  padding: 4px 12px;
  border: 1px solid #d9d9d9;
  border-radius: 6px;
  background: white;
  transition: border-color 0.2s;
}
.native-input:focus {
  border-color: #40a9ff;
  outline: none;
  box-shadow: 0 0 0 2px rgba(24, 144, 255, 0.2);
}
.nested-table-container {
  padding: 12px 24px;
  background: rgba(255, 255, 255, 0.4);
  border-radius: 12px;
  border: 1px solid rgba(0,0,0,0.05);
}
.nested-title {
  margin-bottom: 8px;
  font-weight: 600;
  color: #434343;
}
.nested-table {
  background: white;
  border-radius: 8px;
  overflow: hidden;
  box-shadow: 0 2px 8px rgba(0,0,0,0.02);
}
.device-cell {
  display: flex;
  flex-direction: column;
}
.device-group {
  font-size: 11px;
  color: #8c8c8c;
}
.device-serial {
  font-weight: 500;
  color: #262626;
}
.batch-no-text {
  font-family: monospace;
  font-size: 11px;
  background: #f5f5f5;
  padding: 2px 6px;
  border-radius: 4px;
}
.failed-reason {
  color: #ff4d4f;
  font-size: 12px;
}
.success-reason {
  color: #52c41a;
  font-size: 12px;
}
.qr-action-space {
  margin-top: 16px;
}
.share {
  margin-top: 16px;
  word-break: break-all;
}
.btn-secondary {
  background: transparent;
  border-color: #d9d9d9;
}
.btn-primary {
  box-shadow: 0 2px 8px rgba(24,144,255,0.2);
}
.btn-success {
  background-color: #52c41a;
  border-color: #52c41a;
  color: white;
}
.btn-info {
  background-color: #13c2c2;
  border-color: #13c2c2;
  color: white;
}
</style>
