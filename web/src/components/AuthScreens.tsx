import type { ReactNode } from 'react'
import { api, loginUrl, type GoogleStatus, type Me } from '../lib/api'
import { APP_NAME } from '../lib/config'
import { GoogleG, Logo } from './icons'

function Shell({ children }: { children: ReactNode }) {
  return (
    <main className="relative grid min-h-full place-items-center overflow-hidden px-4 py-10">
      {/* Manchas de cor no fundo: as duas cores do logo, uma para cada pessoa. */}
      <div aria-hidden className="pointer-events-none absolute -top-40 -left-32 size-[28rem] rounded-full bg-[#7c6cff]/20 blur-3xl" />
      <div aria-hidden className="pointer-events-none absolute -right-32 -bottom-40 size-[28rem] rounded-full bg-[#ff7a6b]/20 blur-3xl" />
      <div className="relative w-full max-w-sm rounded-3xl border border-line bg-surface/80 p-8 text-center shadow-xl shadow-black/5 backdrop-blur">
        <Logo size={44} className="mx-auto" />
        {children}
      </div>
    </main>
  )
}

function GoogleButton({ href, children }: { href: string; children: ReactNode }) {
  return (
    <a
      href={href}
      className="mt-8 flex h-11 w-full items-center justify-center gap-3 rounded-full border border-line bg-white px-5 text-sm font-medium text-[#1f1f1f] shadow-sm transition hover:shadow-md focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
    >
      <GoogleG />
      {children}
    </a>
  )
}

export function LoginScreen() {
  return (
    <Shell>
      <h1 className="mt-5 text-2xl font-semibold tracking-tight">{APP_NAME}</h1>
      <p className="mt-2 text-sm text-pretty text-muted">
        Suas agendas do Google, lado a lado. Cada um no seu calendário, os dois na mesma tela.
      </p>
      <GoogleButton href={loginUrl()}>Entrar com Google</GoogleButton>
      <p className="mt-6 text-xs text-muted">Somente leitura. Nada é alterado na sua agenda.</p>
    </Shell>
  )
}

const connectCopy: Record<Exclude<GoogleStatus, 'connected'>, { title: string; body: string }> = {
  notConnected: {
    title: 'Falta conectar sua agenda',
    body: 'Para mostrar seus compromissos, precisamos de permissão de leitura do Google Agenda.',
  },
  needsReconnect: {
    title: 'Sua conexão com o Google expirou',
    body: 'O acesso foi revogado ou expirou. Autorize de novo para continuar vendo sua agenda.',
  },
  missingCalendarScope: {
    title: 'A permissão da agenda ficou desmarcada',
    body: 'Na tela do Google, deixe marcada a opção de ver seus calendários. Sem ela não há o que mostrar.',
  },
}

export function ConnectScreen({ me }: { me: Me }) {
  const copy = connectCopy[me.google.status as Exclude<GoogleStatus, 'connected'>]

  async function logout() {
    await api.logout()
    window.location.href = '/'
  }

  return (
    <Shell>
      <h1 className="mt-5 text-xl font-semibold tracking-tight">{copy.title}</h1>
      <p className="mt-2 text-sm text-pretty text-muted">{copy.body}</p>
      <GoogleButton href={loginUrl(true)}>Conectar Google Agenda</GoogleButton>
      <button onClick={logout} className="mt-5 text-xs text-muted underline-offset-4 hover:underline">
        Entrar com outra conta ({me.email})
      </button>
    </Shell>
  )
}

export function FullScreenMessage({ children }: { children: ReactNode }) {
  return <div className="grid min-h-full place-items-center p-6 text-center text-sm text-muted">{children}</div>
}
