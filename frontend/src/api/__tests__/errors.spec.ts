import { describe, expect, it } from 'vitest'
import { ApiError, errorFromBody, errorFromResponse } from '../errors'

describe('errorFromBody', () => {
  it('oversætter { error } fra autentificeringen til titel', () => {
    const error = errorFromBody(401, { error: 'Autentificering kræves. Send API-nøglen i headeren X-Api-Key.' })
    expect(error).toBeInstanceOf(ApiError)
    expect(error.status).toBe(401)
    expect(error.title).toBe('Autentificering kræves. Send API-nøglen i headeren X-Api-Key.')
    expect(error.detail).toBeUndefined()
    expect(error.isUnauthorized).toBe(true)
  })

  it('oversætter ProblemDetails til titel og detalje', () => {
    const error = errorFromBody(504, {
      type: 'https://tools.ietf.org/html/rfc9110#section-15.6.5',
      title: 'Modellen svarede ikke i tide',
      status: 504,
      detail: 'Ollama nåede ikke at svare inden timeouten.',
    })
    expect(error.title).toBe('Modellen svarede ikke i tide')
    expect(error.detail).toBe('Ollama nåede ikke at svare inden timeouten.')
    expect(error.message).toBe('Modellen svarede ikke i tide Ollama nåede ikke at svare inden timeouten.')
    expect(error.isRetryable).toBe(true)
  })

  it('giver en dansk standardtitel, når body er tom eller ukendt', () => {
    expect(errorFromBody(503, null).title).toBe('Tjenesten er ikke tilgængelig lige nu')
    expect(errorFromBody(418, 'tekst').title).toBe('Uventet fejl (HTTP 418)')
  })

  it('kender forskel på forbudt, uautoriseret og forbigående', () => {
    expect(errorFromBody(403, { title: 'Samtalen tilhører en anden bruger' }).isForbidden).toBe(true)
    expect(new ApiError(0, 'Netværk').isRetryable).toBe(true)
    expect(new ApiError(400, 'Ugyldig').isRetryable).toBe(false)
  })
})

describe('errorFromResponse', () => {
  it('læser JSON-body fra et Response-objekt', async () => {
    const response = new Response(JSON.stringify({ error: 'Feltet \'message\' må ikke være tomt.' }), {
      status: 400,
      headers: { 'Content-Type': 'application/json' },
    })

    const error = await errorFromResponse(response)

    expect(error.status).toBe(400)
    expect(error.title).toContain('må ikke være tomt')
  })

  it('tåler en body, der ikke er JSON', async () => {
    const error = await errorFromResponse(new Response('<html>Bad Gateway</html>', { status: 502 }))
    expect(error.status).toBe(502)
    expect(error.title).toBe('Uventet fejl (HTTP 502)')
  })
})
