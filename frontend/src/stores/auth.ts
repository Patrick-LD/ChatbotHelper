import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { getMe } from '@/api/me'
import { ApiError } from '@/api/errors'
import type { MeResponse } from '@/types/api'

/**
 * Nøglen huskes i sessionStorage: den overlever en reload, men ikke at fanen lukkes, og den deles
 * ikke mellem faner. Det er bevidst et niveau under localStorage — en API-nøgle er et password.
 */
export const STORAGE_KEY = 'ist-assistent.apiKey'

function readStoredKey(): string | null {
  try {
    return sessionStorage.getItem(STORAGE_KEY)
  } catch {
    return null
  }
}

function writeStoredKey(key: string | null): void {
  try {
    if (key) sessionStorage.setItem(STORAGE_KEY, key)
    else sessionStorage.removeItem(STORAGE_KEY)
  } catch {
    // Privat vindue eller blokeret lager: så gælder nøglen bare til reload.
  }
}

export const useAuthStore = defineStore('auth', () => {
  const apiKey = ref<string | null>(readStoredKey())
  const user = ref<MeResponse | null>(null)

  /** Der er en nøgle — men den er ikke nødvendigvis bekræftet af API'et endnu (se restore). */
  const hasKey = computed(() => !!apiKey.value)
  const isAuthenticated = computed(() => !!apiKey.value && !!user.value)
  const isAdmin = computed(() => user.value?.roles.includes('admin') ?? false)
  const displayName = computed(() => user.value?.name ?? user.value?.userId ?? '')

  /** Validerer nøglen mod GET /me og gemmer den. Kaster ApiError (401 = ukendt nøgle). */
  async function login(key: string): Promise<MeResponse> {
    const trimmed = key.trim()
    if (!trimmed) {
      throw new ApiError(400, 'Indtast en API-nøgle')
    }

    const me = await getMe(trimmed)
    apiKey.value = trimmed
    user.value = me
    writeStoredKey(trimmed)
    return me
  }

  /**
   * Efter reload: nøglen ligger i sessionStorage, men brugeren skal hentes igen. En 401 (nøglen er
   * fjernet på serveren) logger ud; netværksfejl beholder nøglen, så et midlertidigt udfald ikke smider brugeren ud.
   */
  async function restore(): Promise<boolean> {
    if (!apiKey.value) return false
    if (user.value) return true

    try {
      user.value = await getMe(apiKey.value)
      return true
    } catch (error) {
      if (error instanceof ApiError && error.isUnauthorized) {
        logout()
      }
      return false
    }
  }

  function logout(): void {
    apiKey.value = null
    user.value = null
    writeStoredKey(null)
  }

  return { apiKey, user, hasKey, isAuthenticated, isAdmin, displayName, login, restore, logout }
})
