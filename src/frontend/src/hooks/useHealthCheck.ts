import { useCallback, useEffect, useState } from 'react'
import { fetchHealth, HealthCheckError } from '../api/health'

export type HealthPhase = 'loading' | 'success' | 'error'

type HealthView = {
  phase: HealthPhase
  label: string
  detail: string | null
  retry: () => void
}

export function useHealthCheck(): HealthView {
  const [attempt, setAttempt] = useState(0)
  const [phase, setPhase] = useState<HealthPhase>('loading')
  const [detail, setDetail] = useState<string | null>(null)

  const retry = useCallback(() => {
    setPhase('loading')
    setDetail(null)
    setAttempt((current) => current + 1)
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    let active = true

    fetchHealth(controller.signal)
      .then((health) => {
        if (!active) {
          return
        }

        if (health.status.toLowerCase() !== 'healthy') {
          setPhase('error')
          setDetail(`The API reported a status of "${health.status.slice(0, 80)}".`)
          return
        }

        setPhase('success')
        setDetail(`Last checked ${formatUtc(health.timestamp)}.`)
      })
      .catch((error: unknown) => {
        if (!active || isAbortError(error)) {
          return
        }

        setPhase('error')
        setDetail(
          error instanceof HealthCheckError
            ? error.message
            : 'The API could not be reached. Check that it is running and try again.',
        )
      })

    return () => {
      active = false
      controller.abort()
    }
  }, [attempt])

  const label =
    phase === 'loading'
      ? 'Checking the API…'
      : phase === 'success'
        ? 'The API is running.'
        : 'The API is unavailable.'

  return { phase, label, detail, retry }
}

function formatUtc(timestamp: string): string {
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'long',
    timeZone: 'UTC',
  }).format(new Date(timestamp))
}

function isAbortError(error: unknown): boolean {
  return (
    (error instanceof DOMException && error.name === 'AbortError') ||
    (error instanceof Error && error.name === 'AbortError')
  )
}
