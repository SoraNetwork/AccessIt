import { defineStore } from 'pinia'
import api from '../services/api'
import type { LoginUser } from '../types'

interface AuthState { token: string | null; user: LoginUser | null }

export const useAuthStore = defineStore('auth', {
  state: (): AuthState => ({ token: localStorage.getItem('accessit.token'), user: JSON.parse(localStorage.getItem('accessit.user') || 'null') }),
  getters: { isAuthenticated: (state) => Boolean(state.token && state.user), role: (state) => state.user?.role },
  actions: {
    async loginWithCode(code: string, inApp = false) {
      const { data } = await api.post(`/auth/dingtalk/${inApp ? 'in-app' : 'web'}`, { code })
      this.token = data.token
      this.user = data.user
      localStorage.setItem('accessit.token', data.token)
      localStorage.setItem('accessit.user', JSON.stringify(data.user))
    },
    logout() {
      this.token = null
      this.user = null
      localStorage.removeItem('accessit.token')
      localStorage.removeItem('accessit.user')
    }
  }
})
