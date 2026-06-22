<script setup lang="ts">
import { onMounted, ref } from 'vue'
import api from '../services/api'
import type { AuditEvent } from '../types'
const events=ref<AuditEvent[]>([]);const loading=ref(false);const action=ref('')
async function load(){loading.value=true;try{events.value=(await api.get<AuditEvent[]>('/audit-logs',{params:{action:action.value||undefined}})).data}finally{loading.value=false}}onMounted(load)
</script>
<template><section><a-page-header title="审计日志" sub-title="所有下发、删除、二维码和远程开门操作均留痕"/><a-card><a-input-search v-model:value="action" placeholder="按操作代码筛选，如 device.remote-open" allow-clear style="max-width:360px;margin-bottom:16px" @search="load"/><a-table :data-source="events" :loading="loading" :row-key="(e:AuditEvent)=>e.id" :scroll="{x:850}"><a-table-column title="时间" data-index="occurredAtUtc"><template #default="{text}">{{new Date(text).toLocaleString()}}</template></a-table-column><a-table-column title="操作" data-index="action"/><a-table-column title="对象" data-index="entityType"/><a-table-column title="对象 ID" data-index="entityId"/><a-table-column title="操作者" data-index="actorUserId"/><a-table-column title="详情" data-index="detailsJson"/></a-table></a-card></section></template>
