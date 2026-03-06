window.sseHelper = {
    _connections: {},

    connect: function (url, dotNetRef, callbackName) {
        if (this._connections[url]) {
            this._connections[url].close();
            delete this._connections[url];
        }

        const es = new EventSource(url);
        es.onmessage = function () {
            dotNetRef.invokeMethodAsync(callbackName);
        };
        this._connections[url] = es;
    },

    disconnect: function (url) {
        if (this._connections[url]) {
            this._connections[url].close();
            delete this._connections[url];
        }
    }
};
