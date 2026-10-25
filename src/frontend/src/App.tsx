import { useEffect, useState } from 'react'
import { loadCurrentSession, signOutOfStudio, type CurrentMembership } from './auth/session'
import { catalogSessionAfterSignIn } from './catalog/photoCatalog'
import { useHealthCheck } from './hooks/useHealthCheck'
import { ApiStatus } from './components/ApiStatus'
import { SignInPanel } from './components/SignInPanel'
import { StudioHome } from './components/StudioHome'
import { viewForSession } from './studio/studioHome'

type StudioAccess =
  | { phase: 'loading' }
  | { phase: 'unavailable' }
  | { phase: 'anonymous' }
  | { phase: 'signed-in'; membership: CurrentMembership; catalogSession: number }

function App() {
  const health = useHealthCheck()
  const [attempt, setAttempt] = useState(0)
  const [session, setSession] = useState<StudioAccess>({ phase: 'loading' })
  const [signingOut, setSigningOut] = useState(false)
  const [signOutError, setSignOutError] = useState<string | null>(null)

  useEffect(() => {
    let active = true
    const baseUrl = import.meta.env.VITE_API_BASE_URL?.trim() ?? ''

    loadCurrentSession(fetch, baseUrl).then((lookup) => {
      if (!active) {
        return
      }

      if (lookup.status === 'authenticated') {
        setSession({
          phase: 'signed-in',
          membership: lookup.membership,
          catalogSession: catalogSessionAfterSignIn(0),
        })
        return
      }

      setSession({ phase: lookup.status === 'anonymous' ? 'anonymous' : 'unavailable' })
    })

    return () => {
      active = false
    }
  }, [attempt])

  const view =
    session.phase === 'signed-in' ? viewForSession('signed-in', session.membership) : viewForSession(session.phase)

  function signOut() {
    const baseUrl = import.meta.env.VITE_API_BASE_URL?.trim() ?? ''
    setSigningOut(true)
    setSignOutError(null)
    signOutOfStudio(fetch, baseUrl)
      .then((result) => {
        if (result === 'signed-out') {
          setSession({ phase: 'anonymous' })
          return
        }

        setSignOutError('Sign-out did not complete. The studio session is unchanged.')
      })
      .finally(() => setSigningOut(false))
  }

  return (
    <div className={view.kind === 'dashboard' ? 'page is-studio' : 'page'}>
      {view.kind === 'dashboard' && session.phase === 'signed-in' ? (
        <StudioHome
          studioName={view.studioName}
          destinations={view.destinations}
          catalogKey={session.catalogSession}
          signingOut={signingOut}
          signOutError={signOutError}
          onSignOut={signOut}
        />
      ) : (
        <main className="signed-out" id="sign-in">
          <p className="brand">Lensora</p>
          <h1>Sign in</h1>
          <p className="intro">Sign in to open your studio. A running API does not sign you in.</p>
          {view.kind === 'loading' ? (
            <section className="status-panel" aria-labelledby="session-heading">
              <h2 id="session-heading">Studio session</h2>
              <p className="status-label" role="status" aria-live="polite" aria-busy="true">
                {view.message}
              </p>
            </section>
          ) : null}
          {view.kind === 'unavailable' ? (
            <section className="status-panel" aria-labelledby="session-heading">
              <h2 id="session-heading">Studio session</h2>
              <p className="error-message" role="alert">
                {view.message}
              </p>
              <button
                type="button"
                className="retry-button"
                onClick={() => {
                  setSession({ phase: 'loading' })
                  setAttempt((current) => current + 1)
                }}
              >
                {view.retryLabel}
              </button>
            </section>
          ) : null}
          {view.kind === 'signed-out' ? (
            <SignInPanel
              onSignedIn={(membership) => {
                setSignOutError(null)
                setSession({
                  phase: 'signed-in',
                  membership,
                  catalogSession: catalogSessionAfterSignIn(0),
                })
              }}
            />
          ) : null}
        </main>
      )}
      <ApiStatus health={health} />
    </div>
  )
}

export default App
