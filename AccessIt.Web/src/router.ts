import { createRouter, createWebHistory } from 'vue-router'
import { beginGlobalLoading, endGlobalLoading } from './services/loading'

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
        { path: 'visitors/:id', component: () => import('./pages/VisitorDetailPage.vue') },
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
  beginGlobalLoading('页面加载中…')
  return true
})
router.afterEach(() => endGlobalLoading())
router.onError(() => endGlobalLoading())

export default router
