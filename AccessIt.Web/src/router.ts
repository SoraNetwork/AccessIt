import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', component: () => import('./pages/LoginPage.vue'), meta: { public: true } },
    { path: '/public/visitor-qr/:token', component: () => import('./pages/PublicQrPage.vue'), meta: { public: true } },
    { path: '/pending-access', component: () => import('./pages/PendingAccessPage.vue'), meta: { pending: true } },
    {
      path: '/', component: () => import('./layouts/AppShell.vue'), children: [
        { path: '', redirect: '/dashboard' },
        { path: 'dashboard', component: () => import('./pages/DashboardPage.vue') },
        { path: 'people', component: () => import('./pages/PeoplePage.vue') },
        { path: 'devices', component: () => import('./pages/DevicesPage.vue') },
        { path: 'operations', component: () => import('./pages/OperationsPage.vue') },
        { path: 'settings', component: () => import('./pages/SettingsPage.vue'), meta: { superAdmin: true } },
        { path: 'users', component: () => import('./pages/UsersPage.vue'), meta: { superAdmin: true } },
        { path: 'audit', component: () => import('./pages/AuditPage.vue') }
      ]
    },
    { path: '/:pathMatch(.*)*', redirect: '/dashboard' }
  ]
})

router.beforeEach((to) => {
  const raw = localStorage.getItem('accessit.user')
  const user = raw ? JSON.parse(raw) as { role?: string } : null
  if (!to.meta.public && !user) return '/login'
  if (user?.role === 'None' && !to.meta.pending) return '/pending-access'
  if (to.meta.superAdmin && user?.role !== 'SuperAdmin') return '/dashboard'
  return true
})

export default router
