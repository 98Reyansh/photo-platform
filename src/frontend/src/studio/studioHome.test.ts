import assert from 'node:assert/strict'
import test from 'node:test'
import { loadCurrentSession, signOutOfStudio } from '../auth/session.ts'
import { catalogAfterFirst } from '../catalog/photoCatalog.ts'
import type { PhotoMetadata } from '../api/photos.ts'
import {
  collectionsDetail,
  collectionsFigure,
  photoCountLabel,
  photoFigure,
  reliablePhotoCount,
  studioDestinations,
  uploadUnavailableMessage,
  viewAfterLookup,
  viewForSession,
} from './studioHome.ts'

const membership = {
  tenantId: '11111111-1111-7111-8111-111111111111',
  name: 'Lensora Studio',
  slug: 'lensora-studio',
  status: 'Active',
}

function photo(id: string): PhotoMetadata {
  return {
    id,
    tenantId: membership.tenantId,
    title: `Photo ${id}`,
    description: null,
    createdAtUtc: '2026-10-25T12:00:00.000Z',
    updatedAtUtc: '2026-10-25T12:00:00.000Z',
  }
}

test('the dashboard waits for a validated membership', () => {
  assert.equal(viewForSession('loading').kind, 'loading')
  assert.equal(viewForSession('unavailable').kind, 'unavailable')
  assert.equal(viewForSession('anonymous').kind, 'signed-out')
  assert.equal(viewForSession('signed-in').kind, 'signed-out')

  const dashboard = viewForSession('signed-in', membership)
  assert.equal(dashboard.kind, 'dashboard')
  if (dashboard.kind === 'dashboard') {
    assert.equal(dashboard.studioName, 'Lensora Studio')
  }
})

test('a restored session uses the membership name and a failed check stays signed out', async () => {
  const restored = await loadCurrentSession(async () => {
    return new Response(JSON.stringify(membership), { status: 200 })
  }, 'http://localhost:5080')
  const dashboard = viewAfterLookup(restored)
  assert.equal(dashboard.kind, 'dashboard')
  if (dashboard.kind === 'dashboard') {
    assert.equal(dashboard.studioName, 'Lensora Studio')
  }

  for (const status of [401, 403]) {
    const rejected = await loadCurrentSession(async () => new Response(null, { status }), 'http://localhost:5080')
    assert.equal(viewAfterLookup(rejected).kind, 'signed-out')
  }

  const unavailable = await loadCurrentSession(async () => {
    throw new TypeError('network down')
  }, 'http://localhost:5080')
  const failure = viewAfterLookup(unavailable)
  assert.equal(failure.kind, 'unavailable')
  if (failure.kind === 'unavailable') {
    assert.equal(failure.retryLabel, 'Retry session check')
  }
})

test('photo count is shown only for a complete library page', () => {
  assert.equal(reliablePhotoCount({ status: 'loading' }), null)
  assert.equal(reliablePhotoCount({ status: 'unauthorized' }), null)
  assert.equal(reliablePhotoCount({ status: 'forbidden' }), null)
  assert.equal(reliablePhotoCount({ status: 'error', message: 'down' }), null)
  assert.equal(reliablePhotoCount(catalogAfterFirst({ kind: 'page', page: { items: [], continuationCursor: null } })), 0)
  assert.equal(photoCountLabel(0), '0 photos')

  const complete = catalogAfterFirst({
    kind: 'page',
    page: { items: [photo('a'), photo('b')], continuationCursor: null },
  })
  assert.equal(reliablePhotoCount(complete), 2)
  assert.equal(photoCountLabel(2), '2 photos')
  assert.equal(photoCountLabel(1), '1 photo')

  const partial = catalogAfterFirst({
    kind: 'page',
    page: { items: [photo('a')], continuationCursor: 'next-page' },
  })
  assert.equal(reliablePhotoCount(partial), null)

  const emptyFigure = photoFigure(catalogAfterFirst({ kind: 'page', page: { items: [], continuationCursor: null } }))
  assert.equal(emptyFigure.figure, '0')
  assert.equal(emptyFigure.counted, true)

  const partialFigure = photoFigure(partial)
  assert.equal(partialFigure.counted, false)
  assert.equal(partialFigure.figure, 'Unavailable')

  const loadingFigure = photoFigure({ status: 'loading' })
  assert.equal(loadingFigure.counted, false)
  assert.equal(loadingFigure.figure, 'Checking')
})

test('collections do not present a fabricated count', () => {
  assert.equal(collectionsFigure, 'Coming soon')
  assert.equal(collectionsDetail, 'Collections are not available yet.')
  assert.equal(/\d/.test(collectionsFigure), false)
})

test('navigation keeps unfinished destinations non-interactive', () => {
  const home = studioDestinations.find((item) => item.id === 'home')
  const photos = studioDestinations.find((item) => item.id === 'photos')
  const collections = studioDestinations.find((item) => item.id === 'collections')
  const settings = studioDestinations.find((item) => item.id === 'settings')

  assert.equal(home && 'href' in home ? home.href : '', '#studio-home')
  assert.equal(home && 'current' in home ? home.current : false, true)
  assert.equal(photos && 'href' in photos ? photos.href : '', '#photo-library')
  assert.equal(collections && 'comingSoon' in collections ? collections.comingSoon : '', 'Coming soon')
  assert.equal(settings && 'comingSoon' in settings ? settings.comingSoon : '', 'Coming soon')
  assert.equal('href' in (collections ?? {}), false)
  assert.equal('href' in (settings ?? {}), false)
  assert.match(uploadUnavailableMessage, /upload workflow/)
})

test('sign-out returns the shell to the signed-out view', async () => {
  const result = await signOutOfStudio(async (input) => {
    const url = String(input)
    if (url.endsWith('/api/v1/auth/csrf')) {
      return new Response(JSON.stringify({ token: 'csrf-token' }), { status: 200 })
    }

    return new Response(null, { status: 204 })
  }, 'http://localhost:5080')

  assert.equal(result, 'signed-out')
  assert.equal(viewForSession('anonymous').kind, 'signed-out')
})
