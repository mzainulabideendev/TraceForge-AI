import { HttpErrorResponse } from '@angular/common/http';

/** RFC 9457 problem details, plus the extensions the API adds. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errors?: string[];
  traceId?: string;
  application?: string;
}

/**
 * Turns any HTTP failure into text an operator can act on. Never surfaces raw stack traces, and
 * never assumes the failure body is JSON.
 */
export function describeProblem(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) {
      return 'The API is unreachable. Check that the backend is running and that this page is served from the same origin.';
    }

    const problem = readProblem(error.error);
    if (problem) {
      const parts = [problem.title, problem.detail].filter(
        (part): part is string => typeof part === 'string' && part.length > 0,
      );

      if (parts.length > 0) {
        return parts.join(' — ');
      }
    }

    if (typeof error.error === 'string' && error.error.trim().length > 0) {
      return error.error.trim();
    }

    return `Request failed with HTTP ${error.status}.`;
  }

  if (error instanceof Error && error.message) {
    return error.message;
  }

  return 'The request could not be completed.';
}

/** Validation messages the API returns in the `errors` problem extension. */
export function problemDetails(error: unknown): string[] {
  if (error instanceof HttpErrorResponse) {
    const problem = readProblem(error.error);
    if (problem?.errors?.length) {
      return problem.errors.filter(
        (entry): entry is string => typeof entry === 'string' && entry.length > 0,
      );
    }
  }

  return [];
}

export function statusOf(error: unknown): number {
  return error instanceof HttpErrorResponse ? error.status : 0;
}

function readProblem(body: unknown): ProblemDetails | null {
  if (typeof body !== 'object' || body === null) {
    return null;
  }

  const candidate = body as ProblemDetails;
  return typeof candidate === 'object' ? candidate : null;
}
