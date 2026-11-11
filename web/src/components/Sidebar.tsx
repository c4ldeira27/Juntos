import { loginUrl, type Me, type Source } from '../lib/api'
import { APP_NAME } from '../lib/config'
import { AlertIcon, CloseIcon, Logo, LogoutIcon } from './icons'

interface SidebarProps {
  me: Me
  sources: Source[] | undefined
  loading: boolean
  selected: string[]
  failed: string[]
  colorFor: (sourceId: string) => string
  onToggle: (sourceId: string) => void
  onLogout: () => void
  onClose?: () => void
}

export function Sidebar({ me, sources, loading, selected, failed, colorFor, onToggle, onLogout, onClose }: SidebarProps) {
  const calendars = sources?.filter((s) => s.kind === 'calendar') ?? []
  const taskLists = sources?.filter((s) => s.kind === 'taskList') ?? []

  return (
    <aside className="flex h-full w-72 flex-col gap-6 overflow-y-auto bg-bg px-4 py-5">
      <div className="flex items-center gap-2.5 px-2">
        <Logo size={26} />
        <span className="text-lg font-semibold tracking-tight">{APP_NAME}</span>
        {onClose && (
          <button onClick={onClose} className="ml-auto rounded-lg p-1.5 text-muted hover:bg-surface-2" aria-label="Fechar menu">
            <CloseIcon />
          </button>
        )}
      </div>

      <div className="flex items-center gap-3 rounded-2xl border border-line bg-surface p-3">
        {me.avatarUrl ? (
          <img src={me.avatarUrl} alt="" referrerPolicy="no-referrer" className="size-9 rounded-full" />
        ) : (
          <div className="grid size-9 place-items-center rounded-full bg-accent text-sm font-semibold text-white">
            {me.name.charAt(0).toUpperCase()}
          </div>
        )}
        <div className="min-w-0 flex-1">
          <div className="truncate text-sm font-medium">{me.name}</div>
          <div className="truncate text-xs text-muted">{me.email}</div>
        </div>
        <button onClick={onLogout} className="rounded-lg p-1.5 text-muted transition hover:bg-surface-2 hover:text-ink" aria-label="Sair" title="Sair">
          <LogoutIcon width={16} height={16} />
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
          <SourceGroup title="Agendas" items={calendars} {...{ selected, failed, colorFor, onToggle }} />
          {me.google.canReadTasks ? (
            taskLists.length > 0 && <SourceGroup title="Tarefas" items={taskLists} {...{ selected, failed, colorFor, onToggle }} />
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
    </aside>
  )
}

interface SourceGroupProps {
  title: string
  items: Source[]
  selected: string[]
  failed: string[]
  colorFor: (sourceId: string) => string
  onToggle: (sourceId: string) => void
}

function SourceGroup({ title, items, selected, failed, colorFor, onToggle }: SourceGroupProps) {
  return (
    <section>
      <h2 className="mb-1.5 px-2 text-[11px] font-semibold tracking-wider text-muted uppercase">{title}</h2>
      <ul className="space-y-0.5">
        {items.map((source) => {
          const checked = selected.includes(source.id)
          const color = colorFor(source.id)
          return (
            <li key={source.id}>
              <label className="group flex cursor-pointer items-center gap-3 rounded-lg px-2 py-1.5 text-sm transition hover:bg-surface-2">
                <input type="checkbox" className="peer sr-only" checked={checked} onChange={() => onToggle(source.id)} />
                {/* Checkbox customizado: a própria cor da agenda vira o indicador. */}
                <span
                  className="grid size-4 shrink-0 place-items-center rounded-[5px] border-2 transition peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-accent"
                  style={{ borderColor: color, background: checked ? color : 'transparent' }}
                >
                  {checked && (
                    <svg viewBox="0 0 12 12" className="size-2.5 text-white" aria-hidden>
                      <path d="m2.5 6 2.5 2.5 4.5-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
                    </svg>
                  )}
                </span>
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
    </section>
  )
}
