window.tokenStorage = {
    _dbName: 'GunRPG',
    _storeName: 'tokens',
    _key: 'refreshToken',

    _openDb: function () {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(this._dbName, 1);
            request.onupgradeneeded = () => {
                const db = request.result;
                if (!db.objectStoreNames.contains(this._storeName)) {
                    db.createObjectStore(this._storeName);
                }
            };
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
    },

    storeRefreshToken: async function (token) {
        const db = await this._openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(this._storeName, 'readwrite');
            tx.objectStore(this._storeName).put(token, this._key);
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
        });
    },

    getRefreshToken: async function () {
        const db = await this._openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(this._storeName, 'readonly');
            const request = tx.objectStore(this._storeName).get(this._key);
            request.onsuccess = () => resolve(request.result || null);
            request.onerror = () => reject(request.error);
        });
    },

    removeRefreshToken: async function () {
        const db = await this._openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(this._storeName, 'readwrite');
            tx.objectStore(this._storeName).delete(this._key);
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
        });
    }
};
