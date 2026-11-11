import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AgendaScreen } from './components/AgendaScreen'
import { ConnectScreen, FullScreenMessage, LoginScreen } from './components/AuthScreens'
import { InviteScreen } from './components/InviteScreen'
import { api, inviteTokenFromPath, isUnauthorized, type Me } from './lib/api'

/**
 * /api/me decides which screen to show:
 * 401 → login | no calendar permission → connect | /invite/{token} → invite | ok → agenda.
 * No react-router for now: with one extra route, an "if" is enough.
 * The login returnUrl is the current URL, so someone who opens the invite signed out comes back to it after Google.
 */
export default function App() {
  const { t } = useTranslation()
  const me = useQuery({ queryKey: ['me'], queryFn: api.me })
  const [inviteToken, setInviteToken] = useState(() => inviteTokenFromPath())

  if (me.isPending) return <FullScreenMessage>{t('common.loading')}</FullScreenMessage>
  if (isUnauthorized(me.error)) return <LoginScreen invited={inviteToken !== null} />
  if (me.isError) return <ErrorScreen onRetry={() => me.refetch()} />
  if (me.data.google.status !== 'connected') return <ConnectScreen me={me.data} />

  if (inviteToken)
    return (
      <InviteScreen
        token={inviteToken}
        onDone={() => {
          // Switches the URL to "/" without reloading (the invite link shouldn't stay in history).
          window.history.replaceState(null, '', '/')
          setInviteToken(null)
        }}
      />
    )

  return <Home me={me.data} />
}

function Home({ me }: { me: Me }) {
  const { t } = useTranslation()
  // Refetches when the tab regains focus: if the other person accepted the invite, they show up without F5.
  const group = useQuery({ queryKey: ['group'], queryFn: api.currentGroup })

  if (group.isPending) return <FullScreenMessage>{t('common.loading')}</FullScreenMessage>
  if (group.isError) return <ErrorScreen onRetry={() => group.refetch()} />
  return <AgendaScreen me={me} group={group.data} />
}

function ErrorScreen({ onRetry }: { onRetry: () => void }) {
  const { t } = useTranslation()
  return (
    <FullScreenMessage>
      <div>
        <p>{t('common.serverError')}</p>
        <button onClick={onRetry} className="mt-3 font-medium text-accent hover:underline">
          {t('common.retry')}
        </button>
      </div>
    </FullScreenMessage>
  )
}
