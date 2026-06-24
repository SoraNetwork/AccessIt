<script setup lang="ts">
import { ref } from 'vue'
import api from '../services/api'

const form = ref({ name: '', enableBeginTimeUtc: '', enableEndTimeUtc: '', cardNo: '', password: '', generateQr: false, faceAssetId: null as string | null })
const shareUrl = ref('')
const result = ref('')
const busy = ref(false)
async function create() {
  busy.value = true
  try {
    const { data } = await api.post('/visitors', { ...form.value, cardNo: form.value.cardNo || null, password: form.value.password || null, enableBeginTimeUtc: new Date(form.value.enableBeginTimeUtc).toISOString(), enableEndTimeUtc: new Date(form.value.enableEndTimeUtc).toISOString() })
    shareUrl.value = data.sharePath || ''; result.value = data.person.lastIssueResultJson || '已提交'
  } finally { busy.value = false }
}
</script>

<template>
  <a-page-header title="访客" sub-title="访客不进入海康团队，保存后直接遍历全部门禁设备下发" />
  <a-card style="max-width:720px"><a-form layout="vertical" @finish="create"><a-form-item label="姓名" required><a-input v-model:value="form.name" required /></a-form-item><a-row :gutter="16"><a-col :span="12"><a-form-item label="开始时间" required><a-input v-model:value="form.enableBeginTimeUtc" type="datetime-local" required /></a-form-item></a-col><a-col :span="12"><a-form-item label="结束时间" required><a-input v-model:value="form.enableEndTimeUtc" type="datetime-local" required /></a-form-item></a-col></a-row><a-form-item label="卡号（可选）"><a-input v-model:value="form.cardNo" /></a-form-item><a-form-item label="密码（可选）"><a-input-password v-model:value="form.password" /></a-form-item><a-form-item><a-checkbox v-model:checked="form.generateQr">生成访客动态二维码并创建外链</a-checkbox></a-form-item><a-button html-type="submit" type="primary" :loading="busy">创建并下发</a-button></a-form><a-alert v-if="shareUrl" type="success" show-icon message="访客二维码外链已生成" :description="shareUrl" style="margin-top:20px" /><a-typography-paragraph v-if="result" style="margin-top:16px"><pre>{{ result }}</pre></a-typography-paragraph></a-card>
</template>
