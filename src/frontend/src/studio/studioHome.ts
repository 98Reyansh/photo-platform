import type { CurrentMembership, SessionLookup } from '../auth/session'
import type { CatalogModel } from '../catalog/photoCatalog'

export const uploadUnavailableMessage =
  'Adding photos will be available when the upload workflow is implemented.'

export const sessionCheckMessage = 'Checking the studio session…'

export const sessionUnavailableMessage =
  'The studio session could not be checked. Confirm the API is running and try again.'

export const sessionRetryLabel = 'Retry session check'

export type StudioDestination =
  | { id: 'home'; label: 'Home'; href: '#studio-home'; current: true }
  | { id: 'photos'; label: 'Photos'; href: '#photo-library'; current: false }
  | { id: 'collections' | 'settings'; label: string; comingSoon: 'Coming soon' }

export const studioDestinations: readonly StudioDestination[] = [
  { id: 'home', label: 'Home', href: '#studio-home', current: true },
  { id: 'photos', label: 'Photos', href: '#photo-library', current: false },
  { id: 'collections', label: 'Collections', comingSoon: 'Coming soon' },
  { id: 'settings', label: 'Studio settings', comingSoon: 'Coming soon' },
]

export type StudioView =
  | { kind: 'loading'; message: string }
  | { kind: 'unavailable'; message: string; retryLabel: string }
  | { kind: 'signed-out' }
  | { kind: 'dashboard'; studioName: string; destinations: readonly StudioDestination[] }

export function viewForSession(
  phase: 'loading' | 'unavailable' | 'anonymous' | 'signed-in',
  membership?: CurrentMembership,
): StudioView {
  if (phase === 'loading') {
    return { kind: 'loading', message: sessionCheckMessage }
  }

  if (phase === 'unavailable') {
    return { kind: 'unavailable', message: sessionUnavailableMessage, retryLabel: sessionRetryLabel }
  }

  if (phase !== 'signed-in' || !membership) {
    return { kind: 'signed-out' }
  }

  return {
    kind: 'dashboard',
    studioName: membership.name,
    destinations: studioDestinations,
  }
}

export function viewAfterLookup(lookup: SessionLookup): StudioView {
  if (lookup.status === 'authenticated') {
    return viewForSession('signed-in', lookup.membership)
  }

  return lookup.status === 'anonymous' ? viewForSession('anonymous') : viewForSession('unavailable')
}

export function reliablePhotoCount(model: CatalogModel): number | null {
  if (model.status === 'empty') {
    return 0
  }

  if (model.status === 'ready' && model.cursor === null) {
    return model.items.length
  }

  return null
}

export function photoCountLabel(count: number): string {
  return count === 1 ? '1 photo' : `${count} photos`
}

export const collectionsFigure = 'Coming soon'

export const collectionsDetail = 'Collections are not available yet.'

export function photoFigure(model: CatalogModel): { figure: string; detail: string; counted: boolean } {
  const count = reliablePhotoCount(model)
  if (count === null) {
    if (model.status === 'loading') {
      return { figure: 'Checking', detail: 'Checking the photo library.', counted: false }
    }

    return {
      figure: 'Unavailable',
      detail: 'A photo count appears when the full page has loaded.',
      counted: false,
    }
  }

  return { figure: String(count), detail: photoCountLabel(count), counted: true }
}
