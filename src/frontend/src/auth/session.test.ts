import assert from 'node:assert/strict'
import test from 'node:test'
import {
  createCsrfMemory,
  interpretSignInStatus,
  parseMembership,
  signInResultMessage,
  signInToStudio,
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
