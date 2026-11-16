import { useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { GroupSummary } from '../lib/api'
import { PERSONAL } from '../lib/hooks'
import { CheckIcon, ChevronDown, PersonIcon } from './icons'

interface GroupSwitcherProps {
  groups: GroupSummary[]
  /** PERSONAL or a group id. */
  selected: string
  onSelect: (id: string) => void
  onCreate: () => void
}

/** Chooses what the calendar shows: my own calendars or one of my groups. */
export function GroupSwitcher({ groups, selected, onSelect, onCreate }: GroupSwitcherProps) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  const current = groups.find((g) => g.id === selected)

  function choose(id: string) {
    setOpen(false)
    onSelect(id)
  }

  return (
    <div className="relative">
      <button
        onClick={() => setOpen((o) => !o)}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-label={t('switcher.label')}
        className="flex w-full items-center gap-3 rounded-2xl border border-line bg-surface px-3 py-2.5 text-left transition hover:bg-surface-2"
      >
        {current ? <ColorDot color={current.myColor} /> : <PersonIcon width={16} height={16} className="text-muted" />}
        <div className="min-w-0 flex-1">
          <div className="truncate text-sm font-semibold">{current ? current.name : t('switcher.personal')}</div>
          <div className="truncate text-xs text-muted">
            {current ? t('switcher.members', { count: current.memberCount }) : t('switcher.personalHint')}
          </div>
        </div>
        <ChevronDown width={16} height={16} className={`text-muted transition ${open ? 'rotate-180' : ''}`} />
      </button>

      {open && (
        <>
          {/* Invisible layer: a click anywhere outside closes the menu. */}
          <div className="fixed inset-0 z-20" onClick={() => setOpen(false)} />
          <ul
            role="listbox"
            className="absolute inset-x-0 top-full z-30 mt-1.5 max-h-80 overflow-y-auto rounded-2xl border border-line bg-surface p-1.5 shadow-xl"
          >
            <Option active={selected === PERSONAL} onClick={() => choose(PERSONAL)}>
              <PersonIcon width={16} height={16} className="text-muted" />
              <span className="flex-1 truncate">{t('switcher.personal')}</span>
            </Option>

            {groups.length > 0 && (
              <li className="px-2.5 pt-2.5 pb-1 text-[11px] font-semibold tracking-wider text-muted uppercase">
                {t('switcher.groups')}
              </li>
            )}
            {groups.map((g) => (
              <Option key={g.id} active={selected === g.id} onClick={() => choose(g.id)}>
                <ColorDot color={g.myColor} />
                <span className="flex-1 truncate">{g.name}</span>
                <span className="text-xs text-muted">{g.memberCount}</span>
              </Option>
            ))}

            <li className="mt-1 border-t border-line pt-1">
              <button
                onClick={() => {
                  setOpen(false)
                  onCreate()
                }}
                className="w-full rounded-xl px-2.5 py-2 text-left text-sm font-medium text-accent transition hover:bg-surface-2"
              >
                {t('switcher.create')}
              </button>
            </li>
          </ul>
        </>
      )}
    </div>
  )
}

function Option({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <li role="option" aria-selected={active}>
      <button
        onClick={onClick}
        className={`flex w-full items-center gap-2.5 rounded-xl px-2.5 py-2 text-left text-sm transition hover:bg-surface-2 ${active ? 'font-semibold' : ''}`}
      >
        {children}
        {active && <CheckIcon width={14} height={14} className="text-accent" />}
      </button>
    </li>
  )
}

/** The person's own color in that group: same color their events have there. */
function ColorDot({ color }: { color: string }) {
  return <span className="size-3 shrink-0 rounded-full" style={{ background: color }} />
}
