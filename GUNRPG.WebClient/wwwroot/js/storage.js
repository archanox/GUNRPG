window.gunRpgStorage = {
    _dbName: 'GunRPG',
    _version: 3,
    _stores: {
        tokens: 'tokens',
        metadata: 'metadata',
        infiledOperators: 'infiledOperators',
        offlineMissionResults: 'offlineMissionResults',
        combatSessions: 'combatSessions'
    },

    _openDb: function () {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(this._dbName, this._version);
            request.onupgradeneeded = () => {
                const db = request.result;

                if (!db.objectStoreNames.contains(this._stores.tokens)) {
                    db.createObjectStore(this._stores.tokens);
                }

                if (!db.objectStoreNames.contains(this._stores.metadata)) {
                    db.createObjectStore(this._stores.metadata);
                }

                if (!db.objectStoreNames.contains(this._stores.infiledOperators)) {
                    const store = db.createObjectStore(this._stores.infiledOperators, { keyPath: 'id' });
                    store.createIndex('isActive', 'isActive', { unique: false });
                }

                if (!db.objectStoreNames.contains(this._stores.offlineMissionResults)) {
                    const store = db.createObjectStore(this._stores.offlineMissionResults, { keyPath: 'id' });
                    store.createIndex('operatorId', 'operatorId', { unique: false });
                    store.createIndex('synced', 'synced', { unique: false });
                    store.createIndex('operatorId_sequenceNumber', ['operatorId', 'sequenceNumber'], { unique: true });
                }

                if (!db.objectStoreNames.contains(this._stores.combatSessions)) {
                    const store = db.createObjectStore(this._stores.combatSessions, { keyPath: 'id' });
                    store.createIndex('operatorId', 'operatorId', { unique: false });
                }
            };
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
    },

    _runTransaction: async function (storeName, mode, action) {
        const db = await this._openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(storeName, mode);
            const store = tx.objectStore(storeName);
            const result = action(store);
            tx.oncomplete = () => resolve(result);
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error || new Error('IndexedDB transaction aborted.'));
        });
    },

    putValue: async function (storeName, key, value) {
        return this._runTransaction(storeName, 'readwrite', store => store.put(value, key));
    },

    getValue: async function (storeName, key) {
        const db = await this._openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(storeName, 'readonly');
            const request = tx.objectStore(storeName).get(key);
            request.onsuccess = () => resolve(request.result ?? null);
            request.onerror = () => reject(request.error);
        });
    },

    deleteValue: async function (storeName, key) {
        return this._runTransaction(storeName, 'readwrite', store => store.delete(key));
    },

    getAllValues: async function (storeName) {
        const db = await this._openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(storeName, 'readonly');
            const request = tx.objectStore(storeName).getAll();
            request.onsuccess = () => resolve(request.result || []);
            request.onerror = () => reject(request.error);
        });
    },

    saveInfiledOperator: async function (record) {
        const db = await this._openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(this._stores.infiledOperators, 'readwrite');
            const store = tx.objectStore(this._stores.infiledOperators);
            const getAllRequest = store.getAll();
            getAllRequest.onsuccess = () => {
                const existing = getAllRequest.result || [];
                for (const item of existing) {
                    if (item.isActive) {
                        item.isActive = false;
                        store.put(item);
                    }
                }

                store.put(record);
            };
            getAllRequest.onerror = () => reject(getAllRequest.error);
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error || new Error('IndexedDB transaction aborted.'));
        });
    },

    getInfiledOperator: async function (id) {
        return this.getValue(this._stores.infiledOperators, id);
    },

    getActiveInfiledOperator: async function () {
        const all = await this.getAllValues(this._stores.infiledOperators);
        return all.find(x => x.isActive) || null;
    },

    hasActiveInfiledOperator: async function () {
        const active = await this.getActiveInfiledOperator();
        return active !== null;
    },

    updateInfiledOperator: async function (record) {
        return this._runTransaction(this._stores.infiledOperators, 'readwrite', store => store.put(record));
    },

    removeInfiledOperator: async function (id) {
        return this.deleteValue(this._stores.infiledOperators, id);
    },

    saveOfflineMissionResult: async function (record) {
        return this._runTransaction(this._stores.offlineMissionResults, 'readwrite', store => store.put(record));
    },

    getOfflineMissionResult: async function (id) {
        return this.getValue(this._stores.offlineMissionResults, id);
    },

    getAllOfflineMissionResults: async function () {
        return this.getAllValues(this._stores.offlineMissionResults);
    },

    saveCombatSession: async function (snapshot) {
        return this._runTransaction(this._stores.combatSessions, 'readwrite', store => store.put(snapshot));
    },

    loadCombatSession: async function (id) {
        return this.getValue(this._stores.combatSessions, id);
    },

    deleteCombatSession: async function (id) {
        return this.deleteValue(this._stores.combatSessions, id);
    },

    getAllCombatSessions: async function () {
        return this.getAllValues(this._stores.combatSessions);
    }
};

window.tokenStorage = {
    _tokensStore: 'tokens',
    _refreshKey: 'refreshToken',
    _accessKey: 'accessToken',

    storeRefreshToken: async function (token) {
        await window.gunRpgStorage.putValue(this._tokensStore, this._refreshKey, token);
    },

    getRefreshToken: async function () {
        return await window.gunRpgStorage.getValue(this._tokensStore, this._refreshKey);
    },

    removeRefreshToken: async function () {
        await window.gunRpgStorage.deleteValue(this._tokensStore, this._refreshKey);
    },

    storeAccessToken: async function (token) {
        await window.gunRpgStorage.putValue(this._tokensStore, this._accessKey, token);
    },

    getAccessToken: async function () {
        return await window.gunRpgStorage.getValue(this._tokensStore, this._accessKey);
    },

    removeAccessToken: async function () {
        await window.gunRpgStorage.deleteValue(this._tokensStore, this._accessKey);
    },

    clearTokens: async function () {
        await this.removeAccessToken();
        await this.removeRefreshToken();
    }
};
