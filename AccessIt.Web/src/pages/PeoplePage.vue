<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { message, Modal } from 'ant-design-vue'
import api from '../services/api'
import type { Device, Person, PersonKind } from '../types'
import { useAuthStore } from '../stores/auth'
import { canManageAccess } from '../utils/permissions'

interface DirectoryUser {
  dingTalkUserId: string
  name: string
  mobile?: string | null
}

const auth = useAuthStore()
const people = ref<Person[]>([])
const devices = ref<Device[]>([])
const directoryUsers = ref<DirectoryUser[]>([])
const kind = ref<PersonKind>('Employee')
const keyword = ref('')
const loading = ref(false)
const showCreate = ref(false)
const showCard = ref(false)
const showQr = ref(false)
const selected = ref<Person | null>(null)
const shareUrl = ref('')
const shareId = ref('')
const form = reactive({ name: '', dingTalkUserId: '', mobile: '', beginTime: '', endTime: '', maxOpenDoorTime: 0, deviceIds: [] as string[] })
const cardForm = reactive({ cardNo: '', isVirtual: false })
const qrForm = reactive({ deviceId: '', expireMinutes: 60, maxOpenTimes: 1 })

const editable = computed(() => canManageAccess(auth.role))
const employeeOptions = computed(() => directoryUsers.value.map(user => ({
  value: user.dingTalkUserId,
  label: `${user.name}${user.mobile ? ` · ${user.mobile}` : ''}`
})))
const deviceOptions = computed(() => devices.value
  .filter(device => device.supportsUserInfo)
  .map(device => ({ value: device.id, label: `${device.groupName || '未分组'} · ${device.deviceSerial}` })))
const columns = [
  { title: '人员编号', dataIndex: 'employeeNo' },
  { title: '姓名', dataIndex: 'name' },
  { title: '类型', dataIndex: 'kind' },
  { title: '有效期', key: 'validity' },
  { title: '凭证', key: 'credentials' },
  { title: '状态', dataIndex: 'status' },
  { title: '操作', key: 'action' }
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
  } finally {
    loading.value = false
  }
}

function resetForm() {
  Object.assign(form, { name: '', dingTalkUserId: '', mobile: '', beginTime: '', endTime: '', maxOpenDoorTime: 0, deviceIds: [] })
}

function create(targetKind: PersonKind) {
  kind.value = targetKind
  resetForm()
  showCreate.value = true
}

function selectDirectoryUser(userId: string) {
  const user = directoryUsers.value.find(item => item.dingTalkUserId === userId)
  if (!user) return
  form.name = user.name
  form.mobile = user.mobile || ''
}

async function submit() {
  if (!form.deviceIds.length) {
    message.warning('请选择至少一台设备')
    return
  }
  if (kind.value === 'Employee' && !form.dingTalkUserId) {
    message.warning('请选择钉钉通讯录成员')
    return
  }
  if (kind.value === 'Visitor' && (!form.name || !form.beginTime || !form.endTime)) {
    message.warning('请填写访客姓名和有效期')
    return
  }

  const payload = kind.value === 'Employee'
    ? { name: form.name, dingTalkUserId: form.dingTalkUserId, mobile: form.mobile || null, deviceIds: form.deviceIds }
    : { name: form.name, mobile: form.mobile || null, beginTime: new Date(form.beginTime).toISOString(), endTime: new Date(form.endTime).toISOString(), maxOpenDoorTime: form.maxOpenDoorTime, deviceIds: form.deviceIds }
  await api.post(kind.value === 'Employee' ? '/people/employees' : '/people/visitors', payload)
  message.success('人员已创建，正在排队下发')
  showCreate.value = false
  await load()
}

function openCard(person: Person) {
  selected.value = person
  Object.assign(cardForm, { cardNo: '', isVirtual: person.kind === 'Visitor' })
  showCard.value = true
}

async function addCard() {
  if (!selected.value || !cardForm.cardNo) return
  await api.post(`/people/${selected.value.id}/cards`, cardForm)
  message.success('卡片已保存并排队下发')
  showCard.value = false
  await load()
}

async function uploadFace(person: Person, file: File) {
  const data = new FormData()
  data.append('file', file)
  await api.post(`/people/${person.id}/face`, data, { headers: { 'Content-Type': 'multipart/form-data' } })
  message.success('人脸已上传并排队下发')
  await load()
  return false
}

async function setPassword(person: Person) {
  const password = window.prompt(`设置 ${person.name} 的 4–6 位门禁密码`) || ''
  if (!password) return
  if (password.length < 4 || password.length > 6) {
    message.warning('密码必须为 4–6 位')
    return
  }
  await api.put(`/people/${person.id}/password`, { password })
  message.success('密码已加密保存并排队下发')
}

function openQr(person: Person) {
  selected.value = person
  qrForm.deviceId = person.deviceGrants[0]?.accessDeviceId || ''
  shareUrl.value = ''
  shareId.value = ''
  showQr.value = true
}

async function createQr() {
  if (!selected.value) return
  const { data } = await api.post('/visitor-qr', { visitorId: selected.value.id, ...qrForm })
  shareUrl.value = data.shareUrl
  shareId.value = data.share.id
  message.success('二维码已生成')
}

function viewQr() {
  if (shareUrl.value) window.open(shareUrl.value, '_blank', 'noopener')
}

async function notifyHost() {
  if (!shareId.value) return
  await api.post(`/visitor-qr/${shareId.value}/notify-host`)
  message.success('已向当前接待人发送钉钉工作通知')
}

async function revokeQr() {
  if (!shareId.value) return
  await api.post(`/visitor-qr/${shareId.value}/revoke`)
  message.success('二维码分享已撤销')
  shareUrl.value = ''
}

