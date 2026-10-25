import type { HealthView } from '../hooks/useHealthCheck'

export function ApiStatus({ health }: { health: HealthView }) {
  return (
    <section className="api-health" aria-labelledby="api-status-heading">
      <h2 id="api-status-heading">API status</h2>
      <p className="api-health-note">This check does not sign you in.</p>
      <div
        className={`status-indicator status-${health.phase}`}
        role="status"
        aria-live="polite"
        aria-busy={health.phase === 'loading'}
      >
        <span className="status-dot" aria-hidden="true" />
        <p className="status-label">{health.label}</p>
        {health.phase === 'success' && health.detail ? <p className="status-detail">{health.detail}</p> : null}
      </div>
      {health.phase === 'error' && health.detail ? (
        <p className="error-message" role="alert">
          {health.detail}
        </p>
      ) : null}
      <button type="button" className="retry-button" onClick={health.retry} disabled={health.phase === 'loading'}>
        Retry API check
      </button>
    </section>
  )
}
