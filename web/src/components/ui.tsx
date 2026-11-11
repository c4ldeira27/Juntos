import { useEffect, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { currentLanguage, LANGUAGES } from '../i18n'
import { CloseIcon } from './icons'

export const buttonPrimary =
  'inline-flex h-10 items-center justify-center gap-2 rounded-full bg-accent px-5 text-sm font-medium text-white transition hover:brightness-110 disabled:opacity-50 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent'

export const buttonSecondary =
  'inline-flex h-10 items-center justify-center gap-2 rounded-full border border-line px-5 text-sm font-medium transition hover:bg-surface-2 disabled:opacity-50'

export function Modal({
  title,
  description,
  onClose,
  children,
  footer,
}: {
  title: string
  description?: ReactNode
  onClose: () => void
  children?: ReactNode
  footer?: ReactNode
}) {
  const { t } = useTranslation()

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div className="fixed inset-0 z-50 grid place-items-center p-4">
      <div className="absolute inset-0 bg-black/40 backdrop-blur-[2px]" onClick={onClose} />
      <div
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className="relative flex max-h-[90vh] w-full max-w-md flex-col rounded-3xl border border-line bg-surface p-6 shadow-2xl"
      >
        <button onClick={onClose} className="absolute top-4 right-4 rounded-lg p-1.5 text-muted hover:bg-surface-2" aria-label={t('common.close')}>
          <CloseIcon width={16} height={16} />
        </button>
        <h2 className="pr-8 text-lg font-semibold tracking-tight">{title}</h2>
        {description && <div className="mt-1.5 text-sm text-pretty text-muted">{description}</div>}
        {children && <div className="mt-5 min-h-0 overflow-y-auto">{children}</div>}
        {footer && <div className="mt-6 flex flex-wrap justify-end gap-2">{footer}</div>}
      </div>
    </div>
  )
}

/** Segmented control (pill buttons). Used for views, sharing options and language. */
export function Segmented<T extends string | null>({
  label,
  value,
  options,
  onChange,
  size = 'md',
}: {
  label: string
  value: T
  options: readonly { value: T; label: string }[]
  onChange: (value: T) => void
  size?: 'sm' | 'md'
}) {
  return (
    <div role="radiogroup" aria-label={label} className="flex shrink-0 rounded-full bg-surface-2 p-0.5">
      {options.map((o) => {
        const active = o.value === value
        return (
          <button
            key={o.label}
            role="radio"
            aria-checked={active}
            onClick={() => onChange(o.value)}
            className={`${size === 'sm' ? 'h-6 px-2.5 text-[11px]' : 'h-7 px-3 text-xs'} rounded-full font-medium transition ${
              active ? 'bg-surface text-ink shadow-sm' : 'text-muted hover:text-ink'
            }`}
          >
            {o.label}
          </button>
        )
      })}
    </div>
  )
}

export function LanguageSwitch() {
  const { t, i18n } = useTranslation()
  return (
    <Segmented
      size="sm"
      label={t('common.language')}
      value={currentLanguage()}
      options={LANGUAGES.map((l) => ({ value: l.code, label: l.label }))}
      onChange={(code) => void i18n.changeLanguage(code)}
    />
  )
}

/** Checkbox where the color itself (of the calendar or the person) is the indicator. */
export function ColorCheck({ checked, color }: { checked: boolean; color: string }) {
  return (
    <span
      className="grid size-4 shrink-0 place-items-center rounded-[5px] border-2 transition peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-accent"
      style={{ borderColor: color, background: checked ? color : 'transparent' }}
    >
      {checked && (
        <svg viewBox="0 0 12 12" className="size-2.5 text-white" aria-hidden>
          <path d="m2.5 6 2.5 2.5 4.5-5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      )}
    </span>
  )
}

export function Avatar({ name, url, color, size = 36 }: { name: string; url: string | null; color: string; size?: number }) {
  const style = { width: size, height: size, boxShadow: `0 0 0 2px var(--surface), 0 0 0 4px ${color}` }
  return url ? (
    <img src={url} alt="" referrerPolicy="no-referrer" className="shrink-0 rounded-full" style={style} />
  ) : (
    <div
      className="grid shrink-0 place-items-center rounded-full text-sm font-semibold text-white"
      style={{ ...style, background: color }}
    >
      {name.charAt(0).toUpperCase()}
    </div>
  )
}
