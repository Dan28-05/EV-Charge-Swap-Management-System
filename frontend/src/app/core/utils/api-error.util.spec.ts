import { HttpErrorResponse } from '@angular/common/http';
import { extractApiError } from './api-error.util';

describe('extractApiError', () => {
  it('extracts plain string body from 409 Conflict responses', () => {
    const err = new HttpErrorResponse({ status: 409, error: 'Ending kWh must be >= starting kWh.' });
    expect(extractApiError(err, 'Fallback')).toBe('Ending kWh must be >= starting kWh.');
  });

  it('extracts ProblemDetails.detail when present', () => {
    const err = new HttpErrorResponse({
      status: 409,
      error: { title: 'Conflict', detail: 'Invoice is already settled.' },
    });
    expect(extractApiError(err, 'Fallback')).toBe('Invoice is already settled.');
  });

  it('extracts first field validation error from ValidationProblemDetails without showing [object Object]', () => {
    const err = new HttpErrorResponse({
      status: 400,
      error: { title: 'One or more validation errors occurred.', errors: { MeterKwh: ['Meter reading must be >= 0.'] } },
    });
    expect(extractApiError(err, 'Fallback')).toBe('Meter reading must be >= 0.');
  });

  it('returns fallback for unknown non-HTTP errors', () => {
    expect(extractApiError(new Error('boom'), 'Fallback error')).toBe('Fallback error');
  });
});
