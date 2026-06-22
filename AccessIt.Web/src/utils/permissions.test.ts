import { describe, expect, it } from 'vitest'
import { canManageAccess, canOpenDoor } from './permissions'

describe('access-control permissions', () => {
  it('only super and access administrators can manage people', () => {
    expect(canManageAccess('SuperAdmin')).toBe(true)
    expect(canManageAccess('AccessAdmin')).toBe(true)
    expect(canManageAccess('Auditor')).toBe(false)
  })

  it('does not give unassigned users remote-door access', () => {
    expect(canOpenDoor('None')).toBe(false)
  })
})
