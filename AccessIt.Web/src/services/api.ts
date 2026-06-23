import axios from 'axios'
import { beginGlobalLoading, endGlobalLoading } from './loading'

declare module 'axios' {
  export interface AxiosRequestConfig {
    loadingText?: string
    skipGlobalLoading?: boolean
  }
}

const api = axios.create({ baseURL: import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000/api' })

api.interceptors.request.use((config) => {
  const token = localStorage.getItem('accessit.token')
  if (token) config.headers.Authorization = `Bearer ${token}`
  if (!config.skipGlobalLoading) {
    beginGlobalLoading(config.loadingText)
    ;(config as typeof config & { __accessitLoading?: boolean }).__accessitLoading = true
  }
  return config
})

api.interceptors.response.use(
  (response) => {
    if ((response.config as typeof response.config & { __accessitLoading?: boolean }).__accessitLoading) endGlobalLoading()
    return response
  },
  (error) => {
    if ((error.config as typeof error.config & { __accessitLoading?: boolean } | undefined)?.__accessitLoading) endGlobalLoading()
    return Promise.reject(error)
  }
)

export default api
