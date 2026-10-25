import { useState, type ReactNode } from 'react'
import type { CatalogModel } from '../catalog/photoCatalog'
import {
  collectionsDetail,
  collectionsFigure,
  photoFigure,
  uploadUnavailableMessage,
  type StudioDestination,
} from '../studio/studioHome'
import { PhotoCatalog } from './PhotoCatalog'

type StudioHomeProps = {
  studioName: string
  destinations: readonly StudioDestination[]
  catalogKey: number
  signingOut: boolean
  signOutError: string | null
  onSignOut: () => void
}

export function StudioHome({
  studioName,
  destinations,
  catalogKey,
  signingOut,
  signOutError,
  onSignOut,
}: StudioHomeProps) {
  const [catalog, setCatalog] = useState<CatalogModel>({ status: 'loading' })
  const photos = photoFigure(catalog)

  return (
    <div className="studio-home">
      <header className="studio-header">
        <div className="brand-lockup">
          <span className="brand-mark" aria-hidden="true">
            <Icon>
              <circle cx="12" cy="12" r="7.25" />
              <path d="M12 4.75 14.35 9.05" />
              <path d="m14.35 9.05 4.7.85" />
              <path d="M19.05 9.9 15.85 13.55" />
              <path d="m15.85 13.55.55 4.75" />
              <path d="M16.4 18.3 12 16.15" />
              <path d="m12 16.15-4.4 2.15" />
              <path d="M7.6 18.3 8.15 13.55" />
              <path d="M8.15 13.55 4.95 9.9" />
              <path d="M4.95 9.9 9.65 9.05" />
              <path d="M9.65 9.05 12 4.75" />
              <circle cx="12" cy="12" r="1.7" />
            </Icon>
          </span>
          <div>
            <p className="brand-name">Lensora</p>
            <p className="brand-kicker">Studio workspace</p>
          </div>
        </div>
        <nav className="studio-nav" aria-label="Studio">
          {destinations.map((item) =>
            'href' in item ? (
              <a key={item.id} href={item.href} aria-current={item.current ? 'page' : undefined}>
                {item.label}
              </a>
            ) : (
              <button key={item.id} type="button" disabled>
                {item.label}
                <span className="coming-soon">{item.comingSoon}</span>
              </button>
            ),
          )}
        </nav>
        <section className="account" aria-labelledby="account-heading">
          <h2 id="account-heading" className="visually-hidden">
            Account
          </h2>
          <p className="account-studio">{studioName}</p>
          <button type="button" className="account-sign-out" onClick={onSignOut} disabled={signingOut}>
            {signingOut ? 'Signing out…' : 'Sign out'}
          </button>
          {signOutError ? (
            <p className="error-message" role="alert">
              {signOutError}
            </p>
          ) : null}
        </section>
      </header>

      <main id="studio-home">
        <p className="eyebrow">Your studio</p>
        <h1>Good to have you back.</h1>
        <p className="welcome-support">Your photography workspace, all in one place.</p>
        <p className="studio-name">{studioName}</p>

        <section className="summary-grid" aria-label="Studio summary">
          <article className="summary-card">
            <span className="summary-icon" aria-hidden="true">
              <Icon>
                <rect x="3.5" y="5" width="17" height="14" rx="2" />
                <circle cx="8.5" cy="9.5" r="1.4" />
                <path d="m20.5 15.5-4.2-4.2-8.3 8.2" />
              </Icon>
            </span>
            <p className={photos.counted ? 'summary-figure' : 'summary-figure is-status'}>{photos.figure}</p>
            <h2>Photos</h2>
            <p className="summary-detail">{photos.detail}</p>
          </article>
          <article className="summary-card">
            <span className="summary-icon" aria-hidden="true">
              <Icon>
                <path d="m12 3.5 8 4.5-8 4.5-8-4.5 8-4.5z" />
                <path d="m4 12.5 8 4.5 8-4.5" />
                <path d="m4 16.5 8 4.5 8-4.5" />
              </Icon>
            </span>
            <p className="summary-figure is-status">{collectionsFigure}</p>
            <h2>Collections</h2>
            <p className="summary-detail">{collectionsDetail}</p>
          </article>
        </section>

        <section id="photo-library" className="library-panel" aria-labelledby="photo-library-heading">
          <div className="library-head">
            <div className="library-title">
              <span className="summary-icon" aria-hidden="true">
                <Icon>
                  <rect x="3.5" y="4.5" width="17" height="15" rx="2" />
                  <path d="M7 4.5v-1h10v1" />
                  <path d="m8 15 2.4-2.4 2.1 2.1 2.2-2.6L18 15" />
                </Icon>
              </span>
              <div>
                <h2 id="photo-library-heading" className="library-heading">
                  Your photo library
                </h2>
                <p className="library-note">The studio&apos;s library will appear here.</p>
              </div>
            </div>
            <button type="button" className="upload-status" disabled>
              Upload
              <span className="coming-soon">Coming soon</span>
            </button>
          </div>
          <p className="library-note library-upload-note">{uploadUnavailableMessage}</p>
          <PhotoCatalog key={catalogKey} onModel={setCatalog} />
        </section>
      </main>
    </div>
  )
}

function Icon({ children }: { children: ReactNode }) {
  return (
    <svg className="icon" viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round">
      {children}
    </svg>
  )
}
