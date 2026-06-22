export type ApplicationRole = 'None' | 'SuperAdmin' | 'AccessAdmin' | 'Auditor'
export type PersonKind = 'Employee' | 'Visitor'

export interface LoginUser { id: string; name: string; role: ApplicationRole; isActive: boolean }
export interface Device { id: string; deviceSerial: string; groupNo?: string; groupName?: string; isManaged: boolean; supportsUserInfo: boolean; supportsCardInfo: boolean; supportsFace: boolean; supportsPassword: boolean; supportsPurePassword: boolean; supportsRemoteOpen: boolean; supportsUserRightPlanTemplate: boolean; lastSyncedAtUtc?: string }
export interface Card { id: string; cardNo: string; isVirtual: boolean }
export interface FaceAsset { id: string; publicToken: string; byteLength: number; width: number; height: number }
export interface Person { id: string; employeeNo: string; name: string; kind: PersonKind; status: string; dingTalkUserId?: string; mobile?: string; permanentValid: boolean; enableBeginTime: string; enableEndTime: string; maxOpenDoorTime: number; deviceGrants: Array<{ accessDeviceId: string; isActive: boolean; accessDevice?: Device }>; cards: Card[]; faceAssets: FaceAsset[] }
export interface Job { id: string; type: string; status: string; attemptCount: number; nextAttemptAtUtc: string; traceId?: string; failureCode?: string; failureMessage?: string; createdAtUtc: string }
export interface SyncConflict { id: string; employeeNo: string; fieldName: string; localValue?: string; remoteValue?: string; resolution: string }
export interface AuditEvent { id: string; action: string; entityType: string; entityId: string; actorUserId?: string; occurredAtUtc: string; detailsJson?: string }
