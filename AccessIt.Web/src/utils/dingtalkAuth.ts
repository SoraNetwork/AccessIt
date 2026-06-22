export function getDingTalkBrowserRedirectUri(origin: string): string {
  return `${origin.replace(/\/$/, '')}/login`
}
