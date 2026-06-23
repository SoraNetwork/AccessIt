<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { message, Modal } from 'ant-design-vue'
import api from '../services/api'
import type { Device, Person, PersonKind } from '../types'
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
const showCard = ref(false)
const showQr = ref(false)
const selected = ref<Person | null>(null)
const shareUrl = ref('')
const shareId = ref('')
const form = reactive({ name: '', dingTalkUserId: '', mobile: '', beginTime: '', endTime: '', maxOpenDoorTime: 0, deviceIds: [] as string[] })
const cardForm = reactive({ cardNo: '', isVirtual: false })
const qrForm = reactive({ deviceId: '', expireMinutes: 60, maxOpenTimes: 1 })
const editable = computed(() => canManageAccess(auth.role))
const employeeOptions = computed(() => directoryUsers.value.map(user => ({ value: user.dingTalkUserId, label: `${user.name}${user.mobile ? ` · ${user.mobile}` : ''}` })))
const deviceOptions = computed(() => devices.value.filter(device => device.supportsUserInfo).map(device => ({ value: device.id, label: `${device.groupName || '未分组'} · ${device.deviceSerial}` })))
const columns = [
  { title: '人员编号', dataIndex: 'employeeNo' }, { title: '姓名', dataIndex: 'name' }, { title: '类型', dataIndex: 'kind' },
  { title: '来源', key: 'source' }, { title: '有效期', key: 'validity' }, { title: '凭证', key: 'credentials' }, { title: '状态', dataIndex: 'status' },
  { title: 'HIKIoT 团队', key: 'hikiot' }, { title: '操作', key: 'action' }
]

async function load() {
  loading.value = true
  try {
    const [personResult, deviceResult] = await Promise.all([api.get<Person[]>('/people', { params: { kind: kind.value, keyword: keyword.value || undefined } }), api.get<Device[]>('/devices')])
    people.value = personResult.data
    devices.value = deviceResult.data
    directoryUsers.value = editable.value ? (await api.get<DirectoryUser[]>('/directory-users')).data : []
  } finally { loading.value = false }
}
function resetForm() { Object.assign(form, { name: '', dingTalkUserId: '', mobile: '', beginTime: '', endTime: '', maxOpenDoorTime: 0, deviceIds: [] }) }
function create(targetKind: PersonKind) { kind.value = targetKind; resetForm(); showCreate.value = true }
function selectDirectoryUser(userId: string) {
  const user = directoryUsers.value.find(item => item.dingTalkUserId === userId)
  if (user) { form.name = user.name; form.mobile = user.mobile || '' }
}
async function submit() {
  if (!form.name) return message.warning('请填写姓名')
  if (kind.value === 'Visitor' && (!form.beginTime || !form.endTime || !form.deviceIds.length)) return message.warning('请填写访客有效期并选择设备')
  const payload = kind.value === 'Employee'
    ? { name: form.name, dingTalkUserId: form.dingTalkUserId || null, mobile: form.mobile || null, deviceIds: [] }
    : { name: form.name, mobile: form.mobile || null, beginTime: new Date(form.beginTime).toISOString(), endTime: new Date(form.endTime).toISOString(), maxOpenDoorTime: form.maxOpenDoorTime, deviceIds: form.deviceIds }
  await api.post(kind.value === 'Employee' ? '/people/employees' : '/people/visitors', payload)
  message.success(kind.value === 'Employee' ? '员工主档已创建，请明确发布到 HIKIoT 团队后下发设备' : '访客已创建，正在排队下发')
  showCreate.value = false
  await load()
}
async function syncSources() {
  const { data } = await api.post('/people/sync-sources')
  message.success(`同步完成：HIKIoT 新增 ${data.hikiot.created}、更新 ${data.hikiot.updated}；钉钉新增 ${data.dingTalk.created}、更新 ${data.dingTalk.updated}`)
  await load()
}
function openCard(person: Person) { selected.value = person; Object.assign(cardForm, { cardNo: '', isVirtual: person.kind === 'Visitor' }); showCard.value = true }
async function addCard() {
  if (!selected.value || !cardForm.cardNo) return
  await api.post(`/people/${selected.value.id}/cards`, cardForm)
  message.success(selected.value.kind === 'Employee' ? '卡片已保存；下次发布团队时会同步' : '卡片已保存并排队下发')
  showCard.value = false
  await load()
}
async function uploadFace(person: Person, file: File) {
  const data = new FormData(); data.append('file', file)
  await api.post(`/people/${person.id}/face`, data, { headers: { 'Content-Type': 'multipart/form-data' } })
  message.success(person.kind === 'Employee' ? '人脸已保存；下次发布团队时会同步' : '人脸已上传并排队下发')
  await load(); return false
}
async function setPassword(person: Person) {
  const password = window.prompt(`设置 ${person.name} 的 4–6 位门禁密码`) || ''
  if (!password) return
  if (password.length < 4 || password.length > 6) return message.warning('密码必须为 4–6 位')
  await api.put(`/people/${person.id}/password`, { password })
  message.success(person.kind === 'Employee' ? '密码已加密保存；下次发布团队时会下发到设备' : '密码已加密保存并排队下发')
}
function publishToHikiot(person: Person) {
  Modal.confirm({ title: `发布 ${person.name} 到 HIKIoT 团队？`, content: '将创建或更新团队成员，并向全部已纳管、支持人员管理的设备授予默认门禁权限。', onOk: async () => {
    const { data } = await api.post(`/people/${person.id}/hikiot/publish`)
    message.success(`已发布到团队并向 ${data.deviceCount} 台设备排队下发`); await load()
  } })
}
function removeFromHikiot(person: Person) {
  Modal.confirm({ title: `从 HIKIoT 团队移除 ${person.name}？`, content: '此操作只移除团队成员，不会自动删除设备人员。', okType: 'danger', onOk: async () => {
    await api.delete(`/people/${person.id}/hikiot`); message.success('已从 HIKIoT 团队移除'); await load()
  } })
}
function openQr(person: Person) { selected.value = person; qrForm.deviceId = person.deviceGrants[0]?.accessDeviceId || ''; shareUrl.value = ''; shareId.value = ''; showQr.value = true }
async function createQr() { if (!selected.value) return; const { data } = await api.post('/visitor-qr', { visitorId: selected.value.id, ...qrForm }); shareUrl.value = data.shareUrl; shareId.value = data.share.id; message.success('二维码已生成') }
function viewQr() { if (shareUrl.value) window.open(shareUrl.value, '_blank', 'noopener') }
async function notifyHost() { if (!shareId.value) return; await api.post(`/visitor-qr/${shareId.value}/notify-host`); message.success('已发送钉钉工作通知') }
async function revokeQr() { if (!shareId.value) return; await api.post(`/visitor-qr/${shareId.value}/revoke`); message.success('二维码分享已撤销'); shareUrl.value = '' }
function remove(person: Person) {
  Modal.confirm({ title: `删除 ${person.name}`, content: person.kind === 'Visitor' ? '将撤销二维码分享，并按人脸、卡片、人员顺序排队清理设备资料。' : '将排队清理设备资料；不会自动移除 HIKIoT 团队成员。', okType: 'danger', onOk: async () => { await api.delete(`/people/${person.id}`); message.success('删除任务已进入队列'); await load() } })
}
onMounted(load)
</script>

