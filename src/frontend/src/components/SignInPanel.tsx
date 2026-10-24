import { useState, type FormEvent } from 'react'
import { createCsrfMemory, signInResultMessage, signInToStudio, type SignInResult } from '../auth/session'

const csrfMemory = createCsrfMemory()

export function SignInPanel() {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [pending, setPending] = useState(false)
  const [result, setResult] = useState<SignInResult | null>(null)

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const baseUrl = import.meta.env.VITE_API_BASE_URL?.trim()
    if (!baseUrl) {
      setResult({ ok: false, message: 'The API address is not configured.' })
      return
    }

    setPending(true)
    try {
      const next = await signInToStudio(fetch, baseUrl, email, password, csrfMemory)
      setResult(next)
    } catch {
      setResult({ ok: false, message: 'The API could not be reached. Check that it is running and try again.' })
    } finally {
      setPending(false)
    }
  }

  return (
    <section className="status-panel sign-in-panel" aria-labelledby="sign-in-heading">
      <h2 id="sign-in-heading">Studio sign-in</h2>
      <form onSubmit={onSubmit}>
        <label className="field" htmlFor="sign-in-email">
          Email
          <input
            id="sign-in-email"
            type="email"
            autoComplete="username"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            required
          />
        </label>
        <label className="field" htmlFor="sign-in-password">
          Password
          <input
            id="sign-in-password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            required
          />
        </label>
        <button className="retry-button" type="submit" disabled={pending}>
          {pending ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
      {result ? (
        <p className={result.ok ? 'sign-in-success' : 'error-message'} role={result.ok ? 'status' : 'alert'}>
          {signInResultMessage(result)}
        </p>
      ) : null}
    </section>
  )
}
