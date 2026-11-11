import { useCallback, useMemo, useState, useSyncExternalStore } from 'react'
import type { Source } from './api'

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
