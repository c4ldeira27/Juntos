import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { api, ApiError, needsReconnect, type AgendaItem, type Group, type GroupSummary, type Me } from '../lib/api'
import { colorAt } from '../lib/config'
import { PERSONAL, useSelectedSources } from '../lib/hooks'
import { CalendarView, type DateRange } from './CalendarView'
import { CreateGroupDialog, InviteDialog, SharingDialog } from './GroupDialogs'
import { GroupSettingsDialog } from './GroupSettingsDialog'
import { GroupSwitcher } from './GroupSwitcher'
import { GroupPanel, LeaveGroupButton, PersonalPanel } from './Panels'
import { Sidebar } from './Sidebar'

const REFRESH_INTERVAL = 5 * 60_000

interface AgendaScreenProps {
  me: Me
  groups: GroupSummary[]
  /** PERSONAL or a group id. */
  selected: string
  /** null goes back to the default view (first group, or personal). */
  onSelect: (id: string | null) => void
}

/** The switcher picks the view: my own calendars, or one group's unified agenda (one color per person). */
export function AgendaScreen({ me, groups, selected, onSelect }: AgendaScreenProps) {
  const [creating, setCreating] = useState(false)
  const switcher = (
    <GroupSwitcher groups={groups} selected={selected} onSelect={onSelect} onCreate={() => setCreating(true)} />
  )

  return (
    <>
      {selected === PERSONAL ? (
        <PersonalAgenda me={me} switcher={switcher} hasGroups={groups.length > 0} onCreateGroup={() => setCreating(true)} />
      ) : (
        // "key" forces a remount when the group changes: filters and dialogs don't leak between groups.
        <GroupAgenda key={selected} me={me} groupId={selected} switcher={switcher} onGone={() => onSelect(null)} />
      )}
      {creating && <CreateGroupDialog onClose={() => setCreating(false)} onCreated={onSelect} />}
    </>
  )
}

// ===================== Personal =====================

interface PersonalAgendaProps {
  me: Me
  switcher: ReactNode
  hasGroups: boolean
  onCreateGroup: () => void
}

function PersonalAgenda({ me, switcher, hasGroups, onCreateGroup }: PersonalAgendaProps) {
  const [range, setRange] = useState<DateRange | null>(null)

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
    <AgendaLayout
      me={me}
      switcher={switcher}
      panel={
        <PersonalPanel
          me={me}
          sources={sourcesQuery.data}
          loading={sourcesQuery.isPending}
          selected={selected}
          failed={agendaQuery.data?.failedSources ?? []}
          colorFor={colorFor}
          onToggle={toggle}
          hasGroups={hasGroups}
          onCreateGroup={onCreateGroup}
        />
      }
      items={selected.length > 0 ? (agendaQuery.data?.items ?? []) : []}
      colorOf={colorOf}
      fetching={agendaQuery.isFetching || sourcesQuery.isFetching}
      failed={agendaQuery.isError && !needsReconnect(agendaQuery.error)}
      onRetry={() => agendaQuery.refetch()}
      onRangeChange={setRange}
    />
  )
}

// ===================== Group =====================

interface GroupAgendaProps {
  me: Me
  groupId: string
  switcher: ReactNode
  /** The group no longer exists for me (left, removed or deleted). */
  onGone: () => void
}

function GroupAgenda({ me, groupId, switcher, onGone }: GroupAgendaProps) {
  const queryClient = useQueryClient()
  const groupQuery = useQuery({ queryKey: ['group', groupId], queryFn: () => api.group(groupId) })

  // Removed from the group by an admin (or the group was deleted) while it was selected: 404 → go to another view.
  const gone = groupQuery.error instanceof ApiError && groupQuery.error.status === 404
  useEffect(() => {
    if (!gone) return
    void queryClient.invalidateQueries({ queryKey: ['groups'] })
    onGone()
  }, [gone, onGone, queryClient])

  if (!groupQuery.data)
    return (
      <AgendaLayout
        me={me}
        switcher={switcher}
        panel={<PanelSkeleton />}
        items={[]}
        colorOf={() => colorAt(0)}
        fetching={groupQuery.isFetching}
        failed={groupQuery.isError && !gone}
        onRetry={() => groupQuery.refetch()}
        onRangeChange={() => {}}
      />
    )

  return <LoadedGroupAgenda me={me} group={groupQuery.data} switcher={switcher} onGone={onGone} />
}

type GroupDialog = 'invite' | 'sharing' | 'settings' | null

function LoadedGroupAgenda({ me, group, switcher, onGone }: { me: Me; group: Group; switcher: ReactNode; onGone: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [range, setRange] = useState<DateRange | null>(null)
  const [hidden, setHidden] = useState<Set<string>>(() => new Set())
  // Someone who just joined (or created) this group hasn't chosen what to share in it yet: open that choice right away.
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
    queryClient.removeQueries({ queryKey: ['group', group.id] })
    queryClient.removeQueries({ queryKey: ['group-agenda', group.id] })
    await queryClient.invalidateQueries({ queryKey: ['groups'] })
    onGone()
  }

  const closeDialog = useCallback(() => setDialog(null), [])

  return (
    <>
      <AgendaLayout
        me={me}
        switcher={switcher}
        panel={
          <GroupPanel
            group={group}
            hidden={hidden}
            unavailable={agendaQuery.data?.unavailableMembers ?? []}
            onToggleMember={toggleMember}
            onInvite={() => setDialog('invite')}
            onSharing={() => setDialog('sharing')}
            onSettings={() => setDialog('settings')}
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
      {dialog === 'settings' && <GroupSettingsDialog group={group} onClose={closeDialog} onDeleted={onGone} />}
    </>
  )
}

function PanelSkeleton() {
  return (
    <div className="space-y-2 px-2" aria-hidden>
      {[0, 1, 2].map((i) => (
        <div key={i} className="h-10 animate-pulse rounded-xl bg-surface-2" />
      ))}
    </div>
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
  switcher: ReactNode
  panel: ReactNode
  footer?: ReactNode
  items: AgendaItem[]
  colorOf: (item: AgendaItem) => string
  fetching: boolean
  failed: boolean
  onRetry: () => void
  onRangeChange: (range: DateRange) => void
}

function AgendaLayout({ me, switcher, panel, footer, items, colorOf, fetching, failed, onRetry, onRangeChange }: AgendaLayoutProps) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [sidebarOpen, setSidebarOpen] = useState(false)

  async function logout() {
    await api.logout()
    queryClient.clear()
    window.location.href = '/'
  }

  const sidebar = (onClose?: () => void) => (
    <Sidebar me={me} onLogout={logout} onClose={onClose} switcher={switcher} footer={footer}>
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
