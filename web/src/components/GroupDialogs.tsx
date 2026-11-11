import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { api, ApiError, type Group, type SharingItem, type Visibility } from '../lib/api'
import { buttonPrimary, buttonSecondary, Modal } from './ui'

// ===================== Criar grupo =====================

export function CreateGroupDialog({ onClose }: { onClose: () => void }) {
  const queryClient = useQueryClient()
  const [name, setName] = useState('')
  const create = useMutation({
    mutationFn: () => api.createGroup(name.trim()),
    // Coloca o grupo direto no cache: a tela troca para o modo grupo sem esperar outra requisição.
    onSuccess: (group) => queryClient.setQueryData(['group'], group),
  })

  function submit(e: FormEvent) {
    e.preventDefault()
    if (name.trim()) create.mutate()
  }

  return (
    <Modal title="Criar grupo" description="Dê um nome para o grupo. Depois você gera o link para convidar." onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <input
          autoFocus
          value={name}
          onChange={(e) => setName(e.target.value)}
          maxLength={60}
          placeholder="Ex.: Vini & Ana"
          className="h-11 w-full rounded-xl border border-line bg-bg px-4 text-sm outline-none focus:border-accent"
        />
        {create.isError && <p className="text-sm text-red-500">Não foi possível criar o grupo. Tente de novo.</p>}
        <div className="flex justify-end gap-2">
          <button type="button" onClick={onClose} className={buttonSecondary}>
            Cancelar
          </button>
          <button type="submit" disabled={!name.trim() || create.isPending} className={buttonPrimary}>
            {create.isPending ? 'Criando…' : 'Criar'}
          </button>
        </div>
      </form>
    </Modal>
  )
}

// ===================== Convite =====================

export function InviteDialog({ groupId, onClose }: { groupId: string; onClose: () => void }) {
  const [copied, setCopied] = useState(false)
  // Query (e não mutation) para o StrictMode do React não gerar dois convites ao montar duas vezes em dev.
  // gcTime 0: cada abertura do diálogo gera um link novo.
  const invite = useQuery({
    queryKey: ['invite', groupId],
    queryFn: () => api.createInvite(groupId),
    staleTime: Infinity,
    gcTime: 0,
    retry: false,
  })

  async function copy() {
    if (!invite.data) return
    await navigator.clipboard.writeText(invite.data.url)
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }

  async function share() {
    if (invite.data) await navigator.share({ title: 'Convite para o Juntos', url: invite.data.url }).catch(() => {})
  }

  const full = invite.error instanceof ApiError && invite.error.code === 'group_full'

  return (
    <Modal
      title="Convidar para o grupo"
      description="Mande este link para a pessoa. Ela entra com a conta Google dela e escolhe o que compartilhar."
      onClose={onClose}
      footer={
        <button onClick={onClose} className={buttonSecondary}>
          Fechar
        </button>
      }
    >
      {invite.isPending && <div className="h-11 animate-pulse rounded-xl bg-surface-2" />}
      {invite.isError && (
        <p className="text-sm text-red-500">{full ? 'O grupo já está cheio.' : 'Não foi possível gerar o convite.'}</p>
      )}
      {invite.data && (
        <div className="space-y-3">
          <div className="flex gap-2">
            <input
              readOnly
              value={invite.data.url}
              onFocus={(e) => e.target.select()}
              className="h-11 min-w-0 flex-1 rounded-xl border border-line bg-bg px-3 font-mono text-xs outline-none"
            />
            <button onClick={copy} className={`${buttonPrimary} h-11 w-28 shrink-0`}>
              {copied ? 'Copiado!' : 'Copiar'}
            </button>
          </div>
          {'share' in navigator && (
            <button onClick={share} className={`${buttonSecondary} w-full`}>
              Compartilhar…
            </button>
          )}
          <p className="text-xs text-muted">
            Vale uma única vez, até{' '}
            {new Date(invite.data.expiresAt).toLocaleString('pt-BR', {
              weekday: 'short',
              day: 'numeric',
              month: 'short',
              hour: '2-digit',
              minute: '2-digit',
            })}
            .
          </p>
        </div>
      )}
    </Modal>
  )
}

// ===================== O que eu compartilho =====================

type Choice = Visibility | null

