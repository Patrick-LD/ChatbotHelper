import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { postChat } from '@/api/chat'
import { ApiError, NETWORK_ERROR_TITLE } from '@/api/errors'
import type { Message } from '@/types/chat'
import { useAuthStore } from './auth'

/** Præcis de ord, API'ets ConfirmationParser genkender som entydigt ja/nej. Knapteksterne er noget andet. */
export const CONFIRM_WORD = 'ja'
export const REJECT_WORD = 'nej'

function newId(): string {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `${Date.now()}-${Math.random().toString(16).slice(2)}`
}

/**
 * Den aktuelle samtale. Frontend'en holder boblerne på skærmen; API'et holder historikken pr.
 * conversationId og afgør selv, om en besked er et "ja" til en ventende handling. Store'n gør
 * derfor kun én ting: sender tekst og viser, hvad der kom tilbage.
 */
export const useChatStore = defineStore('chat', () => {
  const auth = useAuthStore()

  const messages = ref<Message[]>([])
  const conversationId = ref<string | null>(null)
  const isSending = ref(false)
  /** Handlingen fra det seneste svar, der venter på ja/nej. Null når intet venter. */
  const pendingAction = ref<string | null>(null)
  const lastError = ref<ApiError | null>(null)
  const lastUserText = ref<string | null>(null)

  const hasMessages = computed(() => messages.value.length > 0)
  const canRetry = computed(() => !!lastError.value?.isRetryable && !!lastUserText.value && !isSending.value)

  async function send(text: string): Promise<void> {
    const trimmed = text.trim()
    if (!trimmed || isSending.value) return

    const apiKey = auth.apiKey
    if (!apiKey) {
      lastError.value = new ApiError(401, 'Autentificering kræves', 'Log ind med din API-nøgle.')
      return
    }

    lastError.value = null
    lastUserText.value = trimmed
    messages.value.push({ id: newId(), role: 'user', text: trimmed, createdAt: Date.now() })
    isSending.value = true

    try {
      const response = await postChat({ message: trimmed, conversationId: conversationId.value }, apiKey)
      conversationId.value = response.conversationId
      pendingAction.value = response.pendingAction
      messages.value.push({
        id: newId(),
        role: 'assistant',
        text: response.reply,
        sources: response.sources,
        pendingAction: response.pendingAction,
        createdAt: Date.now(),
      })
    } catch (error) {
      const apiError =
        error instanceof ApiError ? error : new ApiError(0, NETWORK_ERROR_TITLE, 'Prøv igen om lidt.')
      lastError.value = apiError

      if (apiError.isUnauthorized) {
        // Nøglen er ugyldig eller fjernet — App.vue sender brugeren til login, når nøglen forsvinder.
        auth.logout()
        return
      }

      if (apiError.isForbidden) {
        // Samtalen tilhører en anden bruger: næste besked starter en ny samtale.
        conversationId.value = null
        pendingAction.value = null
      }

      messages.value.push({ id: newId(), role: 'error', text: apiError.message, error: apiError, createdAt: Date.now() })
    } finally {
      isSending.value = false
    }
  }

  /** "Ja, udfør": sendes som almindelig besked — API'et udfører uden modelkald. */
  function confirm(): Promise<void> {
    return send(CONFIRM_WORD)
  }

  /** "Nej, annullér". */
  function reject(): Promise<void> {
    return send(REJECT_WORD)
  }

  /** Fjerner fejlboblen og den besked, der fejlede, og sender den igen. */
  async function retry(): Promise<void> {
    if (!canRetry.value || !lastUserText.value) return

    const last = messages.value[messages.value.length - 1]
    if (last?.role === 'error') messages.value.pop()
    const previous = messages.value[messages.value.length - 1]
    if (previous?.role === 'user' && previous.text === lastUserText.value) messages.value.pop()

    await send(lastUserText.value)
  }

  /** "Ny samtale": API'et opretter en ny, når conversationId udelades — intet at kalde. */
  function reset(): void {
    messages.value = []
    conversationId.value = null
    pendingAction.value = null
    lastError.value = null
    lastUserText.value = null
  }

  return {
    messages,
    conversationId,
    isSending,
    pendingAction,
    lastError,
    hasMessages,
    canRetry,
    send,
    confirm,
    reject,
    retry,
    reset,
  }
})
