import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ApiError } from '@/api/errors'
import type { ChatResponse } from '@/types/api'
import { CONFIRM_WORD, REJECT_WORD, useChatStore } from '../chat'
import { useAuthStore } from '../auth'

const postChat = vi.hoisted(() => vi.fn<() => Promise<ChatResponse>>())
vi.mock('@/api/chat', () => ({ postChat }))

function reply(overrides: Partial<ChatResponse> = {}): ChatResponse {
  return {
    reply: 'Du har 25 feriedage om året.',
    conversationId: 'c1',
    sources: [{ source: 'personalehaandbog.md', heading: 'Ferie', score: 0.73 }],
    pendingAction: null,
    ...overrides,
  }
}

describe('chat-store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    sessionStorage.clear()
    postChat.mockReset()
    const auth = useAuthStore()
    auth.apiKey = 'dev-hr-2026-noegle'
    auth.user = { userId: 'hanne.hr', name: 'Hanne HR', roles: ['medarbejder', 'hr'] }
  })

  it('send tilføjer bruger- og assistentbesked og husker conversationId', async () => {
    postChat.mockResolvedValueOnce(reply())
    const chat = useChatStore()

    await chat.send('Hvor mange feriedage har jeg?')

    expect(postChat).toHaveBeenCalledWith(
      { message: 'Hvor mange feriedage har jeg?', conversationId: null },
      'dev-hr-2026-noegle',
    )
    expect(chat.messages.map((m) => m.role)).toEqual(['user', 'assistant'])
    expect(chat.messages[1]?.sources).toHaveLength(1)
    expect(chat.conversationId).toBe('c1')
    expect(chat.isSending).toBe(false)

    postChat.mockResolvedValueOnce(reply({ reply: 'Og 5 feriefridage.' }))
    await chat.send('Og feriefridage?')
    expect(postChat).toHaveBeenLastCalledWith(expect.objectContaining({ conversationId: 'c1' }), expect.any(String))
  })

  it('ignorerer tomme beskeder og dobbeltsend', async () => {
    const chat = useChatStore()
    await chat.send('   ')
    expect(postChat).not.toHaveBeenCalled()

    let resolve!: (value: ChatResponse) => void
    postChat.mockReturnValueOnce(new Promise<ChatResponse>((r) => (resolve = r)))
    const first = chat.send('Hej')
    await chat.send('Hej igen')
    expect(postChat).toHaveBeenCalledTimes(1)
    resolve(reply())
    await first
  })

  it('sætter pendingAction fra svaret, og "ja"/"nej" sendes ordret', async () => {
    postChat.mockResolvedValueOnce(reply({ reply: 'Skal jeg oprette?', pendingAction: 'Opret Mette Nielsen (…)' }))
    const chat = useChatStore()
    await chat.send('Opret Mette Nielsen, …')
    expect(chat.pendingAction).toBe('Opret Mette Nielsen (…)')

    postChat.mockResolvedValueOnce(reply({ reply: 'Mette Nielsen er nu oprettet.', pendingAction: null }))
    await chat.confirm()
    expect(postChat).toHaveBeenLastCalledWith(expect.objectContaining({ message: CONFIRM_WORD }), expect.any(String))
    expect(chat.pendingAction).toBeNull()
    expect(chat.messages[chat.messages.length - 2]?.text).toBe(CONFIRM_WORD)

    postChat.mockResolvedValueOnce(reply({ pendingAction: 'Opret Lars' }))
    await chat.send('Opret Lars …')
    postChat.mockResolvedValueOnce(reply({ reply: 'Annulleret.', pendingAction: null }))
    await chat.reject()
    expect(postChat).toHaveBeenLastCalledWith(expect.objectContaining({ message: REJECT_WORD }), expect.any(String))
  })

  it('viser en fejlboble ved 503 og kan prøve igen uden dubletter', async () => {
    postChat.mockRejectedValueOnce(new ApiError(503, 'Modellen kunne ikke kontaktes', 'Kører Ollama?'))
    const chat = useChatStore()

    await chat.send('Hej')

    expect(chat.messages.map((m) => m.role)).toEqual(['user', 'error'])
    expect(chat.lastError?.status).toBe(503)
    expect(chat.canRetry).toBe(true)

    postChat.mockResolvedValueOnce(reply({ reply: 'Hej!' }))
    await chat.retry()

    expect(chat.messages.map((m) => m.role)).toEqual(['user', 'assistant'])
    expect(chat.canRetry).toBe(false)
  })

  it('logger ud ved 401 uden at efterlade en fejlboble', async () => {
    postChat.mockRejectedValueOnce(new ApiError(401, 'Autentificering kræves'))
    const chat = useChatStore()
    const auth = useAuthStore()

    await chat.send('Hej')

    expect(auth.hasKey).toBe(false)
    expect(chat.messages.map((m) => m.role)).toEqual(['user'])
  })

  it('nulstiller samtalen ved 403 (ejerskab), så næste besked starter forfra', async () => {
    postChat.mockResolvedValueOnce(reply())
    const chat = useChatStore()
    await chat.send('Hej')
    expect(chat.conversationId).toBe('c1')

    postChat.mockRejectedValueOnce(new ApiError(403, 'Samtalen tilhører en anden bruger', 'Start en ny samtale.'))
    await chat.send('Mere')

    expect(chat.conversationId).toBeNull()
    expect(chat.messages[chat.messages.length - 1]?.role).toBe('error')
  })

  it('reset tømmer alt', async () => {
    postChat.mockResolvedValueOnce(reply({ pendingAction: 'Opret X' }))
    const chat = useChatStore()
    await chat.send('Opret X')

    chat.reset()

    expect(chat.messages).toEqual([])
    expect(chat.conversationId).toBeNull()
    expect(chat.pendingAction).toBeNull()
    expect(chat.hasMessages).toBe(false)
  })
})
