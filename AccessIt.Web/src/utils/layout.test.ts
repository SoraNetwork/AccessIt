import { describe, expect, it } from 'vitest'
import { appShellStyle } from './layout'

describe('appShellStyle', () => {
  it('uses AuditIt-compatible neutral desktop surfaces', () => {
    expect(appShellStyle.pageBackground).toBe('#f0f2f5')
    expect(appShellStyle.contentBackground).toBe('#fff')
  })
})
