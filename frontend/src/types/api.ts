/** Kontrakten mod Chatbot.Api (camelCase). Spejler DTO'erne i src/Chatbot.Api/Endpoints. */

export interface ChatRequest {
  message: string
  conversationId?: string | null
}

export interface SourceDto {
  source: string
  heading: string
  score: number
}

export interface ChatResponse {
  reply: string
  conversationId: string
  sources: SourceDto[]
  /** Opsummering af en handling, der venter på "ja"/"nej" i næste besked. Null hvis ingen venter. */
  pendingAction: string | null
}

export interface MeResponse {
  userId: string
  name: string
  roles: string[]
}

/** Fejlform fra autentificeringen og fra tomme beskeder: { "error": "…" } */
export interface ErrorBody {
  error: string
}

/** ASP.NET ProblemDetails, som API'et bruger til 403 (ejerskab), 503 og 504. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
}