<template>
  <section>
    <a-page-header title="人员管理" sub-title="员工以主档发布到 HIKIoT 团队；访客保留设备直连流程">
      <template #extra><a-space v-if="editable"><a-button @click="syncSources">同步外部目录</a-button><a-button @click="create('Employee')">新增正式员工</a-button><a-button type="primary" @click="create('Visitor')">新增访客</a-button></a-space></template>
    </a-page-header>
    <a-card>
      <a-space class="filters"><a-segmented v-model:value="kind" :options="[{ label: '正式员工', value: 'Employee' }, { label: '访客', value: 'Visitor' }]" @change="load" /><a-input-search v-model:value="keyword" placeholder="姓名或人员编号" allow-clear @search="load" /></a-space>
      <a-table :columns="columns" :data-source="people" :loading="loading" :row-key="(person: Person) => person.id" :scroll="{ x: 1250 }">
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'source'"><a-tag v-if="record.dingTalkUserId" color="blue">钉钉</a-tag><a-tag v-if="record.hikiotPersonNo" color="green">HIKIoT</a-tag><span v-if="!record.dingTalkUserId && !record.hikiotPersonNo">本地</span></template>
          <template v-else-if="column.key === 'validity'"><span v-if="record.permanentValid">永久有效</span><span v-else>{{ new Date(record.enableBeginTime).toLocaleString() }} 至 {{ new Date(record.enableEndTime).toLocaleString() }}</span></template>
          <template v-else-if="column.key === 'credentials'">卡 {{ record.cards.length }} / 本地人脸 {{ record.faceAssets.length }}<span v-if="record.hikiotFaceIdentificationId"> / 团队人脸</span></template>
          <template v-else-if="column.key === 'hikiot'"><span v-if="record.kind === 'Employee' && record.hikiotPersonNo">已发布：{{ record.hikiotPersonNo }}<br><small>部门：{{ record.hikiotDepartmentNo || '-' }}</small><br><small>钉钉：{{ record.dingTalkUserId || '-' }}</small></span><span v-else-if="record.kind === 'Employee'">未发布<br><small>钉钉：{{ record.dingTalkUserId || '-' }}</small></span><span v-else>-</span></template>
          <template v-else-if="column.key === 'action'">
            <a-space v-if="editable" wrap>
              <a-button size="small" @click="openCard(record)">加卡</a-button><a-upload :show-upload-list="false" :before-upload="(file: File) => uploadFace(record, file)"><a-button size="small">人脸</a-button></a-upload><a-button size="small" @click="setPassword(record)">密码</a-button>
              <a-button v-if="record.kind === 'Employee'" size="small" type="primary" @click="publishToHikiot(record)">发布团队</a-button><a-button v-if="record.kind === 'Employee' && record.hikiotPersonNo" size="small" danger @click="removeFromHikiot(record)">移出团队</a-button>
              <a-button v-if="record.kind === 'Visitor'" size="small" @click="openQr(record)">二维码</a-button><a-button danger size="small" @click="remove(record)">删除</a-button>
            </a-space>
          </template>
        </template>
      </a-table>
    </a-card>
    <a-modal v-model:open="showCreate" :title="kind === 'Employee' ? '新增正式员工' : '新增访客'" @ok="submit"><a-form layout="vertical">
      <a-form-item v-if="kind === 'Employee'" label="钉钉通讯录成员（可选）"><a-select v-model:value="form.dingTalkUserId" allow-clear show-search option-filter-prop="label" placeholder="选择已同步的钉钉成员" :options="employeeOptions" @change="selectDirectoryUser" /></a-form-item>
      <a-form-item label="姓名" required><a-input v-model:value="form.name" /></a-form-item><a-form-item label="手机号"><a-input v-model:value="form.mobile" /></a-form-item>
      <template v-if="kind === 'Visitor'"><a-form-item label="开始时间" required><input v-model="form.beginTime" type="datetime-local" class="native-input"></a-form-item><a-form-item label="结束时间" required><input v-model="form.endTime" type="datetime-local" class="native-input"></a-form-item><a-form-item label="最大开门次数（0 为不限）"><a-input-number v-model:value="form.maxOpenDoorTime" :min="0" :max="255" /></a-form-item><a-form-item label="授权设备" required><a-select v-model:value="form.deviceIds" mode="multiple" :options="deviceOptions" /></a-form-item></template>
    </a-form></a-modal>
    <a-modal v-model:open="showCard" title="新增卡片" @ok="addCard"><a-form layout="vertical"><a-form-item label="卡号" required><a-input v-model:value="cardForm.cardNo" /></a-form-item><a-form-item><a-checkbox v-model:checked="cardForm.isVirtual">虚拟卡（仅访客二维码）</a-checkbox></a-form-item></a-form></a-modal>
    <a-modal v-model:open="showQr" title="生成访客二维码" :footer="null"><a-form layout="vertical"><a-form-item label="设备"><a-select v-model:value="qrForm.deviceId" :options="selected?.deviceGrants.map(grant => ({ value: grant.accessDeviceId, label: grant.accessDevice?.deviceSerial || grant.accessDeviceId }))" /></a-form-item><a-form-item label="二维码有效分钟"><a-input-number v-model:value="qrForm.expireMinutes" :min="5" :max="10080" /></a-form-item><a-form-item label="最大开门次数"><a-input-number v-model:value="qrForm.maxOpenTimes" :min="1" :max="255" /></a-form-item><a-space><a-button type="primary" @click="createQr">生成受控分享链接</a-button><a-button v-if="shareUrl" @click="viewQr">查看二维码</a-button><a-button v-if="shareUrl" @click="notifyHost">通知接待人</a-button><a-button v-if="shareUrl" danger @click="revokeQr">撤销</a-button></a-space><a-alert v-if="shareUrl" class="share" type="success" :message="shareUrl" /></a-form></a-modal>
  </section>
</template>

<style scoped>
.filters { margin-bottom: 16px; }
.native-input { box-sizing: border-box; width: 100%; height: 32px; padding: 4px 11px; border: 1px solid #d9d9d9; border-radius: 6px; }
.share { margin-top: 16px; word-break: break-all; }
</style>
