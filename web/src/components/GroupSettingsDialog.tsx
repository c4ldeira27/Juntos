import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { api, type Group, type Member } from '../lib/api'
import { canChangeRoles, canDelete, canManage, canRemove } from '../lib/permissions'
import { RoleBadge } from './Panels'
import { Avatar, buttonPrimary, buttonSecondary, Modal } from './ui'

interface GroupSettingsDialogProps {
  group: Group
  onClose: () => void
  /** Called after the group is deleted (the screen goes back to another view). */
  onDeleted: () => void
}

/**
 * Name, members and roles. Every action shows only to whoever may do it (lib/permissions mirrors GroupRules);
 * the API enforces the same rules regardless of what the screen shows.
 */
export function GroupSettingsDialog({ group, onClose, onDeleted }: GroupSettingsDialogProps) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [name, setName] = useState(group.name)
  const editable = canManage(group.myRole)

  const refresh = async (updated?: Group) => {
    if (updated) queryClient.setQueryData(['group', group.id], updated)
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['group', group.id] }),
      queryClient.invalidateQueries({ queryKey: ['group-agenda', group.id] }),
      queryClient.invalidateQueries({ queryKey: ['groups'] }),
    ])
  }

  const rename = useMutation({ mutationFn: () => api.renameGroup(group.id, name.trim()), onSuccess: refresh })
  const setRole = useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: 'admin' | 'member' }) => api.setMemberRole(group.id, userId, role),
    onSuccess: refresh,
  })
  const remove = useMutation({
    mutationFn: (userId: string) => api.removeMember(group.id, userId),
    onSuccess: () => refresh(),
  })
  const destroy = useMutation({
    mutationFn: () => api.deleteGroup(group.id),
    onSuccess: async () => {
      queryClient.removeQueries({ queryKey: ['group', group.id] })
      queryClient.removeQueries({ queryKey: ['group-agenda', group.id] })
      await queryClient.invalidateQueries({ queryKey: ['groups'] })
      onDeleted()
    },
  })

  const failed = rename.isError || setRole.isError || remove.isError || destroy.isError
  const busy = rename.isPending || setRole.isPending || remove.isPending || destroy.isPending

  function submitName(e: FormEvent) {
    e.preventDefault()
    if (name.trim() && name.trim() !== group.name) rename.mutate()
  }

  function confirmRemove(member: Member) {
    if (window.confirm(t('groupSettings.confirmRemove', { name: member.name }))) remove.mutate(member.userId)
  }

  function confirmDelete() {
    if (window.confirm(t('groupSettings.confirmDelete', { group: group.name }))) destroy.mutate()
  }

  return (
    <Modal
      title={t('groupSettings.title')}
      onClose={onClose}
      footer={
        <>
          {canDelete(group.myRole) && (
            <button onClick={confirmDelete} disabled={busy} className={`${buttonSecondary} mr-auto border-red-500/40 text-red-500 hover:bg-red-500/10`}>
              {t('groupSettings.delete')}
            </button>
          )}
          <button onClick={onClose} className={buttonSecondary}>
            {t('common.close')}
          </button>
        </>
      }
    >
      <form onSubmit={submitName} className="space-y-1.5">
        <label htmlFor="group-name" className="text-xs font-semibold tracking-wider text-muted uppercase">
          {t('groupSettings.name')}
        </label>
        <div className="flex gap-2">
          <input
            id="group-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            disabled={!editable}
            maxLength={60}
            className="h-10 min-w-0 flex-1 rounded-xl border border-line bg-bg px-3 text-sm outline-none focus:border-accent disabled:opacity-70"
          />
          {editable && (
            <button type="submit" disabled={!name.trim() || name.trim() === group.name || busy} className={`${buttonPrimary} h-10`}>
              {rename.isPending ? t('groupSettings.saving') : t('groupSettings.save')}
            </button>
          )}
        </div>
        {!editable && <p className="text-xs text-muted">{t('groupSettings.onlyAdmins')}</p>}
      </form>

      <div className="mt-6">
        <h3 className="mb-1 text-xs font-semibold tracking-wider text-muted uppercase">
          {t('group.members')} · {group.members.length}/{group.maxMembers}
        </h3>
        <ul className="divide-y divide-line">
          {group.members.map((m) => (
            <li key={m.userId} className="flex flex-wrap items-center gap-3 py-2.5">
              <Avatar name={m.name} url={m.avatarUrl} color={m.color} size={28} />
              <div className="flex min-w-0 flex-1 items-center gap-1.5">
                <span className="truncate text-sm font-medium">
                  {m.name}
                  {m.isMe && <span className="font-normal text-muted"> {t('group.you')}</span>}
                </span>
                <RoleBadge role={m.role} />
              </div>
              {!m.isMe && (
                <div className="flex gap-1">
                  {canChangeRoles(group.myRole) && m.role !== 'owner' && (
                    <button
                      onClick={() => setRole.mutate({ userId: m.userId, role: m.role === 'admin' ? 'member' : 'admin' })}
                      disabled={busy}
                      className="rounded-lg px-2 py-1 text-xs font-medium text-accent transition hover:bg-surface-2 disabled:opacity-50"
                    >
                      {m.role === 'admin' ? t('groupSettings.removeAdmin') : t('groupSettings.makeAdmin')}
                    </button>
                  )}
                  {canRemove(group.myRole, m.role) && (
                    <button
                      onClick={() => confirmRemove(m)}
                      disabled={busy}
                      className="rounded-lg px-2 py-1 text-xs font-medium text-red-500 transition hover:bg-red-500/10 disabled:opacity-50"
                    >
                      {t('groupSettings.remove')}
                    </button>
                  )}
                </div>
              )}
            </li>
          ))}
        </ul>
      </div>

      {failed && <p className="mt-3 text-sm text-red-500">{t('groupSettings.error')}</p>}
    </Modal>
  )
}
