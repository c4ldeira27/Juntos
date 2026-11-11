import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { api, needsReconnect, type Me } from '../lib/api'
import { colorAt } from '../lib/config'
import { useSelectedSources } from '../lib/hooks'
import { CalendarView, type DateRange } from './CalendarView'
import { Sidebar } from './Sidebar'

export function AgendaScreen({ me }: { me: Me }) {
  const queryClient = useQueryClient()
  const [range, setRange] = useState<DateRange | null>(null)
  const [sidebarOpen, setSidebarOpen] = useState(false)

  const sourcesQuery = useQuery({ queryKey: ['sources'], queryFn: api.sources, staleTime: 5 * 60_000 })
  const { selected, toggle } = useSelectedSources(sourcesQuery.data)

  const agendaQuery = useQuery({
    queryKey: ['agenda', range?.from, range?.to, selected],
    queryFn: () => api.agenda(range!.from, range!.to, selected),
    enabled: range !== null && selected.length > 0,
    // Ao trocar de semana, mantém os eventos antigos na tela até os novos chegarem (sem "piscar").
    placeholderData: keepPreviousData,
    // Atualiza sozinho de tempos em tempos e ao voltar para a aba (padrão do TanStack Query).
    refetchInterval: 5 * 60_000,
  })

  // Se o Google revogou o acesso no meio do uso, recarrega o /me: o App troca para a tela de reconexão.
  useEffect(() => {
    if (needsReconnect(sourcesQuery.error) || needsReconnect(agendaQuery.error))
      void queryClient.invalidateQueries({ queryKey: ['me'] })
  }, [sourcesQuery.error, agendaQuery.error, queryClient])

  const colors = useMemo(
    () => new Map((sourcesQuery.data ?? []).map((s, i) => [s.id, colorAt(i)])),
    [sourcesQuery.data],
  )
  const colorFor = useCallback((id: string) => colors.get(id) ?? colorAt(0), [colors])
  const items = selected.length > 0 ? (agendaQuery.data?.items ?? []) : []

  async function logout() {
    await api.logout()
    queryClient.clear()
    window.location.href = '/'
  }

  const sidebar = (onClose?: () => void) => (
    <Sidebar
      me={me}
      sources={sourcesQuery.data}
      loading={sourcesQuery.isPending}
      selected={selected}
      failed={agendaQuery.data?.failedSources ?? []}
      colorFor={colorFor}
      onToggle={toggle}
      onLogout={logout}
      onClose={onClose}
    />
  )

  return (
    <div className="flex h-full">
      <div className="hidden md:block">{sidebar()}</div>

      {/* Gaveta no celular */}
      {sidebarOpen && (
        <div className="fixed inset-0 z-40 md:hidden">
          <div className="absolute inset-0 bg-black/30 backdrop-blur-[2px]" onClick={() => setSidebarOpen(false)} />
          <div className="absolute inset-y-0 left-0 shadow-2xl">{sidebar(() => setSidebarOpen(false))}</div>
        </div>
      )}

      <main className="flex min-w-0 flex-1 flex-col p-2 md:py-3 md:pr-3 md:pl-0">
        {agendaQuery.isError && !needsReconnect(agendaQuery.error) && (
          <div className="mb-2 flex items-center justify-between rounded-xl border border-amber-500/30 bg-amber-500/10 px-4 py-2 text-sm">
            <span>Não foi possível carregar a agenda.</span>
            <button onClick={() => agendaQuery.refetch()} className="font-medium text-accent hover:underline">
              Tentar de novo
            </button>
          </div>
        )}
        <div className="cal min-h-0 flex-1 overflow-hidden rounded-2xl border border-line bg-surface shadow-sm">
          <CalendarView
            items={items}
            colorFor={colorFor}
            fetching={agendaQuery.isFetching || sourcesQuery.isFetching}
            onRangeChange={setRange}
            onOpenSidebar={() => setSidebarOpen(true)}
          />
        </div>
      </main>
    </div>
  )
}
