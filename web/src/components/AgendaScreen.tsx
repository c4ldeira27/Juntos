import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { api, needsReconnect, type AgendaItem, type Group, type Me } from '../lib/api'
import { colorAt } from '../lib/config'
import { useSelectedSources } from '../lib/hooks'
import { CalendarView, type DateRange } from './CalendarView'
import { CreateGroupDialog, InviteDialog, SharingDialog } from './GroupDialogs'
import { GroupPanel, LeaveGroupButton, PersonalPanel } from './Panels'
import { Sidebar } from './Sidebar'

const REFRESH_INTERVAL = 5 * 60_000

/** No group: personal agenda (M1). With a group: unified agenda, one color per person. */
export function AgendaScreen({ me, group }: { me: Me; group: Group | null }) {
  // "key" forces a remount when the group changes: state such as filters and dialogs doesn't leak between groups.
  return group ? <GroupAgenda key={group.id} me={me} group={group} /> : <PersonalAgenda me={me} />
}

// ===================== Personal =====================

function PersonalAgenda({ me }: { me: Me }) {
  const [range, setRange] = useState<DateRange | null>(null)
  const [creating, setCreating] = useState(false)

  const sourcesQuery = useQuery({ queryKey: ['sources'], queryFn: api.sources, staleTime: 5 * 60_000 })
  const { selected, toggle } = useSelectedSources(sourcesQuery.data)

  const agendaQuery = useQuery({
    queryKey: ['agenda', range?.from, range?.to, selected],
    queryFn: () => api.agenda(range!.from, range!.to, selected),
    enabled: range !== null && selected.length > 0,
    // When switching weeks, keeps the old events on screen until the new ones arrive (no flicker).
    placeholderData: keepPreviousData,
    refetchInterval: REFRESH_INTERVAL,
  })
  useReconnectOn(sourcesQuery.error, agendaQuery.error)

  const colors = useMemo(
    () => new Map((sourcesQuery.data ?? []).map((s, i) => [s.id, colorAt(i)])),
    [sourcesQuery.data],
  )
  const colorFor = useCallback((sourceId: string) => colors.get(sourceId) ?? colorAt(0), [colors])
  const colorOf = useCallback((item: AgendaItem) => colorFor(item.sourceId), [colorFor])

  return (
    <>
      <AgendaLayout
        me={me}
        panel={
          <PersonalPanel
            me={me}
            sources={sourcesQuery.data}
            loading={sourcesQuery.isPending}
            selected={selected}
            failed={agendaQuery.data?.failedSources ?? []}
            colorFor={colorFor}
            onToggle={toggle}
            onCreateGroup={() => setCreating(true)}
          />
        }
        items={selected.length > 0 ? (agendaQuery.data?.items ?? []) : []}
        colorOf={colorOf}
        fetching={agendaQuery.isFetching || sourcesQuery.isFetching}
        failed={agendaQuery.isError && !needsReconnect(agendaQuery.error)}
        onRetry={() => agendaQuery.refetch()}
        onRangeChange={setRange}
      />
      {creating && <CreateGroupDialog onClose={() => setCreating(false)} />}
    </>
  )
}

// ===================== Group =====================

type GroupDialog = 'invite' | 'sharing' | null

