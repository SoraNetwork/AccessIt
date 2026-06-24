<script setup lang="ts">
import { onMounted, ref } from 'vue'
import api from '../services/api'
const status = ref<any>(null)
const departmentNo = ref('')
async function load() { status.value = (await api.get('/hikiot/connection')).data }
async function authorize() { const { data } = await api.post('/hikiot/connection/authorize'); window.open(data.authorizationUrl, '_blank', 'noopener') }
async function saveDepartment() { await api.put('/hikiot/connection/default-department', { departmentNo: departmentNo.value }); await load() }
onMounted(async () => { await load(); departmentNo.value = status.value?.defaultDepartmentNo || '' })
</script>
<template><a-page-header title="海康连接" sub-title="人员同步与门禁下发前需要完成海康授权" /><a-card><a-descriptions v-if="status" bordered :column="1"><a-descriptions-item label="授权状态">{{ status.isAuthorized ? '已授权' : '未授权' }}</a-descriptions-item><a-descriptions-item label="团队编号">{{ status.teamNo || '-' }}</a-descriptions-item><a-descriptions-item label="根部门编号">{{ status.defaultDepartmentNo || '-' }}</a-descriptions-item><a-descriptions-item label="授权有效期">{{ status.userTokenExpiresAtUtc || '-' }}</a-descriptions-item><a-descriptions-item label="最近错误">{{ status.lastError || '-' }}</a-descriptions-item></a-descriptions><a-form layout="inline" style="margin-top:16px" @finish="saveDepartment"><a-form-item label="海康根部门 departNo"><a-input v-model:value="departmentNo" required placeholder="从海康团队组织中复制" /></a-form-item><a-button html-type="submit">保存根部门</a-button></a-form><a-space style="margin-top:16px"><a-button type="primary" @click="authorize">前往海康授权</a-button><a-button @click="load">刷新状态</a-button></a-space></a-card></template>
