window.gunRpgConnection = {
    _subscriptions: new Map(),

    isOnline: function () {
        return navigator.onLine;
    },

    subscribe: function (subscriptionId, dotNetRef, methodName) {
        this.unsubscribe(subscriptionId);

        const notify = () => dotNetRef.invokeMethodAsync(methodName, navigator.onLine);
        window.addEventListener('online', notify);
        window.addEventListener('offline', notify);
        this._subscriptions.set(subscriptionId, { notify, dotNetRef });
    },

    unsubscribe: function (subscriptionId) {
        const existing = this._subscriptions.get(subscriptionId);
        if (!existing) {
            return;
        }

        window.removeEventListener('online', existing.notify);
        window.removeEventListener('offline', existing.notify);
        this._subscriptions.delete(subscriptionId);
    }
};
