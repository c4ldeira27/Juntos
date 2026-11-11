import type { DatesSetArg, DayHeaderContentArg, EventContentArg, EventInput } from '@fullcalendar/core'
import ptBrLocale from '@fullcalendar/core/locales/pt-br'
import dayGridPlugin from '@fullcalendar/daygrid'
import listPlugin from '@fullcalendar/list'
import FullCalendar from '@fullcalendar/react'
import timeGridPlugin from '@fullcalendar/timegrid'
import { useMemo, useRef, useState, type CSSProperties } from 'react'
import type { AgendaItem, AgendaItemKind } from '../lib/api'
import { useMediaQuery } from '../lib/hooks'
import { ChevronLeft, ChevronRight, MenuIcon } from './icons'

export interface DateRange {
  from: string
  to: string
}

type ViewName = 'timeGridDay' | 'timeGridWeek' | 'dayGridMonth' | 'listWeek'

const VIEWS: { name: ViewName; label: string }[] = [
  { name: 'timeGridDay', label: 'Dia' },
  { name: 'timeGridWeek', label: 'Semana' },
  { name: 'dayGridMonth', label: 'Mês' },
  { name: 'listWeek', label: 'Lista' },
]

interface EventMeta {
  kind: AgendaItemKind
  color: string
  location: string | null
}

interface CalendarViewProps {
  items: AgendaItem[]
  colorFor: (sourceId: string) => string
  fetching: boolean
  onRangeChange: (range: DateRange) => void
  onOpenSidebar: () => void
}

export function CalendarView({ items, colorFor, fetching, onRangeChange, onOpenSidebar }: CalendarViewProps) {
  const calendarRef = useRef<FullCalendar>(null)
  const isMobile = useMediaQuery('(max-width: 767px)')
  // No celular a grade semanal fica ilegível: começa na lista.
  const [view, setView] = useState<ViewName>(() => (isMobile ? 'listWeek' : 'timeGridWeek'))
  const [title, setTitle] = useState('')

  const events = useMemo<EventInput[]>(
    () =>
      items.map((item) => {
        const color = colorFor(item.sourceId)
        return {
          id: item.id,
          title: item.title ?? 'Ocupado',
          start: item.start,
          end: item.end,
          allDay: item.allDay,
          // A cor também alimenta a "bolinha" da visão de lista.
          color,
          extendedProps: { kind: item.kind, color, location: item.location } satisfies EventMeta,
        }
      }),
    [items, colorFor],
  )

  const calendar = () => calendarRef.current?.getApi()

  function changeView(name: ViewName) {
    setView(name)
    calendar()?.changeView(name)
  }

  function handleDatesSet(arg: DatesSetArg) {
    setTitle(arg.view.title)
    onRangeChange({ from: arg.start.toISOString(), to: arg.end.toISOString() })
  }

  return (
    <div className="flex h-full min-h-0 flex-col">
      <header className="flex flex-wrap items-center gap-2 border-b border-line px-3 py-2.5 md:px-4">
        <button onClick={onOpenSidebar} className="rounded-lg p-2 text-muted hover:bg-surface-2 md:hidden" aria-label="Abrir agendas">
          <MenuIcon />
        </button>
        <button
          onClick={() => calendar()?.today()}
          className="h-8 rounded-full border border-line px-3.5 text-sm font-medium transition hover:bg-surface-2"
        >
          Hoje
        </button>
        <div className="flex">
          <button onClick={() => calendar()?.prev()} className="rounded-full p-1.5 text-muted transition hover:bg-surface-2 hover:text-ink" aria-label="Anterior">
            <ChevronLeft />
          </button>
          <button onClick={() => calendar()?.next()} className="rounded-full p-1.5 text-muted transition hover:bg-surface-2 hover:text-ink" aria-label="Próximo">
            <ChevronRight />
          </button>
        </div>
        <h1 className="min-w-0 flex-1 truncate text-base font-semibold tracking-tight first-letter:uppercase md:text-lg">{title}</h1>

        <div role="tablist" aria-label="Visualização" className="flex rounded-full bg-surface-2 p-0.5">
          {VIEWS.map((v) => (
            <button
              key={v.name}
              role="tab"
              aria-selected={view === v.name}
              onClick={() => changeView(v.name)}
              className={`h-7 rounded-full px-3 text-xs font-medium transition ${
                view === v.name ? 'bg-surface text-ink shadow-sm' : 'text-muted hover:text-ink'
              }`}
            >
              {v.label}
            </button>
          ))}
        </div>
      </header>

      <div className="relative min-h-0 flex-1">
        {/* Barra fina de carregamento: não esconde o calendário enquanto busca a próxima semana. */}
        <div className={`absolute inset-x-0 top-0 z-10 h-0.5 overflow-hidden transition-opacity ${fetching ? 'opacity-100' : 'opacity-0'}`}>
          <div className="h-full w-1/3 animate-[loading_1.1s_ease-in-out_infinite] bg-accent" />
        </div>

        <FullCalendar
          ref={calendarRef}
          plugins={[timeGridPlugin, dayGridPlugin, listPlugin]}
          initialView={view}
          locale={ptBrLocale}
          headerToolbar={false}
          height="100%"
          events={events}
          datesSet={handleDatesSet}
          nowIndicator
          scrollTime="06:00:00"
          allDayText="Dia todo"
          slotLabelFormat={{ hour: '2-digit', minute: '2-digit', hour12: false }}
          eventTimeFormat={{ hour: '2-digit', minute: '2-digit', hour12: false }}
          dayMaxEvents
          eventDisplay="block"
          dayHeaderContent={renderDayHeader}
          eventContent={renderEvent}
          listDayFormat={{ weekday: 'long', day: 'numeric', month: 'short' }}
          listDaySideFormat={false}
          noEventsText={fetching ? 'Carregando…' : 'Nada por aqui nesta semana.'}
        />
      </div>
    </div>
  )
}

/** Cabeçalho do dia no estilo Google: dia da semana pequeno em cima, número grande embaixo. */
function renderDayHeader(arg: DayHeaderContentArg) {
  if (!arg.view.type.startsWith('timeGrid')) return arg.text
  const weekday = arg.date.toLocaleDateString('pt-BR', { weekday: 'short' }).replace('.', '')
  return (
    <div className="dh">
      <span className="dh-wd">{weekday}</span>
      <span className={`dh-num${arg.isToday ? ' today' : ''}`}>{arg.date.getDate()}</span>
    </div>
  )
}

function renderEvent(arg: EventContentArg) {
  const { kind, color, location } = arg.event.extendedProps as EventMeta
  const timed = !arg.event.allDay && arg.view.type.startsWith('timeGrid')

  return (
    <div className={`ev ev-${kind}`} style={{ '--ev': color } as CSSProperties} title={arg.event.title}>
      {kind === 'task' && <span className="ev-check" />}
      <div className="ev-title">{arg.event.title}</div>
      {timed && <div className="ev-meta">{arg.timeText}</div>}
      {timed && location && <div className="ev-meta">{location}</div>}
    </div>
  )
}
