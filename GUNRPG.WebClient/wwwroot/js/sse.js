window.sseHelper = {
    _connections: {},
    _retryDelayMs: 3000,
    _reconnectDelayMs: 1000,
    _maxRetryDelayMs: 30000,

    connect: function (url, dotNetRef, callbackName, tokenCallbackName) {
        if (this._connections[url]) {
            this.disconnect(url);
        }

        const controller = new AbortController();
        const connection = { controller };
        this._connections[url] = connection;

        const delay = (ms) => new Promise(resolve => setTimeout(resolve, ms));
        const nextRetryDelay = (ms) => Math.min(ms * 2, this._maxRetryDelayMs);
        const getAccessToken = async (forceRefresh) => {
            try {
                return await dotNetRef.invokeMethodAsync(tokenCallbackName, forceRefresh);
            } catch {
                return null;
            }
        };

        const processChunk = async function (chunk, state) {
            state.buffer += chunk;

            while (true) {
                const separatorMatch = state.buffer.match(/\r?\n\r?\n/);
                if (!separatorMatch || separatorMatch.index === undefined) {
                    return;
                }

                const separatorIndex = separatorMatch.index;
                const eventBlock = state.buffer.slice(0, separatorIndex);
                state.buffer = state.buffer.slice(separatorIndex + separatorMatch[0].length);

                const hasData = eventBlock
                    .split(/\r?\n/)
                    .some(line => line.startsWith("data:"));

                if (hasData) {
                    await dotNetRef.invokeMethodAsync(callbackName);
                }
            }
        };

        const pump = async () => {
            let retryDelayMs = this._retryDelayMs;

            while (!controller.signal.aborted) {
                try {
                    const accessToken = await getAccessToken(false);
                    if (!accessToken) {
                        break;
                    }

                    const headers = { "Accept": "text/event-stream" };
                    headers["Authorization"] = `Bearer ${accessToken}`;

                    const response = await fetch(url, {
                        headers,
                        cache: "no-store",
                        signal: controller.signal
                    });

                    if (response.status === 401 || response.status === 403) {
                        const refreshedToken = await getAccessToken(true);
                        if (!refreshedToken) {
                            break;
                        }

                        await delay(this._reconnectDelayMs);
                        continue;
                    }

                    if (!response.ok || !response.body) {
                        await delay(retryDelayMs);
                        retryDelayMs = nextRetryDelay(retryDelayMs);
                        continue;
                    }

                    retryDelayMs = this._retryDelayMs;

                    const reader = response.body.getReader();
                    const decoder = new TextDecoder();
                    const state = { buffer: "" };

                    while (!controller.signal.aborted) {
                        const result = await reader.read();
                        if (result.done) {
                            break;
                        }

                        await processChunk(decoder.decode(result.value, { stream: true }), state);
                    }
                } catch (error) {
                    if (controller.signal.aborted) {
                        break;
                    }

                    await delay(retryDelayMs);
                    retryDelayMs = nextRetryDelay(retryDelayMs);
                    continue;
                }

                if (!controller.signal.aborted) {
                    await delay(this._reconnectDelayMs);
                }
            }

            if (window.sseHelper && window.sseHelper._connections[url] === connection) {
                delete window.sseHelper._connections[url];
            }
        };

        pump();
    },

    disconnect: function (url) {
        if (this._connections[url]) {
            this._connections[url].controller.abort();
            delete this._connections[url];
        }
    }
};
