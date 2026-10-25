import { useState } from 'react'
import { catalogSessionAfterSignIn } from './catalog/photoCatalog'
import { useHealthCheck } from './hooks/useHealthCheck'
import { PhotoCatalog } from './components/PhotoCatalog'
import { SignInPanel } from './components/SignInPanel'

function App() {
  const health = useHealthCheck()
  const [catalogSession, setCatalogSession] = useState(0)

  return (
    <div className="page">
      <header className="masthead">
        <p className="eyebrow">Portfolio</p>
        <h1>Photography Portfolio</h1>
        <p className="intro">
          A place to present photographs, collections, and the work behind them.
          This page checks that the API is available.
        </p>
      </header>

      <main>
        <section className="status-panel" aria-labelledby="api-status-heading">
          <h2 id="api-status-heading">API status</h2>
          <div
            className={`status-indicator status-${health.phase}`}
            role="status"
            aria-live="polite"
            aria-busy={health.phase === 'loading'}
          >
            <span className="status-dot" aria-hidden="true" />
            <p className="status-label">{health.label}</p>
            {health.phase === 'success' && health.detail ? (
              <p className="status-detail">{health.detail}</p>
            ) : null}
          </div>
          {health.phase === 'error' && health.detail ? (
            <p className="error-message" role="alert">
              {health.detail}
            </p>
          ) : null}
          <button
            type="button"
            className="retry-button"
            onClick={health.retry}
            disabled={health.phase === 'loading'}
          >
            Retry
          </button>
        </section>
        <SignInPanel onSignedIn={() => setCatalogSession(catalogSessionAfterSignIn)} />
        {catalogSession > 0 ? <PhotoCatalog key={catalogSession} /> : null}
      </main>
    </div>
  )
}

export default App
