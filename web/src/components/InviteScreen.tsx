import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, type InviteStatus } from '../lib/api'
import { APP_NAME } from '../lib/config'
import { Shell } from './AuthScreens'
import { buttonPrimary, buttonSecondary } from './ui'

const blockedCopy: Record<Exclude<InviteStatus, 'valid' | 'alreadyMember'>, { title: string; body: string }> = {
  expired: { title: 'Este convite expirou', body: 'Convites valem por 48 horas. Peça um link novo para quem te convidou.' },
  used: { title: 'Este convite já foi usado', body: 'Cada link funciona uma única vez. Peça um novo para quem te convidou.' },
  inAnotherGroup: {
    title: 'Você já está em outro grupo',
    body: 'Por enquanto cada pessoa participa de um grupo só. Saia do grupo atual para entrar neste.',
  },
  full: { title: 'Este grupo está cheio', body: 'O grupo já atingiu o limite de pessoas.' },
}

export function InviteScreen({ token, onDone }: { token: string; onDone: () => void }) {
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
      Ir para minha agenda
    </button>
  )

  if (preview.isPending)
    return (
      <Shell>
        <p className="mt-5 text-sm text-muted">Abrindo convite…</p>
      </Shell>
    )

  if (preview.isError) {
    const notFound = preview.error instanceof ApiError && preview.error.status === 404
    return (
      <Shell>
        <h1 className="mt-5 text-xl font-semibold tracking-tight">{notFound ? 'Convite não encontrado' : 'Algo deu errado'}</h1>
        <p className="mt-2 text-sm text-muted">
          {notFound ? 'Confira se o link foi copiado inteiro.' : 'Não foi possível abrir o convite. Tente de novo.'}
        </p>
        {goHome}
      </Shell>
    )
  }

  const { groupName, invitedBy, status } = preview.data

  if (status === 'alreadyMember')
    return (
      <Shell>
        <h1 className="mt-5 text-xl font-semibold tracking-tight">Você já está em “{groupName}”</h1>
        <button onClick={onDone} className={`${buttonPrimary} mt-8 w-full`}>
          Abrir agenda
        </button>
      </Shell>
    )

  if (status !== 'valid')
    return (
      <Shell>
        <h1 className="mt-5 text-xl font-semibold tracking-tight">{blockedCopy[status].title}</h1>
        <p className="mt-2 text-sm text-pretty text-muted">{blockedCopy[status].body}</p>
        {goHome}
      </Shell>
    )

  return (
    <Shell>
      <h1 className="mt-5 text-xl font-semibold tracking-tight text-balance">
        {invitedBy} convidou você para “{groupName}”
      </h1>
      <p className="mt-2 text-sm text-pretty text-muted">
        No {APP_NAME} vocês veem as agendas do Google um do outro na mesma tela. Depois de entrar, você escolhe quais
        agendas compartilhar e com quanto detalhe.
      </p>
      <button onClick={() => accept.mutate()} disabled={accept.isPending} className={`${buttonPrimary} mt-8 w-full`}>
        {accept.isPending ? 'Entrando…' : 'Entrar no grupo'}
      </button>
      {accept.isError && <p className="mt-3 text-sm text-red-500">Não foi possível entrar no grupo. Tente de novo.</p>}
      <button onClick={onDone} className="mt-4 text-xs text-muted underline-offset-4 hover:underline">
        Agora não
      </button>
    </Shell>
  )
}
