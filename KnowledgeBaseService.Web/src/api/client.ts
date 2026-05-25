import axios, { AxiosInstance, InternalAxiosRequestConfig, AxiosResponse } from 'axios'

// 根据环境自动决定 API 基础地址
const getApiBaseUrl = () => {
  // 如果是生产环境，直接使用后端的公网 IP
  // 前端运行在 8080，后端运行在 5000（同一服务器）
  if (window.location.hostname !== 'localhost' && window.location.hostname !== '127.0.0.1') {
    return `http://${window.location.hostname}:5000/api`
  }
  
  // 本地开发环境，使用 Nginx 代理
  return '/api'
}

// 创建自定义 Axios 实例类型，响应直接返回 data
interface CustomAxiosInstance extends Omit<AxiosInstance, 'get' | 'post' | 'put' | 'delete' | 'patch'> {
  get<T = any>(url: string, config?: any): Promise<T>
  post<T = any>(url: string, data?: any, config?: any): Promise<T>
  put<T = any>(url: string, data?: any, config?: any): Promise<T>
  delete<T = any>(url: string, config?: any): Promise<T>
  patch<T = any>(url: string, data?: any, config?: any): Promise<T>
}

const instance = axios.create({
  baseURL: getApiBaseUrl(),
  timeout: 30000,
  headers: {
    'Content-Type': 'application/json',
  },
})

// 请求拦截器
instance.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    // 可以在这里添加认证令牌等
    return config
  },
  (error) => {
    return Promise.reject(error)
  }
)

// 响应拦截器 - 直接返回 data
instance.interceptors.response.use(
  (response: AxiosResponse) => {
    return response.data
  },
  (error) => {
    if (error.response?.status === 401) {
      // 处理未授权
    }
    return Promise.reject(error)
  }
)

// 导出为自定义类型
const apiClient = instance as CustomAxiosInstance

export default apiClient
