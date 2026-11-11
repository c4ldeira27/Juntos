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
  /** Dono do item na agenda do grupo (define a cor). */
  memberId?: string | null
}

export interface AgendaResponse {
  items: AgendaItem[]
  failedSources: string[]
}

export interface GroupAgendaResponse extends AgendaResponse {
  /** Membros com a conexão Google expirada: a agenda deles não pôde ser lida. */
  unavailableMembers: string[]
}

export type Visibility = 'details' | 'busyOnly'

export interface Member {
  userId: string
  name: string
  avatarUrl: string | null
  color: string
  role: 'owner' | 'member'
  isMe: boolean
  needsReconnect: boolean
  sharingConfigured: boolean
  sharedSources: number
}

export interface Group {
  id: string
  name: string
  members: Member[]
  maxMembers: number
}

export interface SharingItem {
  sourceId: string
  kind: SourceKind
  name: string
  primary: boolean
  /** null = não compartilhada */
  visibility: Visibility | null
}

export interface SharingChoice {
  sourceId: string
  name: string
  visibility: Visibility
}

export type InviteStatus = 'valid' | 'expired' | 'used' | 'alreadyMember' | 'inAnotherGroup' | 'full'

export interface InvitePreview {
  groupName: string
  invitedBy: string
  expiresAt: string
  status: InviteStatus
}

export interface Invite {
  url: string
  expiresAt: string
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
  const res = await fetch(path, {
    ...init,
    headers: { Accept: 'application/json', ...(init?.headers as Record<string, string> | undefined) },
  })

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

  // ---- Grupo ----
  /** 204 = sem grupo. O TanStack Query não aceita undefined como dado, por isso vira null. */
  currentGroup: async () => (await request<Group | undefined>('/api/groups/current')) ?? null,
  createGroup: (name: string) => request<Group>('/api/groups', withJson('POST', { name })),
  leaveGroup: (groupId: string) => request<void>(`/api/groups/${groupId}/members/me`, { method: 'DELETE' }),
  createInvite: (groupId: string) => request<Invite>(`/api/groups/${groupId}/invites`, { method: 'POST' }),
  sharing: (groupId: string) => request<SharingItem[]>(`/api/groups/${groupId}/sharing`),
  updateSharing: (groupId: string, sources: SharingChoice[]) =>
    request<void>(`/api/groups/${groupId}/sharing`, withJson('PUT', { sources })),
  groupAgenda: (groupId: string, from: string, to: string) =>
    request<GroupAgendaResponse>(`/api/groups/${groupId}/agenda?${new URLSearchParams({ from, to })}`),

  // ---- Convite ----
  invitePreview: (token: string) => request<InvitePreview>(`/api/invites/${encodeURIComponent(token)}`),
  acceptInvite: (token: string) =>
    request<Group>(`/api/invites/${encodeURIComponent(token)}/accept`, { method: 'POST' }),
}

function withJson(method: string, body: unknown): RequestInit {
  return { method, body: JSON.stringify(body), headers: { 'Content-Type': 'application/json' } }
}

/** Token do convite quando a URL é /convite/{token}. */
export function inviteTokenFromPath(pathname = window.location.pathname) {
  return /^\/convite\/([A-Za-z0-9_-]+)\/?$/.exec(pathname)?.[1] ?? null
}

/** O login é uma navegação (redirects para o Google e de volta), não um fetch. */
export function loginUrl(consent = false) {
  const params = new URLSearchParams({ returnUrl: window.location.pathname })
  if (consent) params.set('consent', 'true')
  return `/auth/login?${params}`
}

export const isUnauthorized = (e: unknown) => e instanceof ApiError && e.status === 401
export const needsReconnect = (e: unknown) => e instanceof ApiError && e.code === 'google_reconnect_required'
