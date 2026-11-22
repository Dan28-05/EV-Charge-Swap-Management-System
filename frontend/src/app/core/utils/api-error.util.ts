import { HttpErrorResponse } from '@angular/common/http';

export function extractApiError(err: unknown, fallback: string): string {
  if (err instanceof HttpErrorResponse) {
    const body = err.error;
    if (typeof body === 'string') {
      const trimmed = body.trim();
      return trimmed.length > 0 ? trimmed : fallback;
    }
    if (body && typeof body === 'object') {
      const record = body as Record<string, unknown>;
      if (typeof record['detail'] === 'string' && record['detail'].trim()) {
        return record['detail'].trim();
      }
      if (record['errors'] && typeof record['errors'] === 'object') {
        const errors = Object.values(record['errors'] as Record<string, unknown>);
        for (const value of errors) {
          if (Array.isArray(value) && typeof value[0] === 'string' && value[0].trim()) {
            return value[0].trim();
          }
          if (typeof value === 'string' && value.trim()) {
            return value.trim();
          }
        }
      }
      if (typeof record['message'] === 'string' && record['message'].trim()) {
        return record['message'].trim();
      }
      if (typeof record['title'] === 'string' && record['title'].trim()) {
        return record['title'].trim();
      }
    }
  }
  return fallback;
}
