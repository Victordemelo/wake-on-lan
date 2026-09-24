export type User = { id: string; name: string; email: string; twoFactorEnabled: boolean }
export type AuthResponse = { user: User }
export type RegistrationStatus = { open: boolean; setupRequired: boolean }
export type WakeMethod = 'LocalBroadcast' | 'WakeOnWan' | 'TailscaleGateway'
export type PowerAction = 'shutdown' | 'restart' | 'suspend' | 'hibernate'
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
export type ActivityItem = { id: string; machineId?: string; machineName: string; action: string; succeeded: boolean; message: string; requestedAt: string }
export type SessionInfo = { id: string; createdAt: string; lastSeenAt: string; ipAddress?: string; userAgent?: string; current: boolean }
export type SecurityEvent = { id: string; type: string; createdAt: string; ipAddress?: string; userAgent?: string }
export type SecurityOverview = {
  events: SecurityEvent[]
  failedPasswords: number
  lastFailedPassword?: string
  failedCodes: number
  lastFailedCode?: string
}
export type TwoFactorSetup = { secret: string; uri: string }
export type GatewayStatus = { gatewayConfigured: boolean; gatewayOnline: boolean }

export class ApiError extends Error {
  constructor(message: string, public status: number, public twoFactorRequired = false) { super(message) }
}

// Called when the session is no longer valid (expired, revoked or password changed
// on another device), so the app can drop the account data at once.
let onUnauthorized: (() => void) | null = null
export const setUnauthorizedHandler = (handler: (() => void) | null) => { onUnauthorized = handler }

// The session lives in an HttpOnly cookie that scripts cannot read. The custom header
// is required by the API on every state-changing call (protection against CSRF).
async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  let response: Response
  try {
    response = await fetch(path, {
      ...options,
      credentials: 'same-origin',
      headers: {
        'Content-Type': 'application/json',
        'X-Remote-Wake-Request': '1',
        ...options.headers,
      },
    })
  } catch {
    // fetch only rejects when the server is unreachable; its message is in English.
    throw new ApiError('Sem conexão com o servidor. Verifique a internet e se o Remote Wake está no ar.', 0)
  }

  if (!response.ok) {
    // Sign-in endpoints answer 401 for wrong credentials; everywhere else it means the session ended.
    if (response.status === 401 && !path.startsWith('/api/auth/')) onUnauthorized?.()
    const problem = await response.json().catch(() => null)
    const fallback = response.status === 429 ? 'Muitas tentativas. Aguarde um minuto.' : 'Não foi possível concluir a operação.'
    throw new ApiError(problem?.message ?? problem?.detail ?? fallback, response.status, Boolean(problem?.twoFactorRequired))
  }

  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

const post = <T>(path: string, body?: unknown) =>
  request<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) })

export const api = {
  registration: () => request<RegistrationStatus>('/api/auth/registration'),
  session: () => request<{ user: User | null }>('/api/auth/session'),
  status: () => request<GatewayStatus>('/api/status'),
  login: (email: string, password: string, code?: string) => post<AuthResponse>('/api/auth/login', { email, password, code }),
  register: (name: string, email: string, password: string, setupToken?: string) =>
    post<AuthResponse>('/api/auth/register', { name, email, password, setupToken }),
  logout: () => post<void>('/api/auth/logout'),

  changePassword: (currentPassword: string, newPassword: string) =>
    post<{ revokedSessions: number }>('/api/account/password', { currentPassword, newPassword }),
  sessions: () => request<SessionInfo[]>('/api/account/sessions'),
  revokeSession: (id: string) => request<void>(`/api/account/sessions/${id}`, { method: 'DELETE' }),
  revokeOtherSessions: () => post<{ revokedSessions: number }>('/api/account/sessions/revoke-others'),
  securityOverview: () => request<SecurityOverview>('/api/account/events'),
  startTwoFactor: () => post<TwoFactorSetup>('/api/account/two-factor/setup'),
  enableTwoFactor: (password: string, code: string) => post<{ recoveryCodes: string[] }>('/api/account/two-factor/enable', { password, code }),
  disableTwoFactor: (password: string, code: string) => post<void>('/api/account/two-factor/disable', { password, code }),
  regenerateRecoveryCodes: (password: string) => post<{ recoveryCodes: string[] }>('/api/account/two-factor/recovery-codes', { password }),

  activity: () => request<ActivityItem[]>('/api/activity'),
  machines: () => request<Machine[]>('/api/machines'),
  addMachine: (machine: MachineInput) => post<Machine>('/api/machines', machine),
  updateMachine: (id: string, machine: MachineInput) => request<Machine>(`/api/machines/${id}`, { method: 'PUT', body: JSON.stringify(machine) }),
  removeMachine: (id: string) => request<void>(`/api/machines/${id}`, { method: 'DELETE' }),
  wake: (id: string) => post<{ message: string }>(`/api/machines/${id}/wake`),
  action: (id: string, action: PowerAction) => post<{ message: string }>(`/api/machines/${id}/actions`, { action }),
  agentKey: (id: string) => post<{ machineId: string; key: string }>(`/api/machines/${id}/agent-key`),
  revokeAgent: (id: string) => request<void>(`/api/machines/${id}/agent-key`, { method: 'DELETE' }),
}
