// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { request, test, type APIRequestContext } from '@playwright/test';

import { bodyOf, ContractDeployment, contractCases } from '../deploymentContract';
import { credential, required } from './deployment';

// The contract set asked of the deployment this run stood up, over HTTP beneath `/api/client` with the credential the
// run provisioned, and with no browser. `../deploymentContract.spec.ts` asks the same cases of the fake every journey
// signs in to; a case that passes there and fails here is the fake reading the service wrongly, or the service being
// wrong, and `frontend/tests/AGENTS.md` § *The contract set* says which of the two is corrected.
//
// Every case puts back what it changed, because the specs after this file meet the same mailbox.

let api: APIRequestContext;

test.beforeAll(async () => {
    api = await request.newContext({
        baseURL: required('MAILFATHOM_CLIENT_ORIGIN'),
        extraHTTPHeaders: {
            Authorization: `Basic ${Buffer.from(`${credential.userName}:${credential.password}`).toString('base64')}`,
        },
    });
});

test.afterAll(async () => {
    await api.dispose();
});

for (const contractCase of contractCases) {
    test(contractCase.title, async () => {
        // Each wait a case holds is bounded and named in `../deploymentContract.ts`; this sits above the longest sum of
        // them, the managed-folder case's five two-minute reads, so a slow run fails on the wait that ran out.
        test.setTimeout(12 * 60_000);
        await contractCase.run(
            new ContractDeployment({
                ask: async (method, route, body) => {
                    const answer = await api.fetch(`/api/client${route}`, {
                        method,
                        ...(body === undefined ? {} : { data: body }),
                    });
                    return { status: answer.status(), body: bodyOf(await answer.text()) };
                },
            }),
        );
    });
}
