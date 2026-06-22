<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { message } from 'ant-design-vue'
import api from '../services/api'
import type { ApplicationRole } from '../types'
interface User { id:string; name:string; dingTalkUserId:string; mobile?:string; role:ApplicationRole; isActive:boolean; lastLoginAtUtc?:string }
const users=ref<User[]>([]);const loading=ref(false);const roles:ApplicationRole[]=['SuperAdmin','AccessAdmin','Auditor']
async function load(){loading.value=true;try{users.value=(await api.get<User[]>('/users')).data}finally{loading.value=false}}async function setRole(user:User,role:ApplicationRole){await api.put(`/users/${user.id}/role`,{role});message.success('角色已更新');await load()}async function setActive(user:User){await api.put(`/users/${user.id}/active`,{isActive:!user.isActive});await load()}onMounted(load)
</script>
<template><section><a-page-header title="系统用户" sub-title="钉钉通讯录成员默认无权限，需明确授予角色"/><a-card><a-table :data-source="users" :loading="loading" :row-key="(u:User)=>u.id" :scroll="{x:760}"><a-table-column title="姓名" data-index="name"/><a-table-column title="钉钉 ID" data-index="dingTalkUserId"/><a-table-column title="角色"><template #default="{record}"><a-select :value="record.role==='None'?undefined:record.role" placeholder="未授权" style="width:130px" @change="(role:ApplicationRole)=>setRole(record,role)"><a-select-option v-for="role in roles" :key="role" :value="role">{{role}}</a-select-option></a-select></template></a-table-column><a-table-column title="状态"><template #default="{record}"><a-switch :checked="record.isActive" checked-children="启用" un-checked-children="停用" @change="()=>setActive(record)"/></template></a-table-column><a-table-column title="最近登录"><template #default="{record}">{{record.lastLoginAtUtc?new Date(record.lastLoginAtUtc).toLocaleString():'-'}}</template></a-table-column></a-table></a-card></section></template>
