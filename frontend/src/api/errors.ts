import type { ErrorBody, ProblemDetails } from '@/types/api'

/**
 * Én fejltype til hele UI'et. API'et svarer i to former — `{ error }` (auth, tom besked) og
 * ProblemDetails `{ title, detail, status }` (ejerskab, Ollama/DB nede, timeout) — og netværksfejl
 * har slet ingen body. Her bliver de til status + overskrift + detalje, så komponenterne ikke
 * behøver at kende forskel.
 */
export class ApiError extends Error {
  readonly status: number
  readonly title: string
  readonly detail?: string

  constructor(status: number, title: string, detail?: string) {
    super(detail ? `${title} ${detail}` : title)
    this.name = 'ApiError'
    this.status = status
    this.title = title
    this.detail = detail
  }

  /** 401: nøglen mangler eller er ukendt — brugeren skal logge ind igen. */
  get isUnauthorized(): boolean {
    return this.status === 401
  }

  /** 403 fra /chat: samtalen tilhører en anden bruger. */
  get isForbidden(): boolean {
    return this.status === 403
  }

  /** Netværk (0), 503 og 504: forbigående — det giver mening at prøve igen. */
  get isRetryable(): boolean {
    return this.status === 0 || this.status === 503 || this.status === 504
  }
}

export const NETWORK_ERROR_TITLE = 'Kunne ikke kontakte serveren'
export const TIMEOUT_ERROR_TITLE = 'Serveren svarede ikke i tide'

/** Oversætter et fejlsvar fra API'et til en ApiError. Læser body'en som JSON, hvis den er det. */
export async function errorFromResponse(response: Response): Promise<ApiError> {
  const status = response.status
  let body: unknown = null
  try {
    const text = await response.text()
    body = text ? JSON.parse(text) : null
  } catch {
    body = null
  }

  return errorFromBody(status, body)
}

export function errorFromBody(status: number, body: unknown): ApiError {
  if (body && typeof body === 'object') {
    const asError = body as Partial<ErrorBody>
    if (typeof asError.error === 'string' && asError.error.trim()) {
      return new ApiError(status, asError.error)
    }

    const asProblem = body as ProblemDetails
    if (typeof asProblem.title === 'string' && asProblem.title.trim()) {
      return new ApiError(status, asProblem.title, asProblem.detail || undefined)
    }
  }

  return new ApiError(status, defaultTitle(status))
}

function defaultTitle(status: number): string {
  switch (status) {
    case 400:
      return 'Ugyldig forespørgsel'
    case 401:
      return 'Autentificering kræves'
    case 403:
      return 'Ingen adgang'
    case 404:
      return 'Ikke fundet'
    case 503:
      return 'Tjenesten er ikke tilgængelig lige nu'
    case 504:
      return TIMEOUT_ERROR_TITLE
    default:
      return `Uventet fejl (HTTP ${status})`
  }
}
