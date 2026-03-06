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
        es.onerror = function () {
            // If the EventSource has reached a closed state, clean up the connection.
            if (es.readyState === EventSource.CLOSED) {
                es.close();
                if (window.sseHelper && window.sseHelper._connections[url] === es) {
                    delete window.sseHelper._connections[url];
                }
            }
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
