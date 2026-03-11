const CACHE_NAME = 'gunrpg-shell-v2';
const APP_ROOT = new URL('./', self.location.href).pathname;
const INDEX_URL = new URL('index.html', self.location.href).pathname;
const MANIFEST_URL = new URL('manifest.webmanifest', self.location.href).pathname;
const FAVICON_URL = new URL('favicon.png', self.location.href).pathname;
const ICON_URL = new URL('icon-192.png', self.location.href).pathname;
const APP_CSS_URL = new URL('css/app.css', self.location.href).pathname;
const STORAGE_JS_URL = new URL('js/storage.js', self.location.href).pathname;
const CONNECTION_JS_URL = new URL('js/connection.js', self.location.href).pathname;
const WEBAUTHN_JS_URL = new URL('js/webauthn.js', self.location.href).pathname;
const SSE_JS_URL = new URL('js/sse.js', self.location.href).pathname;
const FLUENT_REBOOT_CSS_URL = new URL('_content/Microsoft.FluentUI.AspNetCore.Components/css/reboot.css', self.location.href).pathname;
const CORE_ASSETS = [
    APP_ROOT,
    INDEX_URL,
    MANIFEST_URL,
    FAVICON_URL,
    ICON_URL,
    APP_CSS_URL,
    STORAGE_JS_URL,
    CONNECTION_JS_URL,
    WEBAUTHN_JS_URL,
    SSE_JS_URL,
    FLUENT_REBOOT_CSS_URL
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
                    cache.put(INDEX_URL, networkResponse.clone());
                }
                return networkResponse;
            } catch {
                const cached = await caches.match(INDEX_URL);
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
