import type { ReactNode } from 'react'
import type { Me } from '../lib/api'
import { APP_NAME } from '../lib/config'
import { CloseIcon, Logo, LogoutIcon } from './icons'
import { Avatar } from './ui'

interface SidebarProps {
  me: Me
  onLogout: () => void
  onClose?: () => void
  /** Conteúdo do modo atual: agendas (pessoal) ou membros (grupo). */
  children: ReactNode
}

export function Sidebar({ me, onLogout, onClose, children }: SidebarProps) {
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
        <Avatar name={me.name} url={me.avatarUrl} color="var(--accent)" size={34} />
        <div className="min-w-0 flex-1">
          <div className="truncate text-sm font-medium">{me.name}</div>
          <div className="truncate text-xs text-muted">{me.email}</div>
        </div>
        <button onClick={onLogout} className="rounded-lg p-1.5 text-muted transition hover:bg-surface-2 hover:text-ink" aria-label="Sair" title="Sair">
          <LogoutIcon width={16} height={16} />
        </button>
      </div>

      {children}
    </aside>
  )
}

export function SidebarSection({ title, action, children }: { title: string; action?: ReactNode; children: ReactNode }) {
  return (
    <section>
      <div className="mb-1.5 flex items-center justify-between px-2">
        <h2 className="text-[11px] font-semibold tracking-wider text-muted uppercase">{title}</h2>
        {action}
      </div>
      {children}
    </section>
  )
}
