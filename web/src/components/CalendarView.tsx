import type { DatesSetArg, DayHeaderContentArg, EventContentArg, EventInput, FormatterInput } from '@fullcalendar/core'
import ptBrLocale from '@fullcalendar/core/locales/pt-br'
import dayGridPlugin from '@fullcalendar/daygrid'
import listPlugin from '@fullcalendar/list'
import FullCalendar from '@fullcalendar/react'
import timeGridPlugin from '@fullcalendar/timegrid'
import { useCallback, useMemo, useRef, useState, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import { currentLanguage } from '../i18n'
import type { AgendaItem, AgendaItemKind } from '../lib/api'
import { useMediaQuery } from '../lib/hooks'
import { ChevronLeft, ChevronRight, MenuIcon } from './icons'
import { Segmented } from './ui'

export interface DateRange {
  from: string
  to: string
}

type ViewName = 'timeGridDay' | 'timeGridWeek' | 'dayGridMonth' | 'listWeek'

interface EventMeta {
  kind: AgendaItemKind
  color: string
  location: string | null
}

// pt-BR uses 24h ("14:00"); English uses 12h ("2 PM").
const TIME_FORMATS: Record<'en' | 'pt-BR', { slot: FormatterInput; event: FormatterInput }> = {
  'pt-BR': {
    slot: { hour: '2-digit', minute: '2-digit', hour12: false },
    event: { hour: '2-digit', minute: '2-digit', hour12: false },
  },
  en: {
    slot: { hour: 'numeric', meridiem: 'short' },
    event: { hour: 'numeric', minute: '2-digit', meridiem: 'short' },
  },
}

interface CalendarViewProps {
  items: AgendaItem[]
  /** Personal: the calendar's color. Group: the person's color. */
  colorOf: (item: AgendaItem) => string
  fetching: boolean
  onRangeChange: (range: DateRange) => void
  onOpenSidebar: () => void
}

export function CalendarView({ items, colorOf, fetching, onRangeChange, onOpenSidebar }: CalendarViewProps) {
  const { t } = useTranslation()
  const language = currentLanguage()
  const calendarRef = useRef<FullCalendar>(null)
  const isMobile = useMediaQuery('(max-width: 767px)')
  // On mobile the week grid is unreadable: start with the list.
  const [view, setView] = useState<ViewName>(() => (isMobile ? 'listWeek' : 'timeGridWeek'))
  const [title, setTitle] = useState('')

  const views: { value: ViewName; label: string }[] = [
    { value: 'timeGridDay', label: t('calendar.views.day') },
    { value: 'timeGridWeek', label: t('calendar.views.week') },
    { value: 'dayGridMonth', label: t('calendar.views.month') },
    { value: 'listWeek', label: t('calendar.views.list') },
  ]

  const events = useMemo<EventInput[]>(
    () =>
      items.map((item) => {
        const color = colorOf(item)
        // The back end sends no UI text: placeholders for busy/untitled items are translated here.
        const title =
          item.title ??
          (item.kind === 'busy'
            ? t('calendar.busy')
            : item.kind === 'task'
              ? t('calendar.untitledTask')
              : t('calendar.untitledEvent'))
        return {
          id: item.id,
          title,
          start: item.start,
          end: item.end,
          allDay: item.allDay,
          // The color also feeds the "dot" in the list view.
          color,
          extendedProps: { kind: item.kind, color, location: item.location } satisfies EventMeta,
        }
      }),
    [items, colorOf, t],
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

  /** Google-style day header: small weekday on top, big day number below. */
  const renderDayHeader = useCallback(
    (arg: DayHeaderContentArg) => {
      if (!arg.view.type.startsWith('timeGrid')) return arg.text
      const weekday = arg.date.toLocaleDateString(language, { weekday: 'short' }).replace('.', '')
      return (
        <div className="dh">
          <span className="dh-wd">{weekday}</span>
          <span className={`dh-num${arg.isToday ? ' today' : ''}`}>{arg.date.getDate()}</span>
        </div>
      )
    },
    [language],
  )

  return (
    <div className="flex h-full min-h-0 flex-col">
      <header className="flex flex-wrap items-center gap-2 border-b border-line px-3 py-2.5 md:px-4">
        <button onClick={onOpenSidebar} className="rounded-lg p-2 text-muted hover:bg-surface-2 md:hidden" aria-label={t('calendar.openSidebar')}>
          <MenuIcon />
        </button>
        <button
          onClick={() => calendar()?.today()}
          className="h-8 rounded-full border border-line px-3.5 text-sm font-medium transition hover:bg-surface-2"
        >
          {t('calendar.today')}
        </button>
        <div className="flex">
          <button onClick={() => calendar()?.prev()} className="rounded-full p-1.5 text-muted transition hover:bg-surface-2 hover:text-ink" aria-label={t('calendar.previous')}>
            <ChevronLeft />
          </button>
          <button onClick={() => calendar()?.next()} className="rounded-full p-1.5 text-muted transition hover:bg-surface-2 hover:text-ink" aria-label={t('calendar.next')}>
            <ChevronRight />
          </button>
        </div>
        <h1 className="min-w-0 flex-1 truncate text-base font-semibold tracking-tight first-letter:uppercase md:text-lg">{title}</h1>

        <Segmented label={t('calendar.viewLabel')} value={view} options={views} onChange={changeView} />
      </header>

      <div className="relative min-h-0 flex-1">
        {/* Thin loading bar: doesn't hide the calendar while fetching the next week. */}
        <div className={`absolute inset-x-0 top-0 z-10 h-0.5 overflow-hidden transition-opacity ${fetching ? 'opacity-100' : 'opacity-0'}`}>
          <div className="h-full w-1/3 animate-[loading_1.1s_ease-in-out_infinite] bg-accent" />
        </div>

        <FullCalendar
          ref={calendarRef}
          plugins={[timeGridPlugin, dayGridPlugin, listPlugin]}
          initialView={view}
          locale={language === 'pt-BR' ? ptBrLocale : 'en'}
          headerToolbar={false}
          height="100%"
          events={events}
          datesSet={handleDatesSet}
          nowIndicator
          scrollTime="06:00:00"
          allDayText={t('calendar.allDay')}
          slotLabelFormat={TIME_FORMATS[language].slot}
          eventTimeFormat={TIME_FORMATS[language].event}
          dayMaxEvents
          eventDisplay="block"
          dayHeaderContent={renderDayHeader}
          eventContent={renderEvent}
          listDayFormat={{ weekday: 'long', day: 'numeric', month: 'short' }}
          listDaySideFormat={false}
          noEventsText={fetching ? t('common.loading') : t('calendar.noEvents')}
        />
      </div>
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
