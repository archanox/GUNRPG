window.webauthn = {
    startRegistration: async function (optionsJson) {
        const options = JSON.parse(optionsJson);

        if (options.challenge) {
            options.challenge = base64UrlToBuffer(options.challenge);
        }
        if (options.user && options.user.id) {
            options.user.id = base64UrlToBuffer(options.user.id);
        }
        if (options.excludeCredentials) {
            options.excludeCredentials = options.excludeCredentials.map(c => ({
                ...c,
                id: base64UrlToBuffer(c.id)
            }));
        }

        const credential = await navigator.credentials.create({ publicKey: options });

        return JSON.stringify({
            id: credential.id,
            rawId: bufferToBase64Url(credential.rawId),
            type: credential.type,
            response: {
                attestationObject: bufferToBase64Url(credential.response.attestationObject),
                clientDataJSON: bufferToBase64Url(credential.response.clientDataJSON)
            }
        });
    },

    startLogin: async function (optionsJson) {
        const options = JSON.parse(optionsJson);

        if (options.challenge) {
            options.challenge = base64UrlToBuffer(options.challenge);
        }
        if (options.allowCredentials) {
            options.allowCredentials = options.allowCredentials.map(c => ({
                ...c,
                id: base64UrlToBuffer(c.id)
            }));
        }

        const credential = await navigator.credentials.get({ publicKey: options });

        return JSON.stringify({
            id: credential.id,
            rawId: bufferToBase64Url(credential.rawId),
            type: credential.type,
            response: {
                authenticatorData: bufferToBase64Url(credential.response.authenticatorData),
                clientDataJSON: bufferToBase64Url(credential.response.clientDataJSON),
                signature: bufferToBase64Url(credential.response.signature),
                userHandle: credential.response.userHandle
                    ? bufferToBase64Url(credential.response.userHandle)
                    : null
            }
        });
    }
};

function base64UrlToBuffer(base64url) {
    const base64 = base64url.replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(base64.length + (4 - base64.length % 4) % 4, '=');
    const binary = atob(padded);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
    }
    return bytes.buffer;
}

function bufferToBase64Url(buffer) {
    const bytes = new Uint8Array(buffer);
    let binary = '';
    for (let i = 0; i < bytes.byteLength; i++) {
        binary += String.fromCharCode(bytes[i]);
    }
    return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}
