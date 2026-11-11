import { useTranslation } from 'react-i18next'
import { loginUrl, type Group, type Me, type Source } from '../lib/api'
import { AlertIcon } from './icons'
import { SidebarSection } from './Sidebar'
import { Avatar, buttonPrimary, buttonSecondary, ColorCheck } from './ui'

// ===================== Personal mode (no group) =====================

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
  const { t } = useTranslation()
  const calendars = sources?.filter((s) => s.kind === 'calendar') ?? []
  const taskLists = sources?.filter((s) => s.kind === 'taskList') ?? []
  const rowProps = { selected, failed, colorFor, onToggle }

  return (
    <>
      <div className="rounded-2xl border border-line bg-surface p-4">
        <p className="text-sm font-semibold">{t('personal.ctaTitle')}</p>
        <p className="mt-1 text-xs text-pretty text-muted">{t('personal.ctaBody')}</p>
        <button onClick={onCreateGroup} className={`${buttonPrimary} mt-3 h-9 w-full`}>
          {t('personal.createGroup')}
        </button>
      </div>

      {loading ? (
        <div className="space-y-2 px-2" aria-label={t('personal.loadingCalendars')}>
          {[0, 1, 2].map((i) => (
            <div key={i} className="h-7 animate-pulse rounded-lg bg-surface-2" />
          ))}
        </div>
      ) : (
        <>
          <SidebarSection title={t('personal.calendars')}>
            <SourceRows items={calendars} {...rowProps} />
          </SidebarSection>
          {me.google.canReadTasks ? (
            taskLists.length > 0 && (
              <SidebarSection title={t('personal.tasks')}>
                <SourceRows items={taskLists} {...rowProps} />
              </SidebarSection>
            )
          ) : (
            <p className="px-2 text-xs text-muted">
              {t('personal.tasksDisabled')}{' '}
              <a href={loginUrl(true)} className="font-medium text-accent hover:underline">
                {t('personal.allowAccess')}
              </a>
            </p>
          )}
        </>
      )}
    </>
  )
}

function SourceRows({
  items,
  selected,
  failed,
  colorFor,
  onToggle,
}: Pick<PersonalPanelProps, 'selected' | 'failed' | 'colorFor' | 'onToggle'> & { items: Source[] }) {
  const { t } = useTranslation()
  return (
    <ul className="space-y-0.5">
      {items.map((source) => {
        const checked = selected.includes(source.id)
        return (
          <li key={source.id}>
            <label className="flex cursor-pointer items-center gap-3 rounded-lg px-2 py-1.5 text-sm transition hover:bg-surface-2">
              <input type="checkbox" className="peer sr-only" checked={checked} onChange={() => onToggle(source.id)} />
              <ColorCheck checked={checked} color={colorFor(source.id)} />
              <span className="min-w-0 flex-1 truncate">{source.name ?? t('personal.untitledList')}</span>
              {failed.includes(source.id) && (
                <span title={t('personal.sourceFailed')} className="text-amber-500">
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

// ===================== Group mode =====================

interface GroupPanelProps {
  group: Group
  hidden: Set<string>
  unavailable: string[]
  onToggleMember: (userId: string) => void
  onInvite: () => void
  onSharing: () => void
}

export function GroupPanel({ group, hidden, unavailable, onToggleMember, onInvite, onSharing }: GroupPanelProps) {
  const { t } = useTranslation()
  const alone = group.members.length === 1
  const canInvite = group.members.length < group.maxMembers

  return (
    <>
      <SidebarSection
        title={group.name}
        action={
          canInvite &&
          !alone && (
            <button onClick={onInvite} className="text-xs font-medium text-accent hover:underline">
              {t('group.invite')}
            </button>
          )
        }
      >
        <ul className="space-y-0.5">
          {group.members.map((m) => {
            const visible = !hidden.has(m.userId)
            const disconnected = m.needsReconnect || unavailable.includes(m.userId)
            const status = disconnected
              ? t('group.needsReconnect')
              : m.sharedSources === 0
                ? t('group.notShared')
                : t('group.calendars', { count: m.sharedSources })

            return (
              <li key={m.userId}>
                {/* Clicking a person shows/hides their events: quick "just me / just them / both" filter. */}
                <label className="flex cursor-pointer items-center gap-3 rounded-xl px-2 py-2 transition hover:bg-surface-2">
                  <input type="checkbox" className="peer sr-only" checked={visible} onChange={() => onToggleMember(m.userId)} />
                  <Avatar name={m.name} url={m.avatarUrl} color={m.color} size={30} />
                  <div className={`min-w-0 flex-1 transition ${visible ? '' : 'opacity-40'}`}>
                    <div className="truncate text-sm font-medium">
                      {m.name}
                      {m.isMe && <span className="font-normal text-muted"> {t('group.you')}</span>}
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
          <p className="text-sm font-semibold">{t('group.aloneTitle')}</p>
          <p className="mt-1 text-xs text-pretty text-muted">{t('group.aloneBody')}</p>
          <button onClick={onInvite} className={`${buttonPrimary} mt-3 h-9 w-full`}>
            {t('group.inviteButton')}
          </button>
        </div>
      )}

      <button onClick={onSharing} className={`${buttonSecondary} h-9 w-full`}>
        {t('group.sharingButton')}
      </button>
    </>
  )
}

export function LeaveGroupButton({ onLeave }: { onLeave: () => void }) {
  const { t } = useTranslation()
  return (
    <button onClick={onLeave} className="text-xs text-muted underline-offset-4 hover:text-ink hover:underline">
      {t('group.leave')}
    </button>
  )
}