function remove(person: Person) {
  Modal.confirm({
    title: `删除 ${person.name}`,
    content: '系统将按人脸、卡片、人员的顺序排队删除设备资料。',
    okType: 'danger',
    onOk: async () => {
      await api.delete(`/people/${person.id}`)
      message.success('删除任务已进入队列')
      await load()
    }
  })
}

onMounted(load)
</script>

<template>
  <section>
    <a-page-header title="人员管理" sub-title="正式员工与访客的单门通行权限">
      <template #extra>
        <a-space v-if="editable">
          <a-button @click="create('Employee')">新增正式员工</a-button>
          <a-button type="primary" @click="create('Visitor')">新增访客</a-button>
        </a-space>
      </template>
    </a-page-header>

    <a-card>
      <a-space class="filters">
        <a-segmented v-model:value="kind" :options="[{ label: '正式员工', value: 'Employee' }, { label: '访客', value: 'Visitor' }]" @change="load" />
        <a-input-search v-model:value="keyword" placeholder="姓名或人员编号" allow-clear @search="load" />
      </a-space>
      <a-table :columns="columns" :data-source="people" :loading="loading" :row-key="(person: Person) => person.id" :scroll="{ x: 900 }">
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'validity'">
            <span v-if="record.permanentValid">永久有效</span>
            <span v-else>{{ new Date(record.enableBeginTime).toLocaleString() }}<br>至 {{ new Date(record.enableEndTime).toLocaleString() }}</span>
          </template>
          <template v-else-if="column.key === 'credentials'">卡 {{ record.cards.length }} / 人脸 {{ record.faceAssets.length }}</template>
          <template v-else-if="column.key === 'action'">
            <a-space v-if="editable" wrap>
              <a-button size="small" @click="openCard(record)">加卡</a-button>
              <a-upload :show-upload-list="false" :before-upload="(file: File) => uploadFace(record, file)">
                <a-button size="small">人脸</a-button>
              </a-upload>
              <a-button size="small" @click="setPassword(record)">密码</a-button>
              <a-button v-if="record.kind === 'Visitor'" size="small" @click="openQr(record)">二维码</a-button>
              <a-button danger size="small" @click="remove(record)">删除</a-button>
            </a-space>
          </template>
        </template>
      </a-table>
    </a-card>

    <a-modal v-model:open="showCreate" :title="kind === 'Employee' ? '新增正式员工' : '新增访客'" @ok="submit">
      <a-form layout="vertical">
        <a-form-item v-if="kind === 'Employee'" label="钉钉通讯录" required>
          <a-select v-model:value="form.dingTalkUserId" show-search option-filter-prop="label" placeholder="选择已同步的钉钉成员" :options="employeeOptions" @change="selectDirectoryUser" />
        </a-form-item>
        <a-form-item label="姓名" required>
          <a-input v-model:value="form.name" :disabled="kind === 'Employee'" />
        </a-form-item>
        <a-form-item label="手机号">
          <a-input v-model:value="form.mobile" :disabled="kind === 'Employee'" />
        </a-form-item>
        <template v-if="kind === 'Visitor'">
          <a-form-item label="开始时间" required><input v-model="form.beginTime" type="datetime-local" class="native-input"></a-form-item>
          <a-form-item label="结束时间" required><input v-model="form.endTime" type="datetime-local" class="native-input"></a-form-item>
          <a-form-item label="最大开门次数（0 为不限）"><a-input-number v-model:value="form.maxOpenDoorTime" :min="0" :max="255" /></a-form-item>
        </template>
        <a-form-item label="授权设备" required><a-select v-model:value="form.deviceIds" mode="multiple" :options="deviceOptions" /></a-form-item>
      </a-form>
    </a-modal>

    <a-modal v-model:open="showCard" title="新增普通卡" @ok="addCard">
      <a-form layout="vertical">
        <a-form-item label="卡号" required><a-input v-model:value="cardForm.cardNo" /></a-form-item>
        <a-form-item><a-checkbox v-model:checked="cardForm.isVirtual">虚拟卡（用于访客二维码）</a-checkbox></a-form-item>
      </a-form>
    </a-modal>

    <a-modal v-model:open="showQr" title="生成访客二维码" :footer="null">
      <a-form layout="vertical">
        <a-form-item label="设备"><a-select v-model:value="qrForm.deviceId" :options="selected?.deviceGrants.map(grant => ({ value: grant.accessDeviceId, label: grant.accessDevice?.deviceSerial || grant.accessDeviceId }))" /></a-form-item>
        <a-form-item label="二维码有效分钟"><a-input-number v-model:value="qrForm.expireMinutes" :min="5" :max="10080" /></a-form-item>
        <a-form-item label="最大开门次数"><a-input-number v-model:value="qrForm.maxOpenTimes" :min="1" :max="255" /></a-form-item>
        <a-space>
          <a-button type="primary" @click="createQr">生成受控分享链接</a-button>
          <a-button v-if="shareUrl" @click="viewQr">查看/下载二维码</a-button>
          <a-button v-if="shareUrl" @click="notifyHost">通知接待人</a-button>
          <a-button v-if="shareUrl" danger @click="revokeQr">撤销</a-button>
        </a-space>
        <a-alert v-if="shareUrl" class="share" type="success" :message="shareUrl" />
      </a-form>
    </a-modal>
  </section>
</template>

<style scoped>
.filters { margin-bottom: 16px; }
.native-input { box-sizing: border-box; width: 100%; height: 32px; padding: 4px 11px; border: 1px solid #d9d9d9; border-radius: 6px; }
.share { margin-top: 16px; word-break: break-all; }
</style>
