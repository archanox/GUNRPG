window.sseHelper = {
    _connections: {},
    _retryDelayMs: 3000,
    _reconnectDelayMs: 1000,

    connect: function (url, accessToken, dotNetRef, callbackName) {
        if (this._connections[url]) {
            this.disconnect(url);
        }

        const controller = new AbortController();
        const connection = { controller };
        this._connections[url] = connection;

        const delay = (ms) => new Promise(resolve => setTimeout(resolve, ms));

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
            while (!controller.signal.aborted) {
                try {
                    const headers = { "Accept": "text/event-stream" };
                    if (accessToken) {
                        headers["Authorization"] = `Bearer ${accessToken}`;
                    }

                    const response = await fetch(url, {
                        headers,
                        cache: "no-store",
                        signal: controller.signal
                    });

                    if (response.status === 401 || response.status === 403) {
                        break;
                    }

                    if (!response.ok || !response.body) {
                        await delay(this._retryDelayMs);
                        continue;
                    }

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
