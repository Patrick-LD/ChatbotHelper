import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ApiError } from '@/api/errors'
import type { MeResponse } from '@/types/api'
import { STORAGE_KEY, useAuthStore } from '../auth'

const getMe = vi.hoisted(() => vi.fn<() => Promise<MeResponse>>())
vi.mock('@/api/me', () => ({ getMe }))

const hanne = { userId: 'hanne.hr', name: 'Hanne HR', roles: ['medarbejder', 'hr'] }

describe('auth-store', () => {
  beforeEach(() => {
    sessionStorage.clear()
    getMe.mockReset()
    setActivePinia(createPinia())
  })

  it('login validerer nøglen mod /me og gemmer den i sessionStorage', async () => {
    getMe.mockResolvedValueOnce(hanne)
    const auth = useAuthStore()

    await auth.login('  dev-hr-2026-noegle ')

    expect(getMe).toHaveBeenCalledWith('dev-hr-2026-noegle')
    expect(auth.isAuthenticated).toBe(true)
    expect(auth.displayName).toBe('Hanne HR')
    expect(auth.isAdmin).toBe(false)
    expect(sessionStorage.getItem(STORAGE_KEY)).toBe('dev-hr-2026-noegle')
  })

  it('login med ukendt nøgle kaster og gemmer intet', async () => {
    getMe.mockRejectedValueOnce(new ApiError(401, 'Autentificering kræves'))
    const auth = useAuthStore()

    await expect(auth.login('forkert-noegle-123456')).rejects.toBeInstanceOf(ApiError)
    expect(auth.hasKey).toBe(false)
    expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull()
  })

  it('login med tom nøgle afvises uden kald', async () => {
    const auth = useAuthStore()
    await expect(auth.login('   ')).rejects.toMatchObject({ status: 400 })
    expect(getMe).not.toHaveBeenCalled()
  })

  it('restore henter brugeren ud fra den gemte nøgle', async () => {
    sessionStorage.setItem(STORAGE_KEY, 'dev-admin-2026-noegle')
    getMe.mockResolvedValueOnce({ ...hanne, userId: 'adam.admin', name: 'Adam Admin', roles: ['admin'] })
    const auth = useAuthStore()

    expect(auth.hasKey).toBe(true)
    expect(auth.isAuthenticated).toBe(false)
    expect(await auth.restore()).toBe(true)
    expect(auth.isAdmin).toBe(true)
  })

  it('restore logger ud ved 401, men beholder nøglen ved netværksfejl', async () => {
    sessionStorage.setItem(STORAGE_KEY, 'dev-hr-2026-noegle')
    getMe.mockRejectedValueOnce(new ApiError(0, 'Kunne ikke kontakte serveren'))
    const auth = useAuthStore()
    expect(await auth.restore()).toBe(false)
    expect(auth.hasKey).toBe(true)

    getMe.mockRejectedValueOnce(new ApiError(401, 'Autentificering kræves'))
    expect(await auth.restore()).toBe(false)
    expect(auth.hasKey).toBe(false)
    expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull()
  })
})
