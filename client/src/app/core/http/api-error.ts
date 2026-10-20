import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { ProblemDetails } from '../models';

/** Typed error carrying the RFC 9457 problem details returned by the API. */
export class ApiError extends Error {
  constructor(
    readonly problem: ProblemDetails,
    readonly status: number,
  ) {
    super(problem.detail ?? problem.title ?? 'The request failed.');
    this.name = 'ApiError';
  }
}

export function statusOf(error: unknown): number | undefined {
  if (error instanceof ApiError) {
    return error.status;
  }
  if (error instanceof HttpErrorResponse) {
    return error.status;
  }
  return undefined;
}

/** Turns any error into a safe, human-readable message (never a raw error). */
export function messageOf(error: unknown): string {
  const problem = readProblem(error);
  if (!problem) {
    return 'Something went wrong. Please try again.';
  }

  if (problem.errors) {
    const first = Object.values(problem.errors).flat()[0];
    if (first) {
      return first;
    }
  }

  return problem.detail ?? problem.title ?? 'The request failed.';
}

function readProblem(error: unknown): ProblemDetails | null {
  if (error instanceof ApiError) {
    return error.problem;
  }
  if (error instanceof HttpErrorResponse) {
    const body = error.error;
    if (body && typeof body === 'object') {
      return body as ProblemDetails;
    }
  }
  return null;
}

/**
 * Converts an HttpErrorResponse whose body is problem details into an
 * {@link ApiError}. Runs as the outermost interceptor so components receive a
 * single, predictable error type.
 */
export const problemDetailsInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse) {
        const body: unknown = error.error;

        // Requests made with responseType 'blob' (CSV export) deliver an
        // error body as a Blob, so the problem details have to be read
        // asynchronously before the component can show a useful message.
        if (body instanceof Blob) {
          return from(body.text()).pipe(
            switchMap((text) => {
              const problem = parseProblem(text);
              return throwError(() =>
                problem ? new ApiError(problem, error.status) : error,
              );
            }),
          );
        }

        if (isProblemDetails(body)) {
          return throwError(() => new ApiError(body, error.status));
        }
      }
      return throwError(() => error);
    }),
  );

function isProblemDetails(value: unknown): value is ProblemDetails {
  return (
    typeof value === 'object' &&
    value !== null &&
    ('title' in value || 'detail' in value)
  );
}

function parseProblem(text: string): ProblemDetails | null {
  if (!text) {
    return null;
  }
  try {
    const parsed: unknown = JSON.parse(text);
    return isProblemDetails(parsed) ? parsed : null;
  } catch {
    return null;
  }
}
