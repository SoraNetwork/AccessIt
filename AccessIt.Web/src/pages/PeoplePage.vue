<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { message, Modal } from 'ant-design-vue'
import api from '../services/api'
import type { Card, Device, Person, PersonKind } from '../types'
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
  { title: '人员编号', dataIndex: 'employeeNo' }, { title: '姓名', dataIndex: 'name' }, { title: '类型', dataIndex: 'kind' },
  { title: '来源', key: 'source', responsive: ['lg'] }, { title: '有效期', key: 'validity', responsive: ['md'] }, { title: '凭证', key: 'credentials', responsive: ['md'] },
  { title: '状态', dataIndex: 'status' }, { title: 'HIKIoT 团队', key: 'hikiot', responsive: ['md'] }, { title: '操作', key: 'action' }
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
  } finally { loading.value = false }
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
  const payload = kind.value === 'Employee'
    ? { name: form.name, dingTalkUserId: form.dingTalkUserId || null, mobile: form.mobile || null, deviceIds: [] }
    : { name: form.name, mobile: form.mobile || null, beginTime: new Date(form.beginTime).toISOString(), endTime: new Date(form.endTime).toISOString(), maxOpenDoorTime: form.maxOpenDoorTime, deviceIds: form.deviceIds }
  await api.post(kind.value === 'Employee' ? '/people/employees' : '/people/visitors', payload, { loadingText: kind.value === 'Employee' ? '正在创建团队成员、配置权限并核验下发状态…' : '正在创建访客并下发设备…' })
  message.success(kind.value === 'Employee' ? '员工已发布到 HIKIoT 团队，并已提交标准门禁权限下发' : '访客已创建并已自动下发到授权设备')
  showCreate.value = false
  await load()
}
function openEdit(person: Person) {
  selected.value = person
  Object.assign(editForm, { name: person.name, dingTalkUserId: person.dingTalkUserId || '', mobile: person.mobile || '', beginTime: toDateInput(person.enableBeginTime), endTime: toDateInput(person.enableEndTime), maxOpenDoorTime: person.maxOpenDoorTime, deviceIds: person.deviceGrants.filter(x => x.isActive).map(x => x.accessDeviceId) })
  showEdit.value = true
}
async function saveEdit() {
  if (!selected.value || !editForm.name) return message.warning('请填写姓名')
  if (selected.value.kind === 'Employee') {
    await api.put(`/people/${selected.value.id}/employee`, { name: editForm.name, dingTalkUserId: editForm.dingTalkUserId || null, mobile: editForm.mobile || null }, { loadingText: '正在更新团队资料、配置权限并核验下发状态…' })
  } else {
    if (!editForm.beginTime || !editForm.endTime || !editForm.deviceIds.length) return message.warning('请填写访客有效期并选择设备')
    await api.put(`/people/${selected.value.id}/visitor`, { beginTime: new Date(editForm.beginTime).toISOString(), endTime: new Date(editForm.endTime).toISOString(), maxOpenDoorTime: editForm.maxOpenDoorTime, mobile: editForm.mobile || null, deviceIds: editForm.deviceIds }, { loadingText: '正在更新访客并自动下发…' })
  }
  message.success(selected.value.kind === 'Employee' ? '人员资料已保存，HIKIoT 标准权限已自动更新并下发' : '人员资料已保存并自动下发')
  showEdit.value = false
  await load()
}
async function syncSources() {
  const { data } = await api.post('/people/sync-sources', undefined, { loadingText: '正在同步钉钉与 HIKIoT 团队目录，人数较多时可能需要几分钟…' })
  message.success(`同步完成：HIKIoT 新增 ${data.hikiot.created}、更新 ${data.hikiot.updated}；钉钉新增 ${data.dingTalk.created}、更新 ${data.dingTalk.updated}`)
  await load()
}
function openCard(person: Person) { selected.value = person; editingCard.value = null; Object.assign(cardForm, { cardNo: '', isVirtual: person.kind === 'Visitor' }); showCard.value = true }
function openEditCard(person: Person, card: Card) { selected.value = person; editingCard.value = card; Object.assign(cardForm, { cardNo: card.cardNo, isVirtual: card.isVirtual }); showCard.value = true }
async function saveCard() {
  if (!selected.value || !cardForm.cardNo) return message.warning('请输入卡号')
  const card = editingCard.value
  const employee = selected.value.kind === 'Employee'
  if (card) await api.put(`/people/${selected.value.id}/cards/${card.id}`, cardForm, { loadingText: employee ? '正在替换团队卡片并核验权限下发状态…' : '正在安全替换访客卡片…' })
  else await api.post(`/people/${selected.value.id}/cards`, cardForm, { loadingText: employee ? '正在发布团队卡片并核验权限下发状态…' : '正在保存访客卡片…' })
  message.success(employee ? (card ? '团队卡片已替换，标准权限已自动更新' : '团队卡片已发布，标准权限已自动更新') : (card ? '卡片已更新并自动下发' : '卡片已保存并自动下发'))
  showCard.value = false
  await load()
}
async function uploadFace(person: Person, file: File) {
  const data = new FormData(); data.append('file', file)
  await api.post(`/people/${person.id}/face`, data, { headers: { 'Content-Type': 'multipart/form-data' }, loadingText: person.kind === 'Employee' ? '正在上传人脸、更新团队凭证并核验权限下发状态…' : '正在上传并下发访客人脸…' })
  message.success(person.kind === 'Employee' ? '人脸已发布到团队，标准权限已自动更新' : '人脸已保存并自动下发')
  await load(); return false
}
function openPassword(person: Person) { passwordPerson.value = person; Object.assign(passwordForm, { password: '', confirmPassword: '' }); showPassword.value = true }
async function savePassword() {
  if (!passwordPerson.value) return
  if (passwordForm.password.length < 4 || passwordForm.password.length > 6) return message.warning('密码必须为 4–6 位')
  if (passwordForm.password !== passwordForm.confirmPassword) return message.warning('两次输入的密码不一致')
  await api.put(`/people/${passwordPerson.value.id}/password`, { password: passwordForm.password }, { loadingText: '正在加密保存密码并下发到支持纯密码的设备…' })
  message.success('密码已加密保存，并已下发到支持纯密码认证的设备')
  showPassword.value = false
}
function publishToHikiot(person: Person) {
  const existsInTeam = Boolean(person.hikiotPersonNo)
  Modal.confirm({ title: existsInTeam ? `重新发布 ${person.name} 的权限？` : `加入 HIKIoT 团队并下发设备？`, content: existsInTeam ? '该人员已在 HIKIoT 团队内，不会重复加入；将重新发布团队资料、凭证与 AccessIt 管理的标准权限。' : '将创建团队成员，并向全部已纳管、支持人员管理的设备授予默认门禁权限。', onOk: async () => {
    const { data } = await api.post(`/people/${person.id}/hikiot/publish`, undefined, { loadingText: '正在发布团队成员、配置权限并核验下发状态…' })
    const issue = data.authorityIssue
    if (issue?.failures?.length) message.warning(`已提交 ${data.deviceCount} 台设备，但有 ${issue.failures.length} 项需要处理：${issue.failures[0].message}`)
    else message.success(issue?.pendingCount ? `已提交到 ${data.deviceCount} 台设备，${issue.pendingCount} 台仍在海康处理中` : `已完成 ${issue?.confirmedCount ?? data.deviceCount} 台设备的权限核验`)
    await load()
  } })
}
function removeFromHikiot(person: Person) {
  Modal.confirm({ title: `从 HIKIoT 团队移除 ${person.name}？`, content: '将先撤销 AccessIt 管理的设备权限并确认，再移除团队成员；海康管理员自行维护的例外权限不会被覆盖。', okType: 'danger', onOk: async () => {
    await api.delete(`/people/${person.id}/hikiot`, { loadingText: '正在撤销设备权限并移除团队成员…' }); message.success('设备权限已撤销，人员已从 HIKIoT 团队移除'); await load()
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
async function notifyHost() { if (!shareId.value) return; await api.post(`/visitor-qr/${shareId.value}/notify-host`, undefined, { loadingText: '正在发送钉钉通知…' }); message.success('已发送钉钉工作通知') }
async function revokeQr() { if (!shareId.value) return; await api.post(`/visitor-qr/${shareId.value}/revoke`, undefined, { loadingText: '正在撤销二维码分享…' }); message.success('二维码分享已撤销'); shareUrl.value = '' }
function remove(person: Person) {
  Modal.confirm({ title: `删除 ${person.name}`, content: person.kind === 'Visitor' ? '将撤销二维码分享，并按人脸、卡片、人员顺序自动清理设备资料。' : '将撤销 AccessIt 管理的标准设备权限；不会自动移除 HIKIoT 团队成员。', okType: 'danger', onOk: async () => { await api.delete(`/people/${person.id}`, { loadingText: '正在撤销权限并清理设备资料…' }); message.success('权限清理已完成或已记录失败状态'); await load() } })
}
onMounted(load)
</script>

<template>
  <section>
    <a-page-header title="人员管理" sub-title="员工变更自动发布到 HIKIoT 团队并走标准权限下发；访客保留设备直连二维码流程">
      <template #extra><a-space v-if="editable" class="page-actions" wrap><a-button @click="syncSources">同步外部目录</a-button><a-button @click="create('Employee')">新增正式员工</a-button><a-button type="primary" @click="create('Visitor')">新增访客</a-button></a-space></template>
    </a-page-header>
    <a-card>
      <a-space class="filters" wrap><a-segmented v-model:value="kind" :options="[{ label: '正式员工', value: 'Employee' }, { label: '访客', value: 'Visitor' }]" @change="load" /><a-input-search v-model:value="keyword" class="search" placeholder="姓名或人员编号" allow-clear @search="load" /></a-space>
      <a-table :columns="columns" :data-source="people" :loading="loading" :pagination="pagination" :row-key="(person: Person) => person.id" :scroll="{ x: 980 }" size="middle">
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'source'"><a-tag v-if="record.dingTalkUserId" color="blue">钉钉</a-tag><a-tag v-if="record.hikiotPersonNo" color="green">HIKIoT</a-tag><span v-if="!record.dingTalkUserId && !record.hikiotPersonNo">本地</span></template>
          <template v-else-if="column.key === 'validity'"><span v-if="record.permanentValid">永久有效</span><span v-else>{{ new Date(record.enableBeginTime).toLocaleString() }} 至 {{ new Date(record.enableEndTime).toLocaleString() }}</span></template>
          <template v-else-if="column.key === 'credentials'"><span>卡 {{ record.cards.length }} / 本地人脸 {{ record.faceAssets.length }}</span><div v-if="record.cards.length" class="credential-list"><a-button v-for="card in record.cards" :key="card.id" type="link" size="small" @click="openEditCard(record, card)">{{ card.cardNo }}</a-button></div></template>
          <template v-else-if="column.key === 'hikiot'"><span v-if="record.kind === 'Employee' && record.hikiotPersonNo">已发布：{{ record.hikiotPersonNo }}<br><small>部门：{{ record.hikiotDepartmentNo || '-' }}</small><br><small>钉钉：{{ record.dingTalkUserId || '-' }}</small></span><span v-else-if="record.kind === 'Employee'">未发布<br><small>钉钉：{{ record.dingTalkUserId || '-' }}</small></span><span v-else>-</span></template>
          <template v-else-if="column.key === 'action'"><a-space v-if="editable" class="actions" wrap><a-button size="small" @click="openEdit(record)">编辑</a-button><a-button size="small" @click="openCard(record)">加卡</a-button><a-upload :show-upload-list="false" :before-upload="(file: File) => uploadFace(record, file)"><a-button size="small">人脸</a-button></a-upload><a-button size="small" @click="openPassword(record)">密码</a-button><a-button v-if="record.kind === 'Employee'" size="small" type="primary" @click="publishToHikiot(record)">{{ record.hikiotPersonNo ? '重新发布' : '加入团队' }}</a-button><a-button v-if="record.kind === 'Employee' && record.hikiotPersonNo" size="small" danger @click="removeFromHikiot(record)">移出团队</a-button><a-button v-if="record.kind === 'Visitor'" size="small" @click="openQr(record)">二维码</a-button><a-button danger size="small" @click="remove(record)">删除</a-button></a-space></template>
        </template>
      </a-table>
    </a-card>

    <a-modal v-model:open="showCreate" :title="kind === 'Employee' ? '新增正式员工' : '新增访客'" @ok="submit"><a-form layout="vertical"><a-form-item v-if="kind === 'Employee'" label="钉钉通讯录成员（可选）"><a-select v-model:value="form.dingTalkUserId" allow-clear show-search option-filter-prop="label" placeholder="选择已同步的钉钉成员" :options="employeeOptions" @change="(id: string) => fillFromDirectory(form, id)" /></a-form-item><a-form-item label="姓名" required><a-input v-model:value="form.name" /></a-form-item><a-form-item label="手机号"><a-input v-model:value="form.mobile" /></a-form-item><template v-if="kind === 'Visitor'"><a-form-item label="开始时间" required><input v-model="form.beginTime" type="datetime-local" class="native-input"></a-form-item><a-form-item label="结束时间" required><input v-model="form.endTime" type="datetime-local" class="native-input"></a-form-item><a-form-item label="最大开门次数（0 为不限）"><a-input-number v-model:value="form.maxOpenDoorTime" :min="0" :max="255" /></a-form-item><a-form-item label="授权设备" required><a-select v-model:value="form.deviceIds" mode="multiple" :options="deviceOptions" /></a-form-item></template></a-form></a-modal>

    <a-modal v-model:open="showEdit" :title="selected?.kind === 'Employee' ? '编辑员工资料' : '编辑访客资料'" @ok="saveEdit"><a-form layout="vertical"><a-form-item v-if="selected?.kind === 'Employee'" label="钉钉通讯录成员"><a-select v-model:value="editForm.dingTalkUserId" allow-clear show-search option-filter-prop="label" :options="employeeOptions" @change="(id: string) => fillFromDirectory(editForm, id)" /></a-form-item><a-form-item label="姓名" required><a-input v-model:value="editForm.name" :disabled="selected?.kind === 'Visitor'" /></a-form-item><a-form-item label="手机号"><a-input v-model:value="editForm.mobile" /></a-form-item><template v-if="selected?.kind === 'Visitor'"><a-form-item label="开始时间" required><input v-model="editForm.beginTime" type="datetime-local" class="native-input"></a-form-item><a-form-item label="结束时间" required><input v-model="editForm.endTime" type="datetime-local" class="native-input"></a-form-item><a-form-item label="最大开门次数"><a-input-number v-model:value="editForm.maxOpenDoorTime" :min="0" :max="255" /></a-form-item><a-form-item label="授权设备" required><a-select v-model:value="editForm.deviceIds" mode="multiple" :options="deviceOptions" /></a-form-item></template><a-alert v-if="selected?.kind === 'Employee'" type="info" show-icon message="钉钉、HIKIoT 目录再次同步时会以外部资料覆盖对应字段。" /></a-form></a-modal>

    <a-modal v-model:open="showCard" :title="cardModalTitle" @ok="saveCard"><a-form layout="vertical"><a-form-item label="卡号" required><a-input v-model:value="cardForm.cardNo" autofocus /></a-form-item><a-form-item><a-checkbox v-model:checked="cardForm.isVirtual">虚拟卡（仅访客二维码）</a-checkbox></a-form-item><a-alert v-if="editingCard" type="info" show-icon :message="selected?.kind === 'Employee' ? '修改员工卡号会替换本系统管理的团队旧卡，并自动重新下发标准门禁权限。' : '修改访客卡号会先删除设备上的旧卡，再下发新卡。'" /></a-form></a-modal>

    <a-modal v-model:open="showPassword" title="设置门禁密码" @ok="savePassword"><a-form layout="vertical"><a-alert type="info" show-icon message="密码仅加密保存，不写入 HIKIoT 团队；保存后仅下发至支持纯密码认证的设备。" /><a-form-item label="新密码" required><a-input-password v-model:value="passwordForm.password" inputmode="numeric" maxlength="6" placeholder="4–6 位密码" /></a-form-item><a-form-item label="确认新密码" required><a-input-password v-model:value="passwordForm.confirmPassword" inputmode="numeric" maxlength="6" placeholder="再次输入密码" /></a-form-item></a-form></a-modal>

    <a-modal v-model:open="showQr" title="生成访客二维码" :footer="null"><a-form layout="vertical"><a-form-item label="设备"><a-select v-model:value="qrForm.deviceId" :options="selected?.deviceGrants.map(grant => ({ value: grant.accessDeviceId, label: grant.accessDevice?.deviceSerial || grant.accessDeviceId }))" /></a-form-item><a-form-item label="二维码有效分钟"><a-input-number v-model:value="qrForm.expireMinutes" :min="5" :max="10080" /></a-form-item><a-form-item label="最大开门次数"><a-input-number v-model:value="qrForm.maxOpenTimes" :min="1" :max="255" /></a-form-item><a-space wrap><a-button type="primary" @click="createQr">生成受控分享链接</a-button><a-button v-if="shareUrl" @click="viewQr">查看二维码</a-button><a-button v-if="shareUrl" @click="notifyHost">通知接待人</a-button><a-button v-if="shareUrl" danger @click="revokeQr">撤销</a-button></a-space><a-alert v-if="shareUrl" class="share" type="success" :message="shareUrl" /></a-form></a-modal>
  </section>
</template>

<style scoped>
.filters { margin-bottom: 16px; }
.search { width: min(360px, 100%); }
.native-input { box-sizing: border-box; width: 100%; height: 32px; padding: 4px 11px; border: 1px solid #d9d9d9; border-radius: 6px; }
.share { margin-top: 16px; word-break: break-all; }
.credential-list { display: flex; flex-wrap: wrap; gap: 2px; margin-top: 4px; }
.credential-list :deep(.ant-btn) { height: auto; padding: 0 3px; }
@media (max-width: 700px) { .page-actions { display: flex; width: 100%; } .page-actions :deep(.ant-btn) { flex: 1 1 calc(50% - 8px); } .actions { min-width: 180px; } :deep(.ant-table-cell) { white-space: normal; } }
</style>
