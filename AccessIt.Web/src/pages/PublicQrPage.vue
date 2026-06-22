<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import QRCode from 'qrcode'
import axios from 'axios'

const route = useRoute(); const image = ref(''); const expiresAt = ref(''); const failed = ref(false)
onMounted(async () => { try { const base = (import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000/api').replace(/\/api$/, ''); const { data } = await axios.get(`${base}/public/visitor-qr/${route.params.token}`); image.value = await QRCode.toDataURL(data.qrCode, { width: 320, margin: 2 }); expiresAt.value = new Date(data.expiresAtUtc).toLocaleString() } catch { failed.value = true } })
</script>

<template><main class="public"><a-card><a-result v-if="failed" status="404" title="二维码已失效或已撤销" /><template v-else><h1>访客开门二维码</h1><a-spin v-if="!image" /><img v-else :src="image" alt="访客开门二维码" /><p>有效期至：{{ expiresAt }}</p></template></a-card></main></template>
<style scoped>.public{min-height:100vh;display:grid;place-items:center;background:#f5f5f5;padding:20px;text-align:center}.public img{max-width:100%;display:block;margin:20px auto}.public p{color:#8c8c8c}</style>
