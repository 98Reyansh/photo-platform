export type CurrentMembership = {
  tenantId: string
  name: string
  slug: string
  status: string
}

export type SignInResult =
  | { ok: false; message: string }
  | { ok: true; membership: CurrentMembership }

const failureMessage = 'The email or password is incorrect.'

export function createCsrfMemory() {
  let token: string | null = null

  return {
    remember(next: string) {
      token = next
    },
    current() {
      return token
    },
  }
}

export function interpretSignInStatus(status: number): 'signed-in' | 'failed' {
  return status === 204 ? 'signed-in' : 'failed'
}

export function parseMembership(value: unknown): CurrentMembership | null {
  if (typeof value !== 'object' || value === null) {
    return null
  }

  const record = value as Record<string, unknown>
  if (
    typeof record.tenantId !== 'string' ||
    typeof record.name !== 'string' ||
    typeof record.slug !== 'string' ||
    typeof record.status !== 'string' ||
    record.tenantId.trim().length === 0
  ) {
    return null
  }

  return {
    tenantId: record.tenantId,
    name: record.name,
    slug: record.slug,
    status: record.status,
  }
}

export function signInResultMessage(result: SignInResult): string {
  return result.ok ? `Signed in to ${result.membership.name}.` : result.message
}

export async function signInToStudio(
  fetchImpl: typeof fetch,
  baseUrl: string,
  email: string,
  password: string,
  csrf: ReturnType<typeof createCsrfMemory>,
): Promise<SignInResult> {
  const root = baseUrl.replace(/\/+$/, '')
  const csrfResponse = await fetchImpl(`${root}/api/v1/auth/csrf`, {
    method: 'GET',
    credentials: 'include',
    headers: { Accept: 'application/json' },
  })
  if (!csrfResponse.ok) {
    return { ok: false, message: failureMessage }
  }

  const csrfBody: unknown = await csrfResponse.json()
  const token =
    typeof csrfBody === 'object' &&
    csrfBody !== null &&
    typeof (csrfBody as { token?: unknown }).token === 'string'
      ? (csrfBody as { token: string }).token
      : null
  if (!token) {
    return { ok: false, message: failureMessage }
  }

  csrf.remember(token)
  const signInResponse = await fetchImpl(`${root}/api/v1/auth/sign-in`, {
    method: 'POST',
    credentials: 'include',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
      'X-CSRF-TOKEN': token,
    },
    body: JSON.stringify({ email, password }),
  })
  if (interpretSignInStatus(signInResponse.status) !== 'signed-in') {
    return { ok: false, message: failureMessage }
  }

  const membershipResponse = await fetchImpl(`${root}/api/v1/membership/current`, {
    method: 'GET',
    credentials: 'include',
    headers: { Accept: 'application/json' },
  })
  if (!membershipResponse.ok) {
    return { ok: false, message: failureMessage }
  }

  const membership = parseMembership(await membershipResponse.json())
  if (!membership) {
    return { ok: false, message: failureMessage }
  }

  return { ok: true, membership }
}
