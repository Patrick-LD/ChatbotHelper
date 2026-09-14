import type { ChatRequest, ChatResponse } from '@/types/api'
import { request } from './client'

/** POST /chat — én tur i samtalen. Udelad conversationId for at starte en ny. */
export function postChat(body: ChatRequest, apiKey: string, signal?: AbortSignal): Promise<ChatResponse> {
  return request<ChatResponse>('/chat', { method: 'POST', body, apiKey, signal })
}
