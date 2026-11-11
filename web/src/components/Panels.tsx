import { loginUrl, type Group, type Me, type Source } from '../lib/api'
import { AlertIcon } from './icons'
import { SidebarSection } from './Sidebar'
import { Avatar, buttonPrimary, buttonSecondary, ColorCheck } from './ui'

// ===================== Modo pessoal (sem grupo) =====================

interface PersonalPanelProps {
  me: Me
  sources: Source[] | undefined
  loading: boolean
  selected: string[]
  failed: string[]
  colorFor: (sourceId: string) => string
  onToggle: (sourceId: string) => void
  onCreateGroup: () => void
}

export function PersonalPanel({ me, sources, loading, selected, failed, colorFor, onToggle, onCreateGroup }: PersonalPanelProps) {
  const calendars = sources?.filter((s) => s.kind === 'calendar') ?? []
  const taskLists = sources?.filter((s) => s.kind === 'taskList') ?? []
  const rowProps = { selected, failed, colorFor, onToggle }

  return (
    <>
      <div className="rounded-2xl border border-line bg-surface p-4">
        <p className="text-sm font-semibold">Melhor a dois</p>
        <p className="mt-1 text-xs text-pretty text-muted">
          Crie um grupo e convide sua pessoa para ver as agendas de vocês lado a lado.
        </p>
        <button onClick={onCreateGroup} className={`${buttonPrimary} mt-3 h-9 w-full`}>
          Criar grupo
        </button>
      </div>

      {loading ? (
        <div className="space-y-2 px-2" aria-label="Carregando agendas">
          {[0, 1, 2].map((i) => (
            <div key={i} className="h-7 animate-pulse rounded-lg bg-surface-2" />
          ))}
        </div>
      ) : (
        <>
          <SidebarSection title="Agendas">
            <SourceRows items={calendars} {...rowProps} />
          </SidebarSection>
          {me.google.canReadTasks ? (
            taskLists.length > 0 && (
              <SidebarSection title="Tarefas">
                <SourceRows items={taskLists} {...rowProps} />
              </SidebarSection>
            )
          ) : (
            <p className="px-2 text-xs text-muted">
              Tarefas desativadas.{' '}
              <a href={loginUrl(true)} className="font-medium text-accent hover:underline">
                Permitir acesso
              </a>
            </p>
          )}
        </>
      )}
    </>
  )
}

function SourceRows({ items, selected, failed, colorFor, onToggle }: Omit<PersonalPanelProps, 'me' | 'sources' | 'loading' | 'onCreateGroup'> & { items: Source[] }) {
  return (
    <ul className="space-y-0.5">
      {items.map((source) => {
        const checked = selected.includes(source.id)
        return (
          <li key={source.id}>
            <label className="flex cursor-pointer items-center gap-3 rounded-lg px-2 py-1.5 text-sm transition hover:bg-surface-2">
              <input type="checkbox" className="peer sr-only" checked={checked} onChange={() => onToggle(source.id)} />
              <ColorCheck checked={checked} color={colorFor(source.id)} />
              <span className="min-w-0 flex-1 truncate">{source.name}</span>
              {failed.includes(source.id) && (
                <span title="Não foi possível carregar esta agenda" className="text-amber-500">
                  <AlertIcon width={14} height={14} />
                </span>
              )}
            </label>
          </li>
        )
      })}
    </ul>
  )
}

// ===================== Modo grupo =====================

interface GroupPanelProps {
  group: Group
  hidden: Set<string>
  unavailable: string[]
  onToggleMember: (userId: string) => void
  onInvite: () => void
  onSharing: () => void
  onLeave: () => void
}

export function GroupPanel({ group, hidden, unavailable, onToggleMember, onInvite, onSharing, onLeave }: GroupPanelProps) {
  const alone = group.members.length === 1
  const canInvite = group.members.length < group.maxMembers

  return (
    <>
      <SidebarSection
        title={group.name}
        action={
          canInvite && !alone && (
            <button onClick={onInvite} className="text-xs font-medium text-accent hover:underline">
              + Convidar
            </button>
          )
        }
      >
        <ul className="space-y-0.5">
          {group.members.map((m) => {
            const visible = !hidden.has(m.userId)
            const disconnected = m.needsReconnect || unavailable.includes(m.userId)
            const status = disconnected
              ? 'Precisa reconectar o Google'
              : m.sharedSources === 0
                ? 'Ainda não compartilhou agendas'
                : `${m.sharedSources} ${m.sharedSources === 1 ? 'agenda' : 'agendas'}`

            return (
              <li key={m.userId}>
                {/* Clicar na pessoa mostra/esconde os eventos dela: filtro rápido "só eu / só ela / os dois". */}
                <label className="flex cursor-pointer items-center gap-3 rounded-xl px-2 py-2 transition hover:bg-surface-2">
                  <input type="checkbox" className="peer sr-only" checked={visible} onChange={() => onToggleMember(m.userId)} />
                  <Avatar name={m.name} url={m.avatarUrl} color={m.color} size={30} />
                  <div className={`min-w-0 flex-1 transition ${visible ? '' : 'opacity-40'}`}>
                    <div className="truncate text-sm font-medium">
                      {m.name}
                      {m.isMe && <span className="font-normal text-muted"> (você)</span>}
                    </div>
                    <div className={`flex items-center gap-1 truncate text-xs ${disconnected ? 'text-amber-500' : 'text-muted'}`}>
                      {disconnected && <AlertIcon width={12} height={12} />}
                      {status}
                    </div>
                  </div>
                  <ColorCheck checked={visible} color={m.color} />
                </label>
              </li>
            )
          })}
        </ul>
      </SidebarSection>

      {alone && (
        <div className="rounded-2xl border border-line bg-surface p-4">
          <p className="text-sm font-semibold">Falta sua pessoa</p>
          <p className="mt-1 text-xs text-pretty text-muted">
            Gere um link de convite. Ele vale por 48 horas e funciona uma única vez.
          </p>
          <button onClick={onInvite} className={`${buttonPrimary} mt-3 h-9 w-full`}>
            Convidar
          </button>
        </div>
      )}

      <button onClick={onSharing} className={`${buttonSecondary} h-9 w-full`}>
        O que eu compartilho
      </button>

      <button onClick={onLeave} className="mt-auto self-start px-2 text-xs text-muted underline-offset-4 hover:text-ink hover:underline">
        Sair do grupo
      </button>
    </>
  )
}
