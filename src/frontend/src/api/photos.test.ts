import assert from 'node:assert/strict'
import test from 'node:test'
import {
  fetchPhotoPage,
  photoPageLimit,
  photoUnexpectedMessage,
  photoUnreachableMessage,
  type PhotoMetadata,
} from './photos.ts'
import {
  appendPhotos,
  catalogAfterFirst,
  catalogAfterMore,
  catalogRetryLabel,
  catalogSessionAfterSignIn,
  emptyCatalogMessage,
  retryCursor,
  sessionExpiredMessage,
  showLoadMore,
} from '../catalog/photoCatalog.ts'

const createdAtUtc = '2026-10-25T12:00:00.0000000Z'
const updatedAtUtc = '2026-10-25T12:05:00Z'

function photo(id: string, title = `Photo ${id}`): PhotoMetadata {
  return {
    id,
    tenantId: '11111111-1111-7111-8111-111111111111',
    title,
    description: null,
    createdAtUtc,
    updatedAtUtc,
  }
}

function pageResponse(items: PhotoMetadata[], continuationCursor: string | null, status = 200): Response {
  return new Response(JSON.stringify({ items, continuationCursor }), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

test('the first page requests limit 20 with credentials and no tenant or csrf header', async () => {
  const storage = throwingStorage()
  Object.defineProperty(globalThis, 'localStorage', { value: storage, configurable: true })
  Object.defineProperty(globalThis, 'sessionStorage', { value: storage, configurable: true })
  let requestUrl = ''
  let credentials: RequestCredentials | undefined
  let headers: Headers | undefined
  const fetchImpl: typeof fetch = async (input, init) => {
    requestUrl = String(input)
    credentials = init?.credentials
    headers = new Headers(init?.headers)
    assert.equal(init?.body, undefined)
    return pageResponse([photo('photo-1', 'Harbor')], null)
  }

  const outcome = await fetchPhotoPage(fetchImpl, 'http://localhost:5080/', null)

  const url = new URL(requestUrl)
  assert.equal(url.origin + url.pathname, 'http://localhost:5080/api/v1/photos')
  assert.equal(url.searchParams.get('limit'), String(photoPageLimit))
  assert.equal(url.searchParams.has('cursor'), false)
  assert.equal(url.searchParams.has('tenantId'), false)
  assert.equal(credentials, 'include')
  assert.equal(headers?.get('X-CSRF-TOKEN'), null)
  assert.equal(headers?.get('X-Tenant-Id'), null)
  assert.equal(outcome.kind, 'page')
  if (outcome.kind === 'page') {
    assert.equal(outcome.page.items[0]?.description, null)
    assert.equal(outcome.page.continuationCursor, null)
    assert.equal(catalogAfterFirst(outcome).status, 'ready')
  }
})

test('the next page sends the exact cursor from the previous page', async () => {
  const cursor = 'v1.abc+def/ghi='
  let requestUrl = ''
  const fetchImpl: typeof fetch = async (input) => {
    requestUrl = String(input)
    return pageResponse([], null)
  }

  await fetchPhotoPage(fetchImpl, 'http://localhost:5080', cursor)

  assert.equal(new URL(requestUrl).searchParams.get('cursor'), cursor)
})

test('http 401 and http 403 are not empty pages', async () => {
  const emptyBody = JSON.stringify({ items: [], continuationCursor: null })
  const unauthorized = await fetchPhotoPage(async () => new Response(emptyBody, { status: 401 }), 'http://localhost:5080', null)
  const forbidden = await fetchPhotoPage(
    async () => new Response(JSON.stringify({ title: 'Studio access was not found.', detail: 'The current user does not have a studio membership.' }), { status: 403 }),
    'http://localhost:5080',
    null,
  )

  assert.equal(unauthorized.kind, 'unauthorized')
  assert.equal(forbidden.kind, 'forbidden')
  assert.equal(catalogAfterFirst(unauthorized).status, 'unauthorized')
  assert.equal(catalogAfterFirst(forbidden).status, 'forbidden')
  assert.notEqual(emptyCatalogMessage, sessionExpiredMessage)
})

test('a thrown fetch is a recoverable network error', async () => {
  const outcome = await fetchPhotoPage(async () => {
    throw new TypeError('network down')
  }, 'http://localhost:5080', null)

  assert.deepEqual(outcome, { kind: 'recoverable', message: photoUnreachableMessage })
  const model = catalogAfterFirst(outcome)
  assert.equal(model.status, 'error')
  if (model.status === 'error') {
    assert.equal(model.message, photoUnreachableMessage)
  }
})

test('invalid json and malformed pages are rejected', async () => {
  const invalidJson = await fetchPhotoPage(async () => new Response('not-json', { status: 200 }), 'http://localhost:5080', null)
  const malformed = await fetchPhotoPage(
    async () => new Response(JSON.stringify({ items: [{ id: 'only-id' }] }), { status: 200 }),
    'http://localhost:5080',
    null,
  )
  const serverError = await fetchPhotoPage(async () => new Response('nope', { status: 500 }), 'http://localhost:5080', null)

  assert.equal(invalidJson.kind, 'recoverable')
  assert.equal(malformed.kind, 'recoverable')
  if (invalidJson.kind === 'recoverable') {
    assert.equal(invalidJson.message, photoUnexpectedMessage)
  }
  assert.equal(serverError.kind, 'recoverable')
  if (serverError.kind === 'recoverable') {
    assert.equal(serverError.message, 'The photo catalog returned an error (HTTP 500).')
  }
})

test('an empty page is distinct from a populated page and a failed next page keeps the current records', () => {
  const empty = catalogAfterFirst({ kind: 'page', page: { items: [], continuationCursor: null } })
  assert.equal(empty.status, 'empty')
  assert.equal(showLoadMore(empty), false)

  const first = catalogAfterFirst({
    kind: 'page',
    page: { items: [photo('photo-1'), photo('photo-2')], continuationCursor: 'cursor-2' },
  })
  assert.equal(first.status, 'ready')
  assert.equal(showLoadMore(first), true)

  const appended = catalogAfterMore(first as Extract<typeof first, { status: 'ready' }>, {
    kind: 'page',
    page: { items: [photo('photo-2'), photo('photo-3')], continuationCursor: null },
  })
  assert.equal(appended.status, 'ready')
  if (appended.status === 'ready') {
    assert.deepEqual(appended.items.map((item) => item.id), ['photo-1', 'photo-2', 'photo-3'])
    assert.equal(appended.cursor, null)
    assert.equal(showLoadMore(appended), false)
  }

  const failed = catalogAfterMore(first as Extract<typeof first, { status: 'ready' }>, {
    kind: 'recoverable',
    message: photoUnreachableMessage,
  })
  assert.equal(failed.status, 'ready')
  if (failed.status === 'ready') {
    assert.deepEqual(failed.items.map((item) => item.id), ['photo-1', 'photo-2'])
    assert.equal(failed.cursor, 'cursor-2')
    assert.equal(failed.notice?.message, photoUnreachableMessage)
    assert.equal(retryCursor(failed), 'cursor-2')
    assert.equal(showLoadMore(failed), false)
  }

  const denied = catalogAfterMore(first as Extract<typeof first, { status: 'ready' }>, { kind: 'unauthorized' })
  assert.equal(denied.status, 'ready')
  if (denied.status === 'ready') {
    assert.equal(denied.items.length, 2)
    assert.equal(denied.notice?.kind, 'unauthorized')
  }
})

test('a later sign-in session replaces the previous catalog page', () => {
  const firstSession = catalogSessionAfterSignIn(0)
  const secondSession = catalogSessionAfterSignIn(firstSession)
  assert.equal(firstSession, 1)
  assert.equal(secondSession, 2)

  const previous = catalogAfterFirst({
    kind: 'page',
    page: { items: [photo('studio-a')], continuationCursor: 'cursor-a' },
  })
  assert.equal(previous.status, 'ready')
  if (previous.status !== 'ready') {
    return
  }

  const carried = catalogAfterMore(previous, {
    kind: 'page',
    page: { items: [photo('studio-b')], continuationCursor: null },
  })
  const startedOver = catalogAfterFirst({
    kind: 'page',
    page: { items: [photo('studio-b')], continuationCursor: null },
  })

  assert.equal(carried.status, 'ready')
  assert.equal(startedOver.status, 'ready')
  if (carried.status === 'ready' && startedOver.status === 'ready') {
    assert.deepEqual(
      carried.items.map((item) => item.id),
      ['studio-a', 'studio-b'],
    )
    assert.equal(previous.cursor, 'cursor-a')
    assert.deepEqual(
      startedOver.items.map((item) => item.id),
      ['studio-b'],
    )
    assert.equal(startedOver.cursor, null)
  }
})

test('catalog retry labels name the failed operation', () => {
  const firstFailure = catalogAfterFirst({ kind: 'recoverable', message: photoUnreachableMessage })
  assert.equal(retryCursor(firstFailure), null)
  assert.equal(catalogRetryLabel(null), 'Retry photo catalog')

  const ready = catalogAfterFirst({
    kind: 'page',
    page: { items: [photo('photo-1')], continuationCursor: 'cursor-2' },
  })
  assert.equal(ready.status, 'ready')
  if (ready.status !== 'ready') {
    return
  }

  const moreFailure = catalogAfterMore(ready, {
    kind: 'recoverable',
    message: photoUnreachableMessage,
  })
  assert.equal(retryCursor(moreFailure), 'cursor-2')
  assert.equal(catalogRetryLabel('cursor-2'), 'Retry loading more photos')
})

test('append keeps server order and drops a repeated id', () => {
  const existing = [photo('b'), photo('a')]
  const next = appendPhotos(existing, [photo('a', 'replacement'), photo('c')])
  assert.deepEqual(next.map((item) => item.title), ['Photo b', 'Photo a', 'Photo c'])
})

function throwingStorage() {
  return {
    setItem() {
      throw new Error('browser storage was used')
    },
    getItem() {
      throw new Error('browser storage was used')
    },
  }
}
