<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { message } from 'ant-design-vue'
import api from '../services/api'
import type { Job, SyncConflict } from '../types'
import { useAuthStore } from '../stores/auth'
import { canManageAccess } from '../utils/permissions'

const auth=useAuthStore();const jobs=ref<Job[]>([]);const conflicts=ref<SyncConflict[]>([]);const loading=ref(false);const editable=()=>canManageAccess(auth.role)
const pagination={pageSize:10,showSizeChanger:true,pageSizeOptions:['10','20','50','100'],showQuickJumper:true,showTotal:(total:number)=>`共 ${total} 项`}
async function load(){loading.value=true;try{const [j,c]=await Promise.all([api.get<Job[]>('/jobs'),api.get<SyncConflict[]>('/sync-conflicts',{params:{resolution:'Pending'}})]);jobs.value=j.data;conflicts.value=c.data}finally{loading.value=false}}
async function retry(id:string){await api.post(`/jobs/${id}/retry`,undefined,{loadingText:'正在立即重试下发…'});message.success('已立即发起重试');await load()}
async function resolve(id:string,resolution:string){await api.post(`/devices/sync-conflicts/${id}/resolve`,{resolution},{loadingText:'正在处理同步冲突…'});message.success('冲突已处理');await load()}
onMounted(load)
</script>
<template><section><a-page-header title="同步与任务中心" sub-title="设备人员差异、下发进度与失败重试" /><a-row :gutter="16"><a-col :xs="24" :xl="14"><a-card title="下发任务"><a-table :data-source="jobs" :loading="loading" :pagination="pagination" :row-key="(j:Job)=>j.id" :scroll="{x:700}"><a-table-column title="步骤" data-index="type" /><a-table-column title="状态" data-index="status"><template #default="{text}"><a-tag :color="text==='Succeeded'?'green':text==='Failed'?'red':'blue'">{{ text }}</a-tag></template></a-table-column><a-table-column title="尝试" data-index="attemptCount" /><a-table-column title="失败原因" data-index="failureMessage" /><a-table-column v-if="editable()" title="操作"><template #default="{record}"><a-button v-if="record.status==='Failed'" size="small" @click="retry(record.id)">重试</a-button></template></a-table-column></a-table></a-card></a-col><a-col :xs="24" :xl="10"><a-card title="待确认同步差异"><a-list :data-source="conflicts" :loading="loading"><template #renderItem="{item}"><a-list-item><a-list-item-meta :title="`${item.employeeNo} · ${item.fieldName}`" :description="`本地：${item.localValue||'-'} / 设备：${item.remoteValue||'-'}`"/><a-space v-if="editable()"><a-button size="small" @click="resolve(item.id,'KeepLocal')">保留本地并下发</a-button><a-button size="small" @click="resolve(item.id,'KeepDevice')">采用设备数据</a-button></a-space></a-list-item></template></a-list></a-card></a-col></a-row></section></template>
