import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { api, loginUrl, type GoogleStatus, type Me } from '../lib/api'
import { APP_NAME } from '../lib/config'
import { GoogleG, Logo } from './icons'
import { LanguageSwitch } from './ui'

export function Shell({ children }: { children: ReactNode }) {
  return (
    <main className="relative grid min-h-full place-items-center overflow-hidden px-4 py-10">
      {/* Color blobs in the background: the two logo colors, one per person. */}
      <div aria-hidden className="pointer-events-none absolute -top-40 -left-32 size-[28rem] rounded-full bg-[#7c6cff]/20 blur-3xl" />
      <div aria-hidden className="pointer-events-none absolute -right-32 -bottom-40 size-[28rem] rounded-full bg-[#ff7a6b]/20 blur-3xl" />
      <div className="relative w-full max-w-sm rounded-3xl border border-line bg-surface/80 p-8 text-center shadow-xl shadow-black/5 backdrop-blur">
        <Logo size={44} className="mx-auto" />
        {children}
      </div>
      <div className="absolute bottom-5">
        <LanguageSwitch />
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

export function LoginScreen({ invited = false }: { invited?: boolean }) {
  const { t } = useTranslation()
  return (
    <Shell>
      <h1 className="mt-5 text-2xl font-semibold tracking-tight">{invited ? t('login.invitedTitle') : APP_NAME}</h1>
      <p className="mt-2 text-sm text-pretty text-muted">
        {invited ? t('login.invitedBody', { app: APP_NAME }) : t('login.tagline')}
      </p>
      <GoogleButton href={loginUrl()}>{t('login.button')}</GoogleButton>
      <p className="mt-6 text-xs text-muted">{t('login.readOnly')}</p>
    </Shell>
  )
}

export function ConnectScreen({ me }: { me: Me }) {
  const { t } = useTranslation()
  const status = me.google.status as Exclude<GoogleStatus, 'connected'>

  async function logout() {
    await api.logout()
    window.location.href = '/'
  }

  return (
    <Shell>
      <h1 className="mt-5 text-xl font-semibold tracking-tight">{t(`connect.${status}.title`)}</h1>
      <p className="mt-2 text-sm text-pretty text-muted">{t(`connect.${status}.body`)}</p>
      <GoogleButton href={loginUrl(true)}>{t('connect.button')}</GoogleButton>
      <button onClick={logout} className="mt-5 text-xs text-muted underline-offset-4 hover:underline">
        {t('connect.switchAccount', { email: me.email })}
      </button>
    </Shell>
  )
}

export function FullScreenMessage({ children }: { children: ReactNode }) {
  return <div className="grid min-h-full place-items-center p-6 text-center text-sm text-muted">{children}</div>
}
