export type User = { id: string; name: string; email: string }
export type AuthResponse = { token: string; user: User }
export type WakeMethod = 'LocalBroadcast' | 'WakeOnWan' | 'TailscaleGateway'
export type Machine = {
  id: string
  name: string
  macAddress: string
  hostname?: string
  broadcastAddress: string
  wolPort: number
  wakeMethod: WakeMethod
  lastWakeRequestedAt?: string
  createdAt: string
  agentOnline: boolean
  gatewayOnline: boolean
}
export type MachineInput = Omit<Machine, 'id' | 'createdAt' | 'lastWakeRequestedAt' | 'agentOnline' | 'gatewayOnline'>
export type ActivityItem = { id: string; machineId: string; machineName: string; action: string; succeeded: boolean; message: string; requestedAt: string }
export class ApiError extends Error { constructor(message: string, public status: number) { super(message) } }

const tokenKey = 'remote-wake-token'

export const session = {
  get: () => localStorage.getItem(tokenKey),
  set: (token: string) => localStorage.setItem(tokenKey, token),
  clear: () => localStorage.removeItem(tokenKey),
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const token = session.get()
  const response = await fetch(path, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...options.headers,
    },
  })

  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    throw new ApiError(problem?.message ?? problem?.detail ?? (response.status === 429 ? 'Muitas tentativas. Aguarde um minuto.' : response.status === 401
      ? 'E-mail ou senha incorretos.'
      : 'Não foi possível concluir a operação.'), response.status)
  }

  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export const api = {
  registration: () => request<{ open: boolean }>('/api/auth/registration'),
  activity: () => request<ActivityItem[]>('/api/activity'),
  login: (email: string, password: string) => request<AuthResponse>('/api/auth/login', {
    method: 'POST', body: JSON.stringify({ email, password }),
  }),
  register: (name: string, email: string, password: string) => request<AuthResponse>('/api/auth/register', {
    method: 'POST', body: JSON.stringify({ name, email, password }),
  }),
  machines: () => request<Machine[]>('/api/machines'),
  addMachine: (machine: MachineInput) => request<Machine>('/api/machines', {
    method: 'POST', body: JSON.stringify(machine),
  }),
  removeMachine: (id: string) => request<void>(`/api/machines/${id}`, { method: 'DELETE' }),
  updateMachine: (id: string, machine: MachineInput) => request<Machine>(`/api/machines/${id}`, { method: 'PUT', body: JSON.stringify(machine) }),
  revokeAgent: (id: string) => request<void>(`/api/machines/${id}/agent-key`, { method: 'DELETE' }),
  wake: (id: string) => request<{ message: string }>(`/api/machines/${id}/wake`, { method: 'POST' }),
  action: (id: string, action: 'shutdown' | 'restart') => request<{ message: string }>(`/api/machines/${id}/actions`, {
    method: 'POST', body: JSON.stringify({ action }),
  }),
  agentKey: (id: string) => request<{ machineId: string; key: string }>(`/api/machines/${id}/agent-key`, { method: 'POST' }),
}

