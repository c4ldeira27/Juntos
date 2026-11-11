import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { api, ApiError } from '../lib/api'
import { APP_NAME } from '../lib/config'
import { Shell } from './AuthScreens'
import { buttonPrimary, buttonSecondary } from './ui'

export function InviteScreen({ token, onDone }: { token: string; onDone: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const preview = useQuery({ queryKey: ['invite-preview', token], queryFn: () => api.invitePreview(token) })
  const accept = useMutation({
    mutationFn: () => api.acceptInvite(token),
    onSuccess: (group) => {
      queryClient.setQueryData(['group'], group)
      onDone()
    },
  })

  const goHome = (
    <button onClick={onDone} className={`${buttonSecondary} mt-8 w-full`}>
      {t('inviteScreen.goHome')}
    </button>
  )

  if (preview.isPending)
    return (
      <Shell>
        <p className="mt-5 text-sm text-muted">{t('inviteScreen.opening')}</p>
      </Shell>
    )

  if (preview.isError) {
    const notFound = preview.error instanceof ApiError && preview.error.status === 404
    return (
      <Shell>
        <h1 className="mt-5 text-xl font-semibold tracking-tight">
          {notFound ? t('inviteScreen.notFoundTitle') : t('inviteScreen.errorTitle')}
        </h1>
        <p className="mt-2 text-sm text-muted">{notFound ? t('inviteScreen.notFoundBody') : t('inviteScreen.errorBody')}</p>
        {goHome}
      </Shell>
    )
  }

  const { groupName, invitedBy, status } = preview.data

  if (status === 'alreadyMember')
    return (
      <Shell>
        <h1 className="mt-5 text-xl font-semibold tracking-tight">{t('inviteScreen.alreadyMember', { group: groupName })}</h1>
        <button onClick={onDone} className={`${buttonPrimary} mt-8 w-full`}>
          {t('inviteScreen.openCalendar')}
        </button>
      </Shell>
    )

  if (status !== 'valid')
    return (
      <Shell>
        <h1 className="mt-5 text-xl font-semibold tracking-tight">{t(`inviteScreen.${status}.title`)}</h1>
        <p className="mt-2 text-sm text-pretty text-muted">{t(`inviteScreen.${status}.body`)}</p>
        {goHome}
      </Shell>
    )

  return (
    <Shell>
      <h1 className="mt-5 text-xl font-semibold tracking-tight text-balance">
        {t('inviteScreen.validTitle', { name: invitedBy ?? t('inviteScreen.someone'), group: groupName })}
      </h1>
      <p className="mt-2 text-sm text-pretty text-muted">{t('inviteScreen.validBody', { app: APP_NAME })}</p>
      <button onClick={() => accept.mutate()} disabled={accept.isPending} className={`${buttonPrimary} mt-8 w-full`}>
        {accept.isPending ? t('inviteScreen.joining') : t('inviteScreen.join')}
      </button>
      {accept.isError && <p className="mt-3 text-sm text-red-500">{t('inviteScreen.joinError')}</p>}
      <button onClick={onDone} className="mt-4 text-xs text-muted underline-offset-4 hover:underline">
        {t('inviteScreen.notNow')}
      </button>
    </Shell>
  )
}