function GroupAgenda({ me, group }: { me: Me; group: Group }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [range, setRange] = useState<DateRange | null>(null)
  const [hidden, setHidden] = useState<Set<string>>(() => new Set())
  // Someone who just joined (or created) hasn't chosen what to share yet: open that choice right away.
  const [dialog, setDialog] = useState<GroupDialog>(() =>
    group.members.find((m) => m.isMe)?.sharingConfigured ? null : 'sharing',
  )

  const agendaQuery = useQuery({
    queryKey: ['group-agenda', group.id, range?.from, range?.to],
    queryFn: () => api.groupAgenda(group.id, range!.from, range!.to),
    enabled: range !== null,
    placeholderData: keepPreviousData,
    refetchInterval: REFRESH_INTERVAL,
  })
  useReconnectOn(agendaQuery.error)

  const colors = useMemo(() => new Map(group.members.map((m) => [m.userId, m.color])), [group.members])
  const colorOf = useCallback(
    (item: AgendaItem) => (item.memberId && colors.get(item.memberId)) || colorAt(0),
    [colors],
  )
  const items = useMemo(
    () => (agendaQuery.data?.items ?? []).filter((i) => !i.memberId || !hidden.has(i.memberId)),
    [agendaQuery.data, hidden],
  )

  function toggleMember(userId: string) {
    setHidden((prev) => {
      const next = new Set(prev)
      if (next.has(userId)) next.delete(userId)
      else next.add(userId)
      return next
    })
  }

  async function leave() {
    const message = group.members.length > 1 ? t('group.confirmLeave') : t('group.confirmLeaveLast')
    if (!window.confirm(message)) return
    await api.leaveGroup(group.id)
    queryClient.removeQueries({ queryKey: ['group-agenda'] })
    queryClient.setQueryData(['group'], null)
  }

  const closeDialog = useCallback(() => setDialog(null), [])

  return (
    <>
      <AgendaLayout
        me={me}
        panel={
          <GroupPanel
            group={group}
            hidden={hidden}
            unavailable={agendaQuery.data?.unavailableMembers ?? []}
            onToggleMember={toggleMember}
            onInvite={() => setDialog('invite')}
            onSharing={() => setDialog('sharing')}
          />
        }
        footer={<LeaveGroupButton onLeave={leave} />}
        items={items}
        colorOf={colorOf}
        fetching={agendaQuery.isFetching}
        failed={agendaQuery.isError && !needsReconnect(agendaQuery.error)}
        onRetry={() => agendaQuery.refetch()}
        onRangeChange={setRange}
      />
      {dialog === 'invite' && <InviteDialog groupId={group.id} onClose={closeDialog} />}
      {dialog === 'sharing' && <SharingDialog group={group} onClose={closeDialog} />}
    </>
  )
}

// ===================== Shared =====================

/** If Google revoked access mid-session, reload /me: App switches to the reconnect screen. */
function useReconnectOn(...errors: unknown[]) {
  const queryClient = useQueryClient()
  const shouldReconnect = errors.some(needsReconnect)
  useEffect(() => {
    if (shouldReconnect) void queryClient.invalidateQueries({ queryKey: ['me'] })
  }, [shouldReconnect, queryClient])
}

interface AgendaLayoutProps {
  me: Me
  panel: ReactNode
  footer?: ReactNode
  items: AgendaItem[]
  colorOf: (item: AgendaItem) => string
  fetching: boolean
  failed: boolean
  onRetry: () => void
  onRangeChange: (range: DateRange) => void
}

function AgendaLayout({ me, panel, footer, items, colorOf, fetching, failed, onRetry, onRangeChange }: AgendaLayoutProps) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [sidebarOpen, setSidebarOpen] = useState(false)

  async function logout() {
    await api.logout()
    queryClient.clear()
    window.location.href = '/'
  }

  const sidebar = (onClose?: () => void) => (
    <Sidebar me={me} onLogout={logout} onClose={onClose} footer={footer}>
      {panel}
    </Sidebar>
  )

  return (
    <div className="flex h-full">
      <div className="hidden md:block">{sidebar()}</div>

      {/* Drawer on mobile */}
      {sidebarOpen && (
        <div className="fixed inset-0 z-40 md:hidden">
          <div className="absolute inset-0 bg-black/30 backdrop-blur-[2px]" onClick={() => setSidebarOpen(false)} />
          <div className="absolute inset-y-0 left-0 shadow-2xl">{sidebar(() => setSidebarOpen(false))}</div>
        </div>
      )}

      <main className="flex min-w-0 flex-1 flex-col p-2 md:py-3 md:pr-3 md:pl-0">
        {failed && (
          <div className="mb-2 flex items-center justify-between rounded-xl border border-amber-500/30 bg-amber-500/10 px-4 py-2 text-sm">
            <span>{t('calendar.loadFailed')}</span>
            <button onClick={onRetry} className="font-medium text-accent hover:underline">
              {t('common.retry')}
            </button>
          </div>
        )}
        <div className="cal min-h-0 flex-1 overflow-hidden rounded-2xl border border-line bg-surface shadow-sm">
          <CalendarView
            items={items}
            colorOf={colorOf}
            fetching={fetching}
            onRangeChange={onRangeChange}
            onOpenSidebar={() => setSidebarOpen(true)}
          />
        </div>
      </main>
    </div>
  )
}
