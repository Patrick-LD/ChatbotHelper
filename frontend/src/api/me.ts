import type { MeResponse } from '@/types/api'
import { request } from './client'

/** GET /me — hvem er nøglen? Bruges til at validere nøglen ved login og vise navn og roller. */
export function getMe(apiKey: string, signal?: AbortSignal): Promise<MeResponse> {
  return request<MeResponse>('/me', { apiKey, signal, timeoutMs: 15_000 })
}
