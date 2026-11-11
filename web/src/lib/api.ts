// Tipos espelham os DTOs do backend (Agenda.Api/Schedule).

export type GoogleStatus = 'notConnected' | 'needsReconnect' | 'missingCalendarScope' | 'connected'

export interface Me {
  id: string
  name: string
  email: string
  avatarUrl: string | null
  google: { status: GoogleStatus; canReadTasks: boolean }
}

export type SourceKind = 'calendar' | 'taskList'

export interface Source {
  id: string
  kind: SourceKind
  name: string
  primary: boolean
  googleColor: string | null
}

export type AgendaItemKind = 'event' | 'task' | 'busy'

export interface AgendaItem {
  id: string
  sourceId: string
  kind: AgendaItemKind
  title: string | null
  /** Com hora: ISO em UTC. Dia inteiro: "yyyy-MM-dd". */
  start: string
  /** Exclusivo. */
  end: string
  allDay: boolean
  location: string | null
}

export interface AgendaResponse {
  items: AgendaItem[]
  failedSources: string[]
}

export class ApiError extends Error {
  readonly status: number
  /** Código estável vindo do backend (ex.: "google_reconnect_required"). */
  readonly code: string | undefined

  constructor(status: number, code?: string) {
    super(`HTTP ${status}${code ? ` (${code})` : ''}`)
    this.status = status
    this.code = code
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  // Mesma origem: o cookie HttpOnly de sessão vai junto automaticamente. Nenhum token passa pelo JS.
  const res = await fetch(path, { headers: { Accept: 'application/json' }, ...init })

  if (!res.ok) {
    let code: string | undefined
    try {
      code = ((await res.json()) as { code?: string }).code
    } catch {
      // resposta sem corpo JSON
    }
    throw new ApiError(res.status, code)
  }

  if (res.status === 204) return undefined as T
  return (await res.json()) as T
}

export const api = {
  me: () => request<Me>('/api/me'),
  sources: () => request<Source[]>('/api/me/sources'),
  agenda: (from: string, to: string, sources: string[]) => {
    const params = new URLSearchParams({ from, to })
    for (const s of sources) params.append('source', s)
    return request<AgendaResponse>(`/api/me/agenda?${params}`)
  },
  logout: () => request<void>('/auth/logout', { method: 'POST' }),
}

/** O login é uma navegação (redirects para o Google e de volta), não um fetch. */
export function loginUrl(consent = false) {
  const params = new URLSearchParams({ returnUrl: window.location.pathname })
  if (consent) params.set('consent', 'true')
  return `/auth/login?${params}`
}

export const isUnauthorized = (e: unknown) => e instanceof ApiError && e.status === 401
export const needsReconnect = (e: unknown) => e instanceof ApiError && e.code === 'google_reconnect_required'
