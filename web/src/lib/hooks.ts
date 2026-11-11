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
    return null // modo privado ou storage bloqueado: segue com o padrão
  }
}

/**
 * Quais fontes estão visíveis. No M1 é preferência local do navegador;
 * no M2 vira configuração de compartilhamento salva no backend (por grupo).
 */
export function useSelectedSources(sources: Source[] | undefined) {
  const [stored, setStored] = useState<string[] | null>(readStored)

  const selected = useMemo(() => {
    if (!sources) return []
    // Primeiro acesso: agenda principal + listas de tarefas.
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
        // ignora: a escolha vale só nesta sessão
      }
    },
    [selected],
  )

  // Ordenado para a queryKey do TanStack Query não mudar só porque a ordem dos cliques mudou.
  const sorted = useMemo(() => [...selected].sort(), [selected])
  return { selected: sorted, toggle }
}
