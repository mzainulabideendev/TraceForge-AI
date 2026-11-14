import { describe, expect, it } from 'vitest';
import {
  auditOutcomeLabel,
  formatTimestamp,
  isPast,
  roleLabel,
  scopeTypeLabel,
  statusClass,
  toUtcIso,
} from './format';

/**
 * These helpers control how wire values are rendered. They must never invent a role, a scope type or
 * a status: an unknown value is shown as unknown, never as something more permissive.
 */
describe('roleLabel', () => {
  it('maps the numeric workspace roles the API returns', () => {
    expect(roleLabel(1)).toBe('Owner');
    expect(roleLabel(2)).toBe('Administrator');
    expect(roleLabel(3)).toBe('Analyst');
  });

  it('never guesses for unknown or missing roles', () => {
    expect(roleLabel(null)).toBe('Unknown');
    expect(roleLabel(undefined)).toBe('Unknown');
    expect(roleLabel(9)).toBe('Role 9');
  });
});

describe('scopeTypeLabel', () => {
  it('maps the numeric scope types the API returns', () => {
    expect(scopeTypeLabel(1)).toBe('IPv4');
    expect(scopeTypeLabel(2)).toBe('IPv6');
    expect(scopeTypeLabel(3)).toBe('CIDR');
    expect(scopeTypeLabel(4)).toBe('Hostname');
    expect(scopeTypeLabel(5)).toBe('Origin');
  });

  it('falls back for unknown values', () => {
    expect(scopeTypeLabel(7)).toBe('Type 7');
  });
});

describe('statusClass', () => {
  it('distinguishes usable and unusable states', () => {
    expect(statusClass('Approved')).toBe('badge-active');
    expect(statusClass('Pending')).toBe('badge-pending');
    expect(statusClass('Revoked')).toBe('badge-revoked');
    expect(statusClass('Expired')).toBe('badge-expired');
    expect(statusClass('something-else')).toBe('');
  });
});

describe('toUtcIso', () => {
  it('converts a local datetime value to a UTC instant', () => {
    expect(toUtcIso('2026-11-14T12:30')).toBe(new Date('2026-11-14T12:30').toISOString());
  });

  it('returns null for empty or unparseable input', () => {
    expect(toUtcIso('')).toBeNull();
    expect(toUtcIso('not-a-date')).toBeNull();
  });
});

describe('formatTimestamp', () => {
  it('renders an em dash instead of inventing a time', () => {
    expect(formatTimestamp(null)).toBe('—');
    expect(formatTimestamp(undefined)).toBe('—');
  });

  it('returns the raw value when it is not a date', () => {
    expect(formatTimestamp('nonsense')).toBe('nonsense');
  });
});

describe('isPast and auditOutcomeLabel', () => {
  it('treats a past expiration as past and a future one as not past', () => {
    expect(isPast('2000-01-01T00:00:00Z')).toBe(true);
    expect(isPast('2999-01-01T00:00:00Z')).toBe(false);
    expect(isPast(null)).toBe(false);
  });

  it('maps audit outcomes without overstating them', () => {
    expect(auditOutcomeLabel(1)).toBe('Success');
    expect(auditOutcomeLabel(3)).toBe('Denied');
    expect(auditOutcomeLabel(99)).toBe('Outcome 99');
  });
});
