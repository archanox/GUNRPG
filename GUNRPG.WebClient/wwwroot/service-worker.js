const CACHE_NAME = 'gunrpg-shell-v1';
const CORE_ASSETS = [
    '/',
    '/index.html',
    '/manifest.webmanifest',
    '/favicon.png',
    '/icon-192.png',
    '/css/app.css',
    '/js/storage.js',
    '/js/connection.js',
    '/js/webauthn.js',
    '/js/sse.js',
    '/_content/Microsoft.FluentUI.AspNetCore.Components/css/reboot.css'
];

self.addEventListener('install', event => {
    event.waitUntil(caches.open(CACHE_NAME).then(cache => cache.addAll(CORE_ASSETS)));
    self.skipWaiting();
});

self.addEventListener('activate', event => {
    event.waitUntil((async () => {
        const keys = await caches.keys();
        await Promise.all(keys.filter(key => key !== CACHE_NAME).map(key => caches.delete(key)));
        await self.clients.claim();
    })());
});

self.addEventListener('fetch', event => {
    const { request } = event;

    if (request.method !== 'GET') {
        return;
    }

    if (request.mode === 'navigate') {
        event.respondWith((async () => {
            try {
                const networkResponse = await fetch(request);
                if (networkResponse.ok) {
                    const cache = await caches.open(CACHE_NAME);
                    cache.put('/index.html', networkResponse.clone());
                }
                return networkResponse;
            } catch {
                const cached = await caches.match('/index.html');
                return cached || Response.error();
            }
        })());
        return;
    }

    event.respondWith((async () => {
        const cached = await caches.match(request);
        if (cached) {
            return cached;
        }

        try {
            const networkResponse = await fetch(request);
            if (networkResponse.ok && request.url.startsWith(self.location.origin)) {
                const cache = await caches.open(CACHE_NAME);
                cache.put(request, networkResponse.clone());
            }
            return networkResponse;
        } catch {
            return Response.error();
        }
    })());
});
