import 'i18next'
import type { en } from './en'

// Typed keys: t('calendar.today') autocompletes, and a key that doesn't exist fails the build.
declare module 'i18next' {
  interface CustomTypeOptions {
    defaultNS: 'translation'
    resources: { translation: typeof en }
  }
}
