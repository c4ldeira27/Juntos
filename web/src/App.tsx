import { useQuery } from '@tanstack/react-query'
import { AgendaScreen } from './components/AgendaScreen'
import { ConnectScreen, FullScreenMessage, LoginScreen } from './components/AuthScreens'
import { api, isUnauthorized } from './lib/api'

/**
 * O /api/me decide qual tela mostrar:
 * 401 → login | conectado sem permissão da agenda → conectar | ok → agenda.
 * Sem react-router por enquanto: com 3 estados, um if resolve.
 */
export default function App() {
  const me = useQuery({ queryKey: ['me'], queryFn: api.me })

  if (me.isPending) return <FullScreenMessage>Carregando…</FullScreenMessage>
  if (isUnauthorized(me.error)) return <LoginScreen />
  if (me.isError)
    return (
      <FullScreenMessage>
        <div>
          <p>Não foi possível falar com o servidor.</p>
          <button onClick={() => me.refetch()} className="mt-3 font-medium text-accent hover:underline">
            Tentar de novo
          </button>
        </div>
      </FullScreenMessage>
    )
  if (me.data.google.status !== 'connected') return <ConnectScreen me={me.data} />
  return <AgendaScreen me={me.data} />
}
