import assert from 'node:assert/strict'
import test from 'node:test'
import {
  createCsrfMemory,
  interpretSignInStatus,
  loadCurrentSession,
  parseMembership,
  signInResultMessage,
  signInToStudio,
  signOutOfStudio,
} from './session.ts'

test('a failed sign-in is not reported as success', () => {
  assert.equal(interpretSignInStatus(401), 'failed')
  assert.equal(interpretSignInStatus(400), 'failed')
  assert.equal(
    signInResultMessage({ ok: false, message: 'The email or password is incorrect.' }),
    'The email or password is incorrect.',
  )
  assert.equal(signInResultMessage({ ok: false, message: 'The email or password is incorrect.' }).includes('Signed in'), false)
})

test('membership from another shape is rejected', () => {
  assert.equal(parseMembership({ tenantId: 'other-studio' }), null)
  assert.equal(parseMembership(null), null)
})

test('csrf token stays in memory and is not written to localStorage', () => {
  const storage = {
    setItem() {
      throw new Error('localStorage was used')
    },
    getItem() {
      throw new Error('localStorage was used')
    },
  }
  Object.defineProperty(globalThis, 'localStorage', { value: storage, configurable: true })
  const csrf = createCsrfMemory()
  csrf.remember('token-value')
  assert.equal(csrf.current(), 'token-value')
})

test('sign-in uses credentialed requests and only reports success after membership', async () => {
  const calls: Array<{ url: string; credentials?: RequestCredentials }> = []
  const fetchImpl: typeof fetch = async (input, init) => {
    const url = String(input)
    calls.push({ url, credentials: init?.credentials })
    if (url.endsWith('/api/v1/auth/csrf')) {
      return new Response(JSON.stringify({ token: 'csrf-token' }), { status: 200 })
    }

    if (url.endsWith('/api/v1/auth/sign-in')) {
      assert.equal(init?.headers && (init.headers as Record<string, string>)['X-CSRF-TOKEN'], 'csrf-token')
      return new Response(null, { status: 204 })
    }

    return new Response(
      JSON.stringify({
        tenantId: '11111111-1111-7111-8111-111111111111',
        name: 'North Studio',
        slug: 'north-studio',
        status: 'Active',
      }),
      { status: 200 },
    )
  }

  const result = await signInToStudio(
    fetchImpl,
    'http://localhost:5080',
    'member@example.test',
    'not-stored',
    createCsrfMemory(),
  )

  assert.equal(result.ok, true)
  if (result.ok) {
    assert.equal(result.membership.name, 'North Studio')
  }
  assert.equal(calls.every((call) => call.credentials === 'include'), true)
})

test('an existing cookie session restores membership without storage', async () => {
  const storage = {
    setItem() {
      throw new Error('web storage was used')
    },
    getItem() {
      throw new Error('web storage was used')
    },
  }
  Object.defineProperty(globalThis, 'localStorage', { value: storage, configurable: true })
  Object.defineProperty(globalThis, 'sessionStorage', { value: storage, configurable: true })
  const calls: Array<{ url: string; credentials?: RequestCredentials }> = []
  const fetchImpl: typeof fetch = async (input, init) => {
    calls.push({ url: String(input), credentials: init?.credentials })
    return new Response(
      JSON.stringify({
        tenantId: '11111111-1111-7111-8111-111111111111',
        name: 'Lensora Studio',
        slug: 'lensora-studio',
        status: 'Active',
      }),
      { status: 200 },
    )
  }

  const restored = await loadCurrentSession(fetchImpl, 'http://localhost:5080/')

  assert.equal(restored.status, 'authenticated')
  if (restored.status === 'authenticated') {
    assert.equal(restored.membership.slug, 'lensora-studio')
  }
  assert.equal(calls.length, 1)
  assert.equal(calls[0]?.url, 'http://localhost:5080/api/v1/membership/current')
  assert.equal(calls[0]?.credentials, 'include')
})

test('missing and forbidden sessions stay signed out', async () => {
  for (const status of [401, 403]) {
    const fetchImpl: typeof fetch = async () => new Response(null, { status })
    const result = await loadCurrentSession(fetchImpl, 'http://localhost:5080')
    assert.equal(result.status, 'anonymous')
  }
})

test('sign-out sends a fresh csrf token and does not store the session', async () => {
  const calls: Array<{ url: string; init?: RequestInit }> = []
  const fetchImpl: typeof fetch = async (input, init) => {
    const url = String(input)
    calls.push({ url, init })
    if (url.endsWith('/api/v1/auth/csrf')) {
      return new Response(JSON.stringify({ token: 'csrf-token' }), { status: 200 })
    }

    assert.ok(init)
    assert.equal(init.method, 'POST')
    assert.equal(init.body, undefined)
    const headers = init.headers as Record<string, string>
    assert.equal(headers['X-CSRF-TOKEN'], 'csrf-token')
    assert.equal(JSON.stringify(init).includes('password'), false)
    return new Response(null, { status: 204 })
  }

  const result = await signOutOfStudio(fetchImpl, 'http://localhost:5080')

  assert.equal(result, 'signed-out')
  assert.equal(calls.every((call) => call.init?.credentials === 'include'), true)
  assert.equal(calls.some((call) => call.url.endsWith('/api/v1/auth/sign-out')), true)
})
