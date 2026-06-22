import { describe, expect, it } from 'vitest'
import { getDingTalkBrowserRedirectUri } from './dingtalkAuth'

describe('getDingTalkBrowserRedirectUri', () => {
  it('uses the current site login route instead of an environment override', () => {
    expect(getDingTalkBrowserRedirectUri('https://door.example.com')).toBe('https://door.example.com/login')
  })

  it('does not duplicate a trailing slash', () => {
    expect(getDingTalkBrowserRedirectUri('https://door.example.com/')).toBe('https://door.example.com/login')
  })
})
