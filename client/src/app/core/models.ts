export type LeaveType = 'Annual' | 'Sick' | 'Unpaid';
export type LeaveStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';
export type LeaveScope = 'own' | 'team' | 'all';

/** Mirrors the server permission catalogue (`LeaveRequests.Api.Security.Permissions`). */
export const PERMISSIONS = {
  leaves: {
    viewOwn: 'leaves.view.own',
    create: 'leaves.create',
    cancel: 'leaves.cancel',
    viewTeam: 'leaves.view.team',
    approve: 'leaves.approve',
    approveLong: 'leaves.approve.long',
    viewAll: 'leaves.view.all',
    export: 'leaves.export',
  },
  users: {
    view: 'users.view',
    manage: 'users.manage',
  },
  roles: {
    view: 'roles.view',
    manage: 'roles.manage',
  },
  audit: {
    view: 'audit.view',
  },
} as const;

export interface UserProfile {
  id: string;
  email: string;
  name: string;
  tenantId: string;
  departmentId: string | null;
  roles: string[];
}

export interface Me extends UserProfile {
  permissions: string[];
}

export interface AuthResponse {
  accessToken: string;
  expiresAtUtc: string;
  user: UserProfile;
}

export interface LeaveRequest {
  id: string;
  tenantId: string;
  departmentId: string;
  departmentName: string;
  employeeId: string;
  employeeName: string;
  type: LeaveType;
  startDate: string;
  endDate: string;
  reason: string;
  status: LeaveStatus;
  reviewedById: string | null;
  reviewedAtUtc: string | null;
  createdAtUtc: string;
}

export interface CreateLeaveRequest {
  type: LeaveType;
  startDate: string;
  endDate: string;
  reason: string;
}

export interface Department {
  id: string;
  name: string;
}

export interface Role {
  id: string;
  name: string;
  description: string | null;
  isSystemRole: boolean;
  permissions: string[];
}

export interface PermissionInfo {
  code: string;
  group: string;
  description: string;
}

export interface User {
  id: string;
  email: string;
  name: string;
  tenantId: string;
  departmentId: string | null;
  isActive: boolean;
  roles: string[];
}

export interface CreateUser {
  email: string;
  fullName: string;
  departmentId?: string | null;
  password?: string | null;
  roles: string[];
}

export interface AuditLog {
  id: string;
  actorName: string;
  action: string;
  entityType: string;
  entityId: string | null;
  details: Record<string, unknown> | null;
  createdAtUtc: string;
}

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errors?: Record<string, string[]>;
  missingPermissions?: string[];
}