const CALENDAR_OPTIONS: { value: Choice; label: string }[] = [
  { value: 'details', label: 'Detalhes' },
  { value: 'busyOnly', label: 'Só ocupado' },
  { value: null, label: 'Oculta' },
]

// "Só ocupado" não faz sentido para tarefas (não são blocos de tempo).
const TASK_OPTIONS: { value: Choice; label: string }[] = [
  { value: 'details', label: 'Mostrar' },
  { value: null, label: 'Oculta' },
]

export function SharingDialog({ group, onClose }: { group: Group; onClose: () => void }) {
  const queryClient = useQueryClient()
  const me = group.members.find((m) => m.isMe)
  const firstTime = !me?.sharingConfigured
  const sharing = useQuery({ queryKey: ['sharing', group.id], queryFn: () => api.sharing(group.id), staleTime: 0 })
  const [edited, setEdited] = useState<Record<string, Choice> | null>(null)

  // Primeira vez: sugere compartilhar a agenda principal com detalhes e o resto oculto.
  const initial = (items: SharingItem[]) =>
    Object.fromEntries(items.map((i) => [i.sourceId, firstTime ? (i.primary ? 'details' : null) : i.visibility]))
  const choices: Record<string, Choice> | null = edited ?? (sharing.data ? initial(sharing.data) : null)

  const save = useMutation({
    mutationFn: () =>
      api.updateSharing(
        group.id,
        (sharing.data ?? [])
          .filter((i) => choices?.[i.sourceId])
          .map((i) => ({ sourceId: i.sourceId, name: i.name, visibility: choices![i.sourceId]! })),
      ),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['group'] }),
        queryClient.invalidateQueries({ queryKey: ['group-agenda'] }),
      ])
      onClose()
    },
  })

  const set = (sourceId: string, value: Choice) => setEdited({ ...choices, [sourceId]: value })

  return (
    <Modal
      title={firstTime ? 'O que você quer compartilhar?' : 'O que eu compartilho'}
      description={
        <>
          Isso define o que aparece no grupo, para todos. Eventos marcados como <b>particulares</b> no Google sempre
          aparecem como “Ocupado” para os outros.
        </>
      }
      onClose={onClose}
      footer={
        <>
          <button onClick={onClose} className={buttonSecondary}>
            {firstTime ? 'Depois' : 'Cancelar'}
          </button>
          <button onClick={() => save.mutate()} disabled={!choices || save.isPending} className={buttonPrimary}>
            {save.isPending ? 'Salvando…' : 'Salvar'}
          </button>
        </>
      }
    >
      {sharing.isPending && (
        <div className="space-y-2">
          {[0, 1, 2].map((i) => (
            <div key={i} className="h-12 animate-pulse rounded-xl bg-surface-2" />
          ))}
        </div>
      )}
      {sharing.isError && <p className="text-sm text-red-500">Não foi possível carregar suas agendas.</p>}
      {sharing.data && choices && (
        <ul className="divide-y divide-line">
          {sharing.data.map((item) => {
            const options = item.kind === 'taskList' ? TASK_OPTIONS : CALENDAR_OPTIONS
            return (
              <li key={item.sourceId} className="flex flex-col gap-2 py-3 sm:flex-row sm:items-center sm:justify-between">
                <div className="min-w-0">
                  <div className="truncate text-sm font-medium">{item.name}</div>
                  <div className="text-xs text-muted">
                    {item.kind === 'taskList' ? 'Lista de tarefas' : item.primary ? 'Agenda principal' : 'Agenda'}
                  </div>
                </div>
                <div role="radiogroup" aria-label={`Compartilhamento de ${item.name}`} className="flex shrink-0 rounded-full bg-surface-2 p-0.5">
                  {options.map((o) => {
                    const active = choices[item.sourceId] === o.value
                    return (
                      <button
                        key={o.label}
                        role="radio"
                        aria-checked={active}
                        onClick={() => set(item.sourceId, o.value)}
                        className={`h-7 rounded-full px-3 text-xs font-medium transition ${
                          active ? 'bg-surface text-ink shadow-sm' : 'text-muted hover:text-ink'
                        }`}
                      >
                        {o.label}
                      </button>
                    )
                  })}
                </div>
              </li>
            )
          })}
        </ul>
      )}
      {save.isError && <p className="mt-3 text-sm text-red-500">Não foi possível salvar. Tente de novo.</p>}
    </Modal>
  )
}
