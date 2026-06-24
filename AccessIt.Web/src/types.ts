export type ApplicationRole = 'None' | 'SuperAdmin' | 'AccessAdmin' | 'Auditor'

export interface LoginUser { id: string; name: string; role: ApplicationRole; isActive: boolean }
export type AccessPersonKind = 'Employee' | 'Visitor'
export interface Person {
  id: string; name: string; mobile?: string; kind: AccessPersonKind; hikiotPersonNo?: string
  sources: string[]; cardNo?: string; faceAssetId?: string; enableBeginTimeUtc?: string; enableEndTimeUtc?: string
  qrShareToken?: string; lastIssueResultJson?: string
}
