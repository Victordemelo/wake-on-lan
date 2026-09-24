import { useSyncExternalStore } from 'react'

// Just two public paths ("/" and "/login"), so a tiny history-based router is enough.
const listeners = new Set<() => void>()

function subscribe(listener: () => void) {
  listeners.add(listener)
  window.addEventListener('popstate', listener)
  return () => {
    listeners.delete(listener)
    window.removeEventListener('popstate', listener)
  }
}

export function navigate(path: string, { replace = false } = {}) {
  if (window.location.pathname === path) {
    if (!replace) window.scrollTo(0, 0)
    return
  }
  if (replace) window.history.replaceState(null, '', path)
  else window.history.pushState(null, '', path)
  listeners.forEach((listener) => listener())
  if (!replace) window.scrollTo(0, 0)
}

export const usePath = () => useSyncExternalStore(subscribe, () => window.location.pathname)
