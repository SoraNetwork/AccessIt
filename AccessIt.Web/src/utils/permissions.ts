import type { ApplicationRole } from '../types'

export function canManageAccess(role: ApplicationRole | undefined): boolean {
  return role === 'SuperAdmin' || role === 'AccessAdmin'
}

export function canOpenDoor(role: ApplicationRole | undefined): boolean {
  return canManageAccess(role)
}

export function canManageSystem(role: ApplicationRole | undefined): boolean {
  return role === 'SuperAdmin'
}
