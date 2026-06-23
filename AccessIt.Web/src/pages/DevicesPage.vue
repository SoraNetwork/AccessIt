<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { message } from 'ant-design-vue'
import api from '../services/api'
import type { Device } from '../types'
import { useAuthStore } from '../stores/auth'
import { canManageAccess } from '../utils/permissions'

const auth = useAuthStore(); const devices = ref<Device[]>([]); const loading = ref(false); const reason = ref(''); const target = ref<Device | null>(null); const showOpen = ref(false)
const pagination = { pageSize: 10, showSizeChanger: true, pageSizeOptions: ['10', '20', '50', '100'], showQuickJumper: true, showTotal: (total: number) => `共 ${total} 台` }
const columns = [{title:'设备分组',dataIndex:'groupName'},{title:'设备序列号',dataIndex:'deviceSerial'},{title:'能力集',key:'capability'},{title:'最近同步',dataIndex:'lastSyncedAtUtc'},{title:'操作',key:'action'}]
const editable = () => canManageAccess(auth.role)
async function load(){loading.value=true;try{devices.value=(await api.get<Device[]>('/devices')).data}finally{loading.value=false}}
async function discover(){await api.post('/devices/discover',undefined,{loadingText:'正在同步 HIKIoT 设备分组与能力集…'});message.success('设备组与能力集同步完成');await load()}
function confirmOpen(device:Device){target.value=device;reason.value='';showOpen.value=true}
async function open(){if(!target.value||!reason.value){message.warning('请填写开门事由');return}const {data}=await api.post(`/devices/${target.value.id}/open`,{reason:reason.value},{loadingText:'正在向设备发送开门指令…'});data.succeeded?message.success('已发送开门指令'):message.error(data.message);showOpen.value=false;target.value=null}
async function sync(device:Device){await api.post(`/devices/${device.id}/sync`,undefined,{loadingText:'正在读取设备人员并比对差异…'});message.success('人员同步已完成，请在任务中心查看差异')}
onMounted(load)
</script>
<template><section><a-page-header title="设备管理" sub-title="HIKIoT 单门设备与能力集"><template #extra><a-button v-if="editable()" type="primary" :loading="loading" @click="discover">同步设备分组</a-button></template></a-page-header><a-card><a-table :columns="columns" :data-source="devices" :loading="loading" :pagination="pagination" :row-key="(d:Device)=>d.id" :scroll="{x:860}"><template #bodyCell="{column,record}"><template v-if="column.key==='capability'"><a-space wrap><a-tag :color="record.supportsUserInfo?'green':'default'">人员</a-tag><a-tag :color="record.supportsCardInfo?'green':'default'">卡片</a-tag><a-tag :color="record.supportsFace?'green':'default'">人脸</a-tag><a-tag :color="record.supportsPurePassword?'green':'default'">密码</a-tag><a-tag :color="record.supportsRemoteOpen?'green':'default'">远程开门</a-tag></a-space></template><template v-else-if="column.dataIndex==='lastSyncedAtUtc'">{{ record.lastSyncedAtUtc ? new Date(record.lastSyncedAtUtc).toLocaleString() : '-' }}</template><template v-else-if="column.key==='action'"><a-space v-if="editable()"><a-button size="small" @click="sync(record)">同步人员</a-button><a-button v-if="record.supportsRemoteOpen" danger size="small" @click="confirmOpen(record)">远程开门</a-button></a-space></template></template></a-table></a-card><a-modal v-model:open="showOpen" title="远程开门确认" @ok="open"><a-alert type="warning" show-icon message="该动作将直接向门禁设备发送开门指令，并写入审计日志。"/><a-form layout="vertical"><a-form-item label="开门事由" required><a-textarea v-model:value="reason" :rows="3" /></a-form-item></a-form></a-modal></section></template>
