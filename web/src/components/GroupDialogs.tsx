import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { currentLanguage } from '../i18n'
import { api, ApiError, type Group, type SharingItem, type Visibility } from '../lib/api'
import { APP_NAME } from '../lib/config'
import { buttonPrimary, buttonSecondary, Modal, Segmented } from './ui'

// ===================== Create group =====================

export function CreateGroupDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [name, setName] = useState('')
  const create = useMutation({
    mutationFn: () => api.createGroup(name.trim()),
    // Puts the group straight into the cache: the screen switches to group mode without another request.
    onSuccess: (group) => queryClient.setQueryData(['group'], group),
  })

  function submit(e: FormEvent) {
    e.preventDefault()
    if (name.trim()) create.mutate()
  }

  return (
    <Modal title={t('createGroup.title')} description={t('createGroup.description')} onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <input
          autoFocus
          value={name}
          onChange={(e) => setName(e.target.value)}
          maxLength={60}
          placeholder={t('createGroup.placeholder')}
          className="h-11 w-full rounded-xl border border-line bg-bg px-4 text-sm outline-none focus:border-accent"
        />
        {create.isError && <p className="text-sm text-red-500">{t('createGroup.error')}</p>}
        <div className="flex justify-end gap-2">
          <button type="button" onClick={onClose} className={buttonSecondary}>
            {t('common.cancel')}
          </button>
          <button type="submit" disabled={!name.trim() || create.isPending} className={buttonPrimary}>
            {create.isPending ? t('createGroup.creating') : t('createGroup.create')}
          </button>
        </div>
      </form>
    </Modal>
  )
}

// ===================== Invite =====================

export function InviteDialog({ groupId, onClose }: { groupId: string; onClose: () => void }) {
  const { t } = useTranslation()
  const [copied, setCopied] = useState(false)
  // A query (not a mutation) so React's StrictMode doesn't create two invites when it mounts twice in dev.
  // gcTime 0: every time the dialog opens, a new link is generated.
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
    if (invite.data)
      await navigator.share({ title: t('invite.shareTitle', { app: APP_NAME }), url: invite.data.url }).catch(() => {})
  }

  const full = invite.error instanceof ApiError && invite.error.code === 'group_full'

  return (
    <Modal
      title={t('invite.title')}
      description={t('invite.description')}
      onClose={onClose}
      footer={
        <button onClick={onClose} className={buttonSecondary}>
          {t('common.close')}
        </button>
      }
    >
      {invite.isPending && <div className="h-11 animate-pulse rounded-xl bg-surface-2" />}
      {invite.isError && <p className="text-sm text-red-500">{full ? t('invite.full') : t('invite.error')}</p>}
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
              {copied ? t('invite.copied') : t('invite.copy')}
            </button>
          </div>
          {'share' in navigator && (
            <button onClick={share} className={`${buttonSecondary} w-full`}>
              {t('invite.share')}
            </button>
          )}
          <p className="text-xs text-muted">
            {t('invite.validUntil', {
              date: new Date(invite.data.expiresAt).toLocaleString(currentLanguage(), {
                weekday: 'short',
                day: 'numeric',
                month: 'short',
                hour: 'numeric',
                minute: '2-digit',
              }),
            })}
          </p>
        </div>
      )}
    </Modal>
  )
}

// ===================== What I share =====================

type Choice = Visibility | null

export function SharingDialog({ group, onClose }: { group: Group; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const me = group.members.find((m) => m.isMe)
  const firstTime = !me?.sharingConfigured
  const sharing = useQuery({ queryKey: ['sharing', group.id], queryFn: () => api.sharing(group.id), staleTime: 0 })
  const [edited, setEdited] = useState<Record<string, Choice> | null>(null)

  const calendarOptions = [
    { value: 'details', label: t('sharing.options.details') },
    { value: 'busyOnly', label: t('sharing.options.busyOnly') },
    { value: null, label: t('sharing.options.hidden') },
  ] as const
  // "Busy only" makes no sense for tasks (they aren't time blocks).
  const taskOptions = [
    { value: 'details', label: t('sharing.options.show') },
    { value: null, label: t('sharing.options.hidden') },
  ] as const

  // First time: suggests sharing the primary calendar with details and hiding the rest.
  const initial = (items: SharingItem[]) =>
    Object.fromEntries(items.map((i) => [i.sourceId, firstTime ? (i.primary ? 'details' : null) : i.visibility]))
  const choices: Record<string, Choice> | null = edited ?? (sharing.data ? initial(sharing.data) : null)
  const nameOf = (item: SharingItem) => item.name ?? t('personal.untitledList')

  const save = useMutation({
    mutationFn: () =>
      api.updateSharing(
        group.id,
        (sharing.data ?? [])
          .filter((i) => choices?.[i.sourceId])
          .map((i) => ({ sourceId: i.sourceId, name: nameOf(i), visibility: choices![i.sourceId]! })),
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
      title={firstTime ? t('sharing.titleFirst') : t('sharing.title')}
      description={<Trans i18nKey="sharing.description" components={{ b: <b /> }} />}
      onClose={onClose}
      footer={
        <>
          <button onClick={onClose} className={buttonSecondary}>
            {firstTime ? t('sharing.later') : t('common.cancel')}
          </button>
          <button onClick={() => save.mutate()} disabled={!choices || save.isPending} className={buttonPrimary}>
            {save.isPending ? t('sharing.saving') : t('sharing.save')}
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
      {sharing.isError && <p className="text-sm text-red-500">{t('sharing.loadError')}</p>}
      {sharing.data && choices && (
        <ul className="divide-y divide-line">
          {sharing.data.map((item) => (
            <li key={item.sourceId} className="flex flex-col gap-2 py-3 sm:flex-row sm:items-center sm:justify-between">
              <div className="min-w-0">
                <div className="truncate text-sm font-medium">{nameOf(item)}</div>
                <div className="text-xs text-muted">
                  {item.kind === 'taskList'
                    ? t('sharing.kind.taskList')
                    : item.primary
                      ? t('sharing.kind.primary')
                      : t('sharing.kind.calendar')}
                </div>
              </div>
              <Segmented<Choice>
                label={t('sharing.ariaLabel', { name: nameOf(item) })}
                value={choices[item.sourceId] ?? null}
                options={item.kind === 'taskList' ? taskOptions : calendarOptions}
                onChange={(value) => set(item.sourceId, value)}
              />
            </li>
          ))}
        </ul>
      )}
      {save.isError && <p className="mt-3 text-sm text-red-500">{t('sharing.saveError')}</p>}
    </Modal>
  )
}
