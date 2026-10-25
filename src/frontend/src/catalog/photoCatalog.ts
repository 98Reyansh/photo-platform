import type { PhotoListOutcome, PhotoMetadata } from '../api/photos'

export const emptyCatalogMessage = 'This studio has no photos yet.'

export const sessionExpiredMessage = 'Your session has expired. Sign in again to view photos.'

export const membershipRequiredMessage = 'A studio membership is required to view photos.'

export const noDescriptionLabel = 'No description'

export type CatalogNotice = {
  kind: 'unauthorized' | 'forbidden' | 'error'
  message: string
}

export type CatalogModel =
  | { status: 'loading' }
  | { status: 'empty' }
  | { status: 'unauthorized' }
  | { status: 'forbidden' }
  | { status: 'error'; message: string }
  | {
      status: 'ready'
      items: PhotoMetadata[]
      cursor: string | null
      pending: boolean
      notice: CatalogNotice | null
    }

export function catalogAfterFirst(outcome: PhotoListOutcome): CatalogModel {
  if (outcome.kind === 'unauthorized') {
    return { status: 'unauthorized' }
  }

  if (outcome.kind === 'forbidden') {
    return { status: 'forbidden' }
  }

  if (outcome.kind === 'recoverable') {
    return { status: 'error', message: outcome.message }
  }

  if (outcome.page.items.length === 0 && outcome.page.continuationCursor === null) {
    return { status: 'empty' }
  }

  return {
    status: 'ready',
    items: outcome.page.items,
    cursor: outcome.page.continuationCursor,
    pending: false,
    notice: null,
  }
}

export function catalogAfterMore(current: Extract<CatalogModel, { status: 'ready' }>, outcome: PhotoListOutcome): CatalogModel {
  if (outcome.kind === 'page') {
    return {
      status: 'ready',
      items: appendPhotos(current.items, outcome.page.items),
      cursor: outcome.page.continuationCursor,
      pending: false,
      notice: null,
    }
  }

  return {
    ...current,
    pending: false,
    notice: noticeFor(outcome),
  }
}

export function appendPhotos(existing: PhotoMetadata[], incoming: PhotoMetadata[]): PhotoMetadata[] {
  const seen = new Set(existing.map((photo) => photo.id))
  const next = existing.slice()
  for (const photo of incoming) {
    if (seen.has(photo.id)) {
      continue
    }

    seen.add(photo.id)
    next.push(photo)
  }

  return next
}

export function showLoadMore(model: CatalogModel): boolean {
  return model.status === 'ready' && model.cursor !== null && !model.pending && model.notice === null
}

export function catalogSessionAfterSignIn(session: number): number {
  return session + 1
}

export function retryCursor(model: CatalogModel): string | null | undefined {
  if (model.status === 'ready' && model.notice !== null) {
    return model.cursor
  }

  if (model.status === 'unauthorized' || model.status === 'forbidden' || model.status === 'error') {
    return null
  }

  return undefined
}

export function catalogRetryLabel(cursor: string | null): string {
  return cursor === null ? 'Retry photo catalog' : 'Retry loading more photos'
}

function noticeFor(outcome: Exclude<PhotoListOutcome, { kind: 'page' }>): CatalogNotice {
  if (outcome.kind === 'unauthorized') {
    return { kind: 'unauthorized', message: sessionExpiredMessage }
  }

  if (outcome.kind === 'forbidden') {
    return { kind: 'forbidden', message: membershipRequiredMessage }
  }

  return { kind: 'error', message: outcome.message }
}
