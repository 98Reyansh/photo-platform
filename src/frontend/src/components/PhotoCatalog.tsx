import { useEffect, useRef, useState } from 'react'
import { fetchPhotoPage, photoUnreachableMessage, type PhotoListOutcome, type PhotoMetadata } from '../api/photos'
import {
  catalogAfterFirst,
  catalogAfterMore,
  catalogRetryLabel,
  emptyCatalogMessage,
  membershipRequiredMessage,
  noDescriptionLabel,
  retryCursor,
  sessionExpiredMessage,
  showLoadMore,
  type CatalogModel,
} from '../catalog/photoCatalog'

export function PhotoCatalog() {
  const [model, setModel] = useState<CatalogModel>({ status: 'loading' })
  const pending = useRef(false)

  async function load(cursor: string | null, mode: 'first' | 'more', signal?: AbortSignal) {
    if (pending.current) {
      return
    }

    pending.current = true
    if (mode === 'first') {
      setModel({ status: 'loading' })
    } else {
      setModel((current) => (current.status === 'ready' ? { ...current, pending: true, notice: null } : current))
    }

    let outcome: PhotoListOutcome
    try {
      const baseUrl = import.meta.env.VITE_API_BASE_URL?.trim() ?? ''
      outcome = await fetchPhotoPage(fetch, baseUrl, cursor, signal)
    } catch (error) {
      pending.current = false
      if (isAbortError(error)) {
        return
      }

      outcome = { kind: 'recoverable', message: photoUnreachableMessage }
    }

    pending.current = false
    setModel((current) => {
      if (mode === 'more' && current.status === 'ready') {
        return catalogAfterMore(current, outcome)
      }

      return catalogAfterFirst(outcome)
    })
  }

  useEffect(() => {
    const controller = new AbortController()
    let active = true
    const baseUrl = import.meta.env.VITE_API_BASE_URL?.trim() ?? ''

    fetchPhotoPage(fetch, baseUrl, null, controller.signal)
      .then((outcome) => {
        if (active) {
          setModel(catalogAfterFirst(outcome))
        }
      })
      .catch((error: unknown) => {
        if (!active || isAbortError(error)) {
          return
        }

        setModel(catalogAfterFirst({ kind: 'recoverable', message: photoUnreachableMessage }))
      })

    return () => {
      active = false
      controller.abort()
    }
  }, [])

  const cursor = retryCursor(model)
  const loadingMore = model.status === 'ready' && model.pending

  return (
    <section className="status-panel photo-catalog" aria-labelledby="photo-catalog-heading" aria-busy={model.status === 'loading' || loadingMore}>
      <h2 id="photo-catalog-heading">Studio photos</h2>
      {model.status === 'loading' ? <p className="status-label">Loading photos…</p> : null}
      {model.status === 'empty' ? <p className="photo-empty">{emptyCatalogMessage}</p> : null}
      {model.status === 'unauthorized' ? <CatalogAlert message={sessionExpiredMessage} /> : null}
      {model.status === 'forbidden' ? <CatalogAlert message={membershipRequiredMessage} /> : null}
      {model.status === 'error' ? <CatalogAlert message={model.message} /> : null}
      {model.status === 'ready' && model.notice ? <CatalogAlert message={model.notice.message} /> : null}
      {model.status === 'ready' ? <PhotoList items={model.items} /> : null}
      {loadingMore ? <p className="status-label">Loading more photos…</p> : null}
      <div className="photo-actions">
        {showLoadMore(model) && model.status === 'ready' ? (
          <button type="button" className="retry-button" onClick={() => void load(model.cursor, 'more')}>
            Load more
          </button>
        ) : null}
        {cursor !== undefined ? (
          <button
            type="button"
            className="retry-button"
            aria-label={catalogRetryLabel(cursor)}
            onClick={() => void load(cursor, cursor === null ? 'first' : 'more')}
          >
            Retry
          </button>
        ) : null}
      </div>
    </section>
  )
}

function PhotoList({ items }: { items: PhotoMetadata[] }) {
  if (items.length === 0) {
    return null
  }

  return (
    <ul className="photo-list">
      {items.map((photo) => (
        <li className="photo-card" key={photo.id}>
          <h3>{photo.title}</h3>
          <p className={photo.description === null ? 'photo-description missing' : 'photo-description'}>
            {photo.description ?? noDescriptionLabel}
          </p>
          <p className="photo-times">
            <span>Created {formatUtc(photo.createdAtUtc)}</span>
            <span>Updated {formatUtc(photo.updatedAtUtc)}</span>
          </p>
        </li>
      ))}
    </ul>
  )
}

function CatalogAlert({ message }: { message: string }) {
  return (
    <p className="error-message" role="alert">
      {message}
    </p>
  )
}

function formatUtc(value: string): string {
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) {
    return 'Unknown time'
  }

  const formatted = new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
    timeZone: 'UTC',
  }).format(parsed)
  return `${formatted} UTC`
}

function isAbortError(error: unknown): boolean {
  return (
    (error instanceof DOMException && error.name === 'AbortError') ||
    (error instanceof Error && error.name === 'AbortError')
  )
}
