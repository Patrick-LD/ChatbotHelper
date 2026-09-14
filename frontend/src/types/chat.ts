import type { ApiError } from '@/api/errors'
import type { SourceDto } from './api'

export type MessageRole = 'user' | 'assistant' | 'error'

/** Én boble i samtalen. Frontend'en holder kun den aktuelle samtale — historikken ligger i API'ets database. */
export interface Message {
  id: string
  role: MessageRole
  text: string
  /** Kun assistent: kilder botten slog op til dette svar. */
  sources?: SourceDto[]
  /** Kun assistent: den handling, svaret efterlod ventende (snapshot). */
  pendingAction?: string | null
  /** Kun fejl: den normaliserede API-fejl, så UI kan tilbyde "prøv igen". */
  error?: ApiError
  createdAt: number
}
