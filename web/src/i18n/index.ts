import i18n from 'i18next'
import LanguageDetector from 'i18next-browser-languagedetector'
import { initReactI18next } from 'react-i18next'
import { en } from './en'
import { ptBR } from './pt-BR'

export const LANGUAGES = [
  { code: 'en', label: 'EN' },
  { code: 'pt-BR', label: 'PT' },
] as const

export type Language = (typeof LANGUAGES)[number]['code']

void i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources: { en: { translation: en }, 'pt-BR': { translation: ptBR } },
    supportedLngs: LANGUAGES.map((l) => l.code),
    fallbackLng: 'en',
    detection: {
      // A manual choice (saved in localStorage) wins; otherwise follow the browser language.
      order: ['localStorage', 'navigator'],
      lookupLocalStorage: 'juntos.language',
      caches: ['localStorage'],
      // Any Portuguese variant (pt, pt-PT...) uses pt-BR; anything else falls back to English.
      convertDetectedLanguage: (lng) => (lng.toLowerCase().startsWith('pt') ? 'pt-BR' : 'en'),
    },
    // React already escapes values, so i18next doesn't need to (and double escaping breaks "&" in names).
    interpolation: { escapeValue: false },
  })

// Keeps <html lang> right for screen readers, spell checkers and browser translation.
const syncHtmlLang = (lng: string) => document.documentElement.setAttribute('lang', lng)
syncHtmlLang(i18n.language)
i18n.on('languageChanged', syncHtmlLang)

export const currentLanguage = (): Language => (i18n.language === 'pt-BR' ? 'pt-BR' : 'en')

export default i18n
