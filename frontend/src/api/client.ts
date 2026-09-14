import { ApiError, NETWORK_ERROR_TITLE, TIMEOUT_ERROR_TITLE, errorFromResponse } from './errors'

export const API_KEY_HEADER = 'X-Api-Key'

/**
 * Længste ventetid på ét kald. API'ets egen Ollama-timeout er 300 s (Chatbot:Ollama:TimeoutSeconds),
 * så vi venter lidt længere og lader API'ets 504 komme først, når det er modellen, der er langsom.
 */
export const REQUEST_TIMEOUT_MS = 320_000

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE'
  body?: unknown
  apiKey?: string | null
  signal?: AbortSignal
  timeoutMs?: number
}

/** Basis-URL: "/api" i udvikling (Vite-proxy), API'ets fulde adresse i produktion. Uden afsluttende skråstreg. */
export function apiBase(): string {
  const base = (import.meta.env.VITE_API_BASE as string | undefined) ?? '/api'
  return base.replace(/\/+$/, '')
}

/**
 * Den ene fetch-wrapper, alt går igennem: sætter nøglen, sender/læser JSON, holder en timeout og
 * oversætter alle fejl til ApiError. Komponenter og stores ser aldrig en rå Response.
 */
export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const controller = new AbortController()
  const timeout = setTimeout(() => controller.abort(), options.timeoutMs ?? REQUEST_TIMEOUT_MS)
  const onOuterAbort = () => controller.abort()
  options.signal?.addEventListener('abort', onOuterAbort)

  const headers: Record<string, string> = { Accept: 'application/json' }
  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json; charset=utf-8'
  }
  if (options.apiKey) {
    headers[API_KEY_HEADER] = options.apiKey
  }

  let response: Response
  try {
    response = await fetch(`${apiBase()}${path}`, {
      method: options.method ?? 'GET',
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: controller.signal,
    })
  } catch (error) {
    if (options.signal?.aborted) {
      throw error
    }
    const timedOut = controller.signal.aborted
    throw new ApiError(
      0,
      timedOut ? TIMEOUT_ERROR_TITLE : NETWORK_ERROR_TITLE,
      timedOut ? 'Prøv igen om lidt.' : 'Kører API’et? Tjek forbindelsen, og prøv igen.',
    )
  } finally {
    clearTimeout(timeout)
    options.signal?.removeEventListener('abort', onOuterAbort)
  }

  if (!response.ok) {
    throw await errorFromResponse(response)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}
