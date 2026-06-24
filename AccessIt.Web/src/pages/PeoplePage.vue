<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import api from '../services/api'
import type { Person } from '../types'

const people = ref<Person[]>([])
const query = ref('')
const busy = ref(false)
const credential = ref<{ id: string; name: string; cardNo?: string; password: string }>({ id: '', name: '', password: '' })
const editOpen = ref(false)
const filtered = computed(() => people.value.filter(x => !query.value || `${x.name}${x.mobile || ''}`.toLowerCase().includes(query.value.toLowerCase())))

async function load() { people.value = (await api.get<Person[]>('/persons', { skipGlobalLoading: true })).data }
async function sync(kind: 'hikiot' | 'dingtalk') { busy.value = true; try { await api.post(`/persons/sync/${kind}`); await load() } finally { busy.value = false } }
async function publish(person: Person) { await api.post(`/persons/${person.id}/publish-hikiot`); await load() }
function edit(person: Person) { credential.value = { id: person.id, name: person.name, cardNo: person.cardNos?.join(', ') || person.cardNo, password: '' }; editOpen.value = true }
async function save() { await api.put(`/persons/${credential.value.id}/credentials`, { cardNo: credential.value.cardNo, password: credential.value.password || null, faceAssetId: null }); editOpen.value = false; await load() }
async function upload(person: Person, file: File) { const data = new FormData(); data.append('file', file); const { data: face } = await api.post(`/persons/${person.id}/face`, data); await api.put(`/persons/${person.id}/credentials`, { cardNo: null, password: null, faceAssetId: face.faceAssetId }); await load(); return false }
onMounted(load)
</script>

<template>
  <a-space direction="vertical" size="large" style="width:100%">
    <a-page-header title="人员" sub-title="合并海康团队和钉钉通讯录；同名人员自动合并" />
    <a-space wrap>
      <a-button type="primary" :loading="busy" @click="sync('hikiot')">同步海康团队</a-button>
      <a-button :loading="busy" @click="sync('dingtalk')">同步钉钉通讯录</a-button>
      <a-input v-model:value="query" placeholder="搜索姓名或手机号" style="width:220px" allow-clear />
    </a-space>
    <a-table :data-source="filtered" :pagination="false" row-key="id" :scroll="{ x: 850 }">
      <a-table-column title="姓名" data-index="name" />
      <a-table-column title="手机号" data-index="mobile" />
      <a-table-column title="类型" data-index="kind" />
      <a-table-column title="来源"><template #default="{ record }"><a-tag v-for="source in record.sources" :key="source" :color="source === 'Hikiot' ? 'blue' : 'green'">{{ source }}</a-tag></template></a-table-column>
      <a-table-column title="认证"><template #default="{ record }">{{ record.cardNos?.length ? `卡 × ${record.cardNos.length}` : '' }}{{ record.faceAssetId || record.hikiotFaceUrl ? ' 人脸' : '' }}{{ !record.cardNos?.length && !record.faceAssetId && !record.hikiotFaceUrl ? '-' : '' }}</template></a-table-column>
      <a-table-column title="操作" fixed="right"><template #default="{ record }"><a-space><a-button size="small" @click="edit(record)">认证信息</a-button><a-upload :before-upload="(file: File) => upload(record, file)" :show-upload-list="false"><a-button size="small">上传人脸</a-button></a-upload><a-button v-if="record.kind === 'Employee' && !record.hikiotPersonNo" size="small" type="primary" @click="publish(record)">下发到海康团队</a-button></a-space></template></a-table-column>
    </a-table>
  </a-space>
  <a-modal v-model:open="editOpen" :title="`认证信息：${credential.name}`" @ok="save"><a-form layout="vertical"><a-form-item label="卡号（可多张，以逗号或换行分隔）"><a-textarea v-model:value="credential.cardNo" /></a-form-item><a-form-item label="密码（仅本次下发，不保存）"><a-input-password v-model:value="credential.password" /></a-form-item></a-form></a-modal>
</template>
