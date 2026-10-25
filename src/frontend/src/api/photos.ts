export const photoPageLimit = 20

export const photoUnreachableMessage =
  'The photo catalog could not be reached. Check that the API is running and try again.'

export const photoUnexpectedMessage = 'The photo catalog returned an unexpected response.'

export const photoUnconfiguredMessage = 'The API address is not configured.'

export type PhotoMetadata = {
  id: string
  tenantId: string
  title: string
  description: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

export type PhotoPage = {
  items: PhotoMetadata[]
  continuationCursor: string | null
}

export type PhotoListOutcome =
  | { kind: 'page'; page: PhotoPage }
  | { kind: 'unauthorized' }
  | { kind: 'forbidden' }
  | { kind: 'recoverable'; message: string }

export async function fetchPhotoPage(
  fetchImpl: typeof fetch,
  baseUrl: string,
  cursor: string | null,
  signal?: AbortSignal,
): Promise<PhotoListOutcome> {
  const root = baseUrl.trim().replace(/\/+$/, '')
  if (!root) {
    return { kind: 'recoverable', message: photoUnconfiguredMessage }
  }

  let response: Response
  try {
    response = await fetchImpl(photoListUrl(root, cursor), {
      method: 'GET',
      credentials: 'include',
      headers: { Accept: 'application/json' },
      cache: 'no-store',
      signal,
    })
  } catch (error) {
    if (isAbortError(error)) {
      throw error
    }

    return { kind: 'recoverable', message: photoUnreachableMessage }
  }

  if (response.status === 401) {
    return { kind: 'unauthorized' }
  }

  if (response.status === 403) {
    return { kind: 'forbidden' }
  }

  if (!response.ok) {
    return {
      kind: 'recoverable',
      message: `The photo catalog returned an error (HTTP ${response.status}).`,
    }
  }

  let body: unknown
  try {
    body = await response.json()
  } catch {
    return { kind: 'recoverable', message: photoUnexpectedMessage }
  }

  const page = parsePhotoPage(body)
  if (!page) {
    return { kind: 'recoverable', message: photoUnexpectedMessage }
  }

  return { kind: 'page', page }
}

export function parsePhotoPage(value: unknown): PhotoPage | null {
  if (typeof value !== 'object' || value === null) {
    return null
  }

  const record = value as Record<string, unknown>
  if (!Array.isArray(record.items)) {
    return null
  }

  if (record.continuationCursor !== null && typeof record.continuationCursor !== 'string') {
    return null
  }

  if (typeof record.continuationCursor === 'string' && record.continuationCursor.length === 0) {
    return null
  }

  const items: PhotoMetadata[] = []
  for (const item of record.items) {
    const photo = parsePhoto(item)
    if (!photo) {
      return null
    }

    items.push(photo)
  }

  return { items, continuationCursor: record.continuationCursor }
}

function photoListUrl(root: string, cursor: string | null): string {
  const query = new URLSearchParams({ limit: String(photoPageLimit) })
  if (cursor !== null) {
    query.set('cursor', cursor)
  }

  return `${root}/api/v1/photos?${query.toString()}`
}

function parsePhoto(value: unknown): PhotoMetadata | null {
  if (typeof value !== 'object' || value === null) {
    return null
  }

  const record = value as Record<string, unknown>
  if (!isPresentString(record.id) || !isPresentString(record.tenantId) || !isPresentString(record.title)) {
    return null
  }

  if (record.description !== null && typeof record.description !== 'string') {
    return null
  }

  if (!isUtcTimestamp(record.createdAtUtc) || !isUtcTimestamp(record.updatedAtUtc)) {
    return null
  }

  return {
    id: record.id,
    tenantId: record.tenantId,
    title: record.title,
    description: record.description,
    createdAtUtc: record.createdAtUtc,
    updatedAtUtc: record.updatedAtUtc,
  }
}

function isPresentString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isUtcTimestamp(value: unknown): value is string {
  return typeof value === 'string' && Number.isFinite(Date.parse(value))
}

function isAbortError(error: unknown): boolean {
  return (
    (error instanceof DOMException && error.name === 'AbortError') ||
    (error instanceof Error && error.name === 'AbortError')
  )
}
