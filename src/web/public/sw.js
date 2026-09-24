const CACHE = 'remote-wake-v3'
const SHELL = ['/', '/manifest.webmanifest', '/brand/remote-wake-mark.svg', '/brand/icon-192.png']

self.addEventListener('install', (event) => {
  event.waitUntil(caches.open(CACHE).then((cache) => cache.addAll(SHELL)).then(() => self.skipWaiting()))
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((key) => key !== CACHE).map((key) => caches.delete(key))))
      .then(() => self.clients.claim()),
  )
})

// Network first: online the panel is always fresh; offline it still opens with
// the last cached shell. API calls are never cached.
self.addEventListener('fetch', (event) => {
  const { request } = event
  const url = new URL(request.url)
  if (request.method !== 'GET' || url.origin !== self.location.origin
    || url.pathname.startsWith('/api/') || url.pathname === '/health') return

  const key = request.mode === 'navigate' ? '/' : request
  event.respondWith(
    fetch(request)
      .then((response) => {
        if (response.ok && (request.mode === 'navigate' || url.pathname.startsWith('/assets/'))) {
          const copy = response.clone()
          event.waitUntil(caches.open(CACHE).then((cache) => cache.put(key, copy)))
        }
        return response
      })
      .catch(async () => (await caches.match(key)) ?? Response.error()),
  )
})
