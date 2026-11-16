import { useCallback, useMemo, useState, useSyncExternalStore } from 'react'
import type { GroupSummary, Source } from './api'

/** Value of the switcher for "my own calendars, no group". */
export const PERSONAL = 'personal'
const GROUP_KEY = 'juntos.selectedGroup'

/** Lets other screens (e.g. accepting an invite) choose which view opens next. null = default. */
export function rememberSelectedGroup(id: string | null) {
  try {
    if (id === null) localStorage.removeItem(GROUP_KEY)
    else localStorage.setItem(GROUP_KEY, id)
  } catch {
    // private mode or blocked storage: the choice lasts only for this session
  }
}

/**
 * Which view is on screen: PERSONAL or a group id. Remembered per browser.
 * Default: the first group (the one joined first), or the personal view if there are none.
 */
export function useSelectedGroup(groups: GroupSummary[] | undefined) {
  const [stored, setStored] = useState<string | null>(() => {
    try {
      return localStorage.getItem(GROUP_KEY)
    } catch {
      return null
    }
  })

  const selected = useMemo(() => {
    if (!groups) return null
    if (stored === PERSONAL) return PERSONAL
    if (stored && groups.some((g) => g.id === stored)) return stored
    return groups[0]?.id ?? PERSONAL
  }, [groups, stored])

  const select = useCallback((id: string | null) => {
    setStored(id)
    rememberSelectedGroup(id)
  }, [])

  return { selected, select }
}

export function useMediaQuery(query: string) {
  return useSyncExternalStore(
    (onChange) => {
      const mql = window.matchMedia(query)
      mql.addEventListener('change', onChange)
      return () => mql.removeEventListener('change', onChange)
    },
    () => window.matchMedia(query).matches,
  )
}

const STORAGE_KEY = 'juntos.selectedSources'

function readStored(): string[] | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as string[]) : null
  } catch {
    return null // private mode or blocked storage: fall back to the default
  }
}

/** Which sources are visible in the personal agenda (a per-browser preference). */
export function useSelectedSources(sources: Source[] | undefined) {
  const [stored, setStored] = useState<string[] | null>(readStored)

  const selected = useMemo(() => {
    if (!sources) return []
    // First visit: primary calendar + task lists.
    if (stored === null) return sources.filter((s) => s.primary || s.kind === 'taskList').map((s) => s.id)
    const valid = new Set(sources.map((s) => s.id))
    return stored.filter((id) => valid.has(id))
  }, [sources, stored])

  const toggle = useCallback(
    (id: string) => {
      const next = selected.includes(id) ? selected.filter((x) => x !== id) : [...selected, id]
      setStored(next)
      try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(next))
      } catch {
        // ignore: the choice lasts only for this session
      }
    },
    [selected],
  )

  // Sorted so TanStack Query's queryKey doesn't change just because the click order changed.
  const sorted = useMemo(() => [...selected].sort(), [selected])
  return { selected: sorted, toggle }
}
