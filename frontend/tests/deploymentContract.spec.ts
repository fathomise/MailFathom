// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect, test } from '@playwright/test';

import { bodyOf, ContractDeployment, contractCases, departuresFrom, type ContractTarget } from './deploymentContract';
import { FakeDeployment } from './fakeDeployment';

// The contract set asked of the fake every journey signs in to, in process and with no page, on every pull request that
// reaches the client. `end-to-end/deploymentContract.spec.ts` asks the same cases of a real deployment; this half is
// what keeps a change to the fake from leaving the reading both share.

/** The fake as a target: every request answered in process, exactly as the browser's routing would hand it over. */
function inProcess(deployment: FakeDeployment): ContractTarget {
    return {
        ask: (method, route, body) => {
            const answer = deployment.answer({
                method,
                url: `http://deployment.invalid/api/client${route}`,
                body: body === undefined ? null : JSON.stringify(body),
                contentType: 'application/json',
            });

            return Promise.resolve({ status: answer.status, body: bodyOf(answer.body) });
        },
    };
}

for (const contractCase of contractCases) {
    test(contractCase.title, async () => {
        const deployment = new FakeDeployment('0.0.0');

        // The credential the end-to-end run provisions holds the seeded User role, which reads, writes, and sends mail;
        // the fake's frame-sized credential is widened to the same reach so the two are asked as the same person.
        deployment.grant('mailfathom.mail.read', 'mailfathom.mail.send');

        await contractCase.run(new ContractDeployment(inProcess(deployment)));
    });
}

// The check that holds every answer to the recorded shape is itself held, so a reading of the schema that passed
// everything could not pass for a contract that holds.
test('refuses an answer naming a field the service does not, or typing one differently, and takes one stating less', () => {
    const task = { $ref: '#/components/schemas/ClientTaskResponse' };
    const answered = {
        id: 'task-1',
        title: 'A task',
        dueOn: null,
        reminders: [],
        remindsAt: [],
        origin: 'Asserted',
        completed: false,
        sourceMessageId: null,
    };

    expect(departuresFrom(task, answered)).toStrictEqual([]);
    expect(departuresFrom(task, { ...answered, completed: 'no' })).toStrictEqual([
        '$.completed is string where the service answers boolean',
    ]);

    expect(departuresFrom(task, { ...answered, isCompleted: false })).toStrictEqual([
        '$.isCompleted is a field the service does not answer',
    ]);
    expect(departuresFrom(task, { id: 'task-1', title: 'A task' })).toStrictEqual([]);
});
