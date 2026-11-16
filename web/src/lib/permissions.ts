import type { GroupRole } from './api'

// Mirror of GroupRules on the back end, used only to decide which buttons to show.
// The API is the one that actually enforces these rules (a hidden button is not security).

/** Rename the group and create invites. */
export const canManage = (role: GroupRole) => role === 'owner' || role === 'admin'

export const canDelete = (role: GroupRole) => role === 'owner'

export const canChangeRoles = (role: GroupRole) => role === 'owner'

/** The owner can remove anyone else; an admin only regular members; members nobody. */
export function canRemove(actor: GroupRole, target: GroupRole) {
  if (actor === 'owner') return target !== 'owner'
  if (actor === 'admin') return target === 'member'
  return false
}
