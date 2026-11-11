import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { AgendaScreen } from './components/AgendaScreen'
import { ConnectScreen, FullScreenMessage, LoginScreen } from './components/AuthScreens'
import { InviteScreen } from './components/InviteScreen'
import { api, inviteTokenFromPath, isUnauthorized, type Me } from './lib/api'

/**
 * O /api/me decide qual tela mostrar:
 * 401 → login | sem permissão da agenda → conectar | /convite/{token} → convite | ok → agenda.
 * Sem react-router por enquanto: com uma rota extra, um if resolve.
 * O returnUrl do login é a URL atual, então quem abre o convite deslogado volta para ele depois do Google.
 */
export default function App() {
  const me = useQuery({ queryKey: ['me'], queryFn: api.me })
  const [inviteToken, setInviteToken] = useState(() => inviteTokenFromPath())

  if (me.isPending) return <FullScreenMessage>Carregando…</FullScreenMessage>
  if (isUnauthorized(me.error)) return <LoginScreen invited={inviteToken !== null} />
  if (me.isError) return <ErrorScreen onRetry={() => me.refetch()} />
  if (me.data.google.status !== 'connected') return <ConnectScreen me={me.data} />

  if (inviteToken)
    return (
      <InviteScreen
        token={inviteToken}
        onDone={() => {
          // Troca a URL para "/" sem recarregar a página (o link do convite não deve ficar no histórico).
          window.history.replaceState(null, '', '/')
          setInviteToken(null)
        }}
      />
    )

  return <Home me={me.data} />
}

function Home({ me }: { me: Me }) {
  // Ao voltar para a aba, recarrega: se a outra pessoa aceitou o convite, ela aparece sem F5.
  const group = useQuery({ queryKey: ['group'], queryFn: api.currentGroup })

  if (group.isPending) return <FullScreenMessage>Carregando…</FullScreenMessage>
  if (group.isError) return <ErrorScreen onRetry={() => group.refetch()} />
  return <AgendaScreen me={me} group={group.data} />
}

function ErrorScreen({ onRetry }: { onRetry: () => void }) {
  return (
    <FullScreenMessage>
      <div>
        <p>Não foi possível falar com o servidor.</p>
        <button onClick={onRetry} className="mt-3 font-medium text-accent hover:underline">
          Tentar de novo
        </button>
      </div>
    </FullScreenMessage>
  )
}
