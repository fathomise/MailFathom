// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import {
    readContactPhoto,
    readContactPhotoRequest,
    removeContactPhoto,
    removeContactPhotoRequest,
    replaceContactPhoto,
    replaceContactPhotoRequest,
} from './contactPhoto';
import { largestPortraitOctets } from './ownPortrait';
import type { ClientSession } from './session';
import type { ClientRequest } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const contactId = '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90';

const photoPath = `https://mail.example.invalid/api/client/contacts/${contactId}/photo`;

describe('readContactPhotoRequest', () => {
    it('asks for the photo under the contact it names, in the two kinds the surface serves', () => {
        const request = readContactPhotoRequest(session, contactId);

        expect(request.method).toBe('GET');
        expect(request.path).toBe(photoPath);
        expect(request.headers['Authorization']).toBe('Basic dGVzdA==');
        expect(request.headers['Accept']).toBe('image/jpeg, image/png');
        expect(request.longestAnswer).toBe(largestPortraitOctets);
    });

    it('escapes an identifier rather than letting it reach another route', () => {
        expect(readContactPhotoRequest(session, '../portrait').path).toBe(
            'https://mail.example.invalid/api/client/contacts/..%2Fportrait/photo',
        );
    });
});

describe('replaceContactPhotoRequest', () => {
    it('states the kind the chosen file was found to be and carries no body of its own', () => {
        const request = replaceContactPhotoRequest(session, contactId, 'image/png');

        expect(request.method).toBe('POST');
        expect(request.path).toBe(photoPath);
        expect(request.headers['Content-Type']).toBe('image/png');
        expect(request.body).toBeUndefined();
    });
});

describe('removeContactPhotoRequest', () => {
    it('removes the photo at the route it is read at', () => {
        const request = removeContactPhotoRequest(session, contactId);

        expect(request.method).toBe('DELETE');
        expect(request.path).toBe(photoPath);
    });
});

describe('the three reported operations', () => {
    it('hand the adapter exactly the request this module composes and answer what it answered', async () => {
        const asked: ClientRequest[] = [];
        const deliver = (request: ClientRequest): Promise<string> => {
            asked.push(request);

            return Promise.resolve('answered');
        };

        const answers = [
            await readContactPhoto(session, contactId, deliver, () => null),
            await replaceContactPhoto(session, contactId, 'image/jpeg', deliver, () => null),
            await removeContactPhoto(session, contactId, deliver, () => null),
        ];

        expect(asked).toEqual([
            readContactPhotoRequest(session, contactId),
            replaceContactPhotoRequest(session, contactId, 'image/jpeg'),
            removeContactPhotoRequest(session, contactId),
        ]);
        expect(answers).toEqual(['answered', 'answered', 'answered']);
    });
});
