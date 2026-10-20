export type HealthResponse = {
  status: string
  timestamp: string
}

export class HealthCheckError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'HealthCheckError'
  }
}

export async function fetchHealth(signal?: AbortSignal): Promise<HealthResponse> {
  const baseUrl = import.meta.env.VITE_API_BASE_URL?.trim()
  if (!baseUrl) {
    throw new HealthCheckError(
      'The API address is not configured. Set VITE_API_BASE_URL and restart the frontend.',
    )
  }

  let response: Response
  try {
    response = await fetch(joinUrl(baseUrl, '/api/v1/health'), {
      method: 'GET',
      headers: { Accept: 'application/json' },
      cache: 'no-store',
      signal,
    })
  } catch (error) {
    if (isAbortError(error)) {
      throw error
    }

    throw new HealthCheckError(
      'The API could not be reached. Check that it is running and try again.',
    )
  }

  if (!response.ok) {
    throw new HealthCheckError(`The API returned an error (HTTP ${response.status}).`)
  }

  let body: unknown
  try {
    body = await response.json()
  } catch {
    throw new HealthCheckError('The API returned an unexpected response.')
  }

  if (!isHealthResponse(body)) {
    throw new HealthCheckError('The API returned an unexpected response.')
  }

  return body
}

function joinUrl(baseUrl: string, path: string): string {
  return `${baseUrl.replace(/\/+$/, '')}${path}`
}

function isHealthResponse(value: unknown): value is HealthResponse {
  if (typeof value !== 'object' || value === null) {
    return false
  }

  const record = value as Record<string, unknown>
  return (
    typeof record.status === 'string' &&
    record.status.trim().length > 0 &&
    typeof record.timestamp === 'string' &&
    Number.isFinite(Date.parse(record.timestamp))
  )
}

function isAbortError(error: unknown): boolean {
  return (
    (error instanceof DOMException && error.name === 'AbortError') ||
    (error instanceof Error && error.name === 'AbortError')
  )
}
