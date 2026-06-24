<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import api from '../services/api'
import type { Person } from '../types'

const router = useRouter()
const form = ref({ name: '', enableBeginTimeUtc: '', enableEndTimeUtc: '', cardNo: '', password: '', generateQr: false, faceAssetId: null as string | null })
const error = ref(''); const busy = ref(false); const visitors = ref<Person[]>([]); const page = ref(1); const total = ref(0); const query = ref(''); const status = ref<string | undefined>()
async function load(targetPage = page.value) { page.value = targetPage; const { data } = await api.get<{ items: Person[]; total: number }>('/visitors', { params: { page: page.value, pageSize: 20, q: query.value || undefined, status: status.value }, skipGlobalLoading: true }); visitors.value = data.items; total.value = data.total }
async function create() {
  error.value = ''
  if (!form.value.name || !form.value.enableBeginTimeUtc || !form.value.enableEndTimeUtc) { error.value = '请填写姓名、开始时间和结束时间。'; return }
  busy.value = true
  try {
    const { data } = await api.post('/visitors', { ...form.value, cardNo: form.value.cardNo || null, password: form.value.password || null, enableBeginTimeUtc: new Date(form.value.enableBeginTimeUtc).toISOString(), enableEndTimeUtc: new Date(form.value.enableEndTimeUtc).toISOString() })
    await router.push(`/visitors/${data.person.id}`)
  } catch (e: any) { error.value = e.response?.data || e.message || '创建访客失败。' } finally { busy.value = false }
}
function changePage(next: number) { load(next) }
onMounted(load)
</script>

<template>
  <a-space direction="vertical" size="large" style="width:100%"><a-page-header title="访客" sub-title="访客不进入海康团队，保存后直接遍历全部门禁设备下发" />
    <a-card title="新建访客" style="max-width:720px"><a-alert v-if="error" type="error" :message="error" show-icon style="margin-bottom:16px" /><a-form layout="vertical"><a-form-item label="姓名" required><a-input v-model:value="form.name" /></a-form-item><a-row :gutter="16"><a-col :span="12"><a-form-item label="开始时间" required><a-input v-model:value="form.enableBeginTimeUtc" type="datetime-local" /></a-form-item></a-col><a-col :span="12"><a-form-item label="结束时间" required><a-input v-model:value="form.enableEndTimeUtc" type="datetime-local" /></a-form-item></a-col></a-row><a-form-item label="卡号（可多张，以逗号或换行分隔）"><a-textarea v-model:value="form.cardNo" /></a-form-item><a-form-item label="密码（可选）"><a-input-password v-model:value="form.password" /></a-form-item><a-form-item><a-checkbox v-model:checked="form.generateQr">生成访客动态二维码并创建外链</a-checkbox></a-form-item><a-button type="primary" :loading="busy" @click="create">创建并下发</a-button></a-form></a-card>
    <a-card title="访客记录"><a-space style="margin-bottom:16px" wrap><a-input v-model:value="query" placeholder="姓名、手机号或人员编号" style="width:220px" allow-clear @press-enter="load(1)" /><a-select v-model:value="status" placeholder="全部状态" allow-clear style="width:130px" :options="[{ value: 'active', label: '有效中' }, { value: 'upcoming', label: '未生效' }, { value: 'expired', label: '已过期' }]" @change="load(1)" /><a-button @click="load(1)">搜索</a-button></a-space><a-table :data-source="visitors" :pagination="{ current: page, pageSize: 20, total, showSizeChanger: false }" row-key="id" @change="(pagination: any) => changePage(pagination.current)"><a-table-column title="姓名" data-index="name" /><a-table-column title="有效期"><template #default="{ record }">{{ record.enableBeginTimeUtc }} — {{ record.enableEndTimeUtc }}</template></a-table-column><a-table-column title="二维码"><template #default="{ record }">{{ record.qrShareToken ? '已生成' : '-' }}</template></a-table-column><a-table-column title="操作"><template #default="{ record }"><a-button size="small" @click="router.push(`/visitors/${record.id}`)">详情与控制</a-button></template></a-table-column></a-table></a-card>
  </a-space>
</template>
