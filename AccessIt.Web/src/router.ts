import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', component: () => import('./pages/LoginPage.vue'), meta: { public: true } },
    {
      path: '/',
      component: () => import('./layouts/AppShell.vue'),
      children: [
        { path: '', redirect: '/dashboard' },
        { path: 'dashboard', component: () => import('./pages/PeoplePage.vue') },
        { path: 'visitors', component: () => import('./pages/VisitorsPage.vue') },
        { path: 'hikiot', component: () => import('./pages/HikiotPage.vue') },
      ]
    },
    { path: '/:pathMatch(.*)*', redirect: '/login' }
  ]
})

router.beforeEach((to) => {
  const raw = localStorage.getItem('accessit.user')
  const user = raw ? JSON.parse(raw) as { role?: string } : null
  if (!to.meta.public && !user) return '/login'
  if (user?.role === 'None' && !to.meta.pending) return '/login'
  if (to.meta.superAdmin && user?.role !== 'SuperAdmin') return '/login'
  return true
})

export default router
