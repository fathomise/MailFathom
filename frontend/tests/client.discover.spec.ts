// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect } from '@playwright/test';

import {
    holdTheClock,
    messageRegion,
    openSignedIn,
    openTheFirstMessage,
    silentAnswerInterval,
    test,
} from './client.harness';
import * as discovery from './fixtures/discovery';

// Asking a question in Discover and following the run that answers it: the field that records the question, the run
// the deployment starts for it, the reads that follow the run until it ends, and the way an answer is carried on into
// the Agent space.

const theAnswer = /The bays were confirmed at four/u;

test('follows a question asked in Discover from its first block to the finished answer', async ({
    page,
    deployment,
}) => {
    await page.clock.install();
    await openSignedIn(page);
    await holdTheClock(page);

    deployment.startNextAnswerRunning();

    const question = page.getByRole('searchbox', { name: 'Ask your mail' });
    await question.fill('How many bays were confirmed?');
    await question.press('Enter');

    // The run is one block into its plan: what arrived is drawn, and the run can still be stopped.
    await expect(page.getByText(theAnswer)).toBeVisible();
    await expect(page.getByRole('button', { name: 'Cancel run' })).toBeVisible();
    await expect(page.getByText('This run has finished.')).toHaveCount(0);

    // Asked about all mail, whatever the Mail space opened on: a first sign-in lands on the inbox of the one mailbox
    // the corpus has, and none of that narrows the run.
    expect(deployment.requests('POST', '/discovery/runs').map(({ body }) => body)).toStrictEqual([
        { question: 'How many bays were confirmed?', accounts: [], folders: [], thread: null, emails: [] },
    ]);

    await page.clock.runFor(silentAnswerInterval);

    // The read the interval caused answers the rest of the plan and the run's end, carried on from where the first
    // read stopped rather than from the beginning again.
    await expect(page.getByText('This run has finished.')).toBeVisible();
    await expect(page.getByText('The bays were confirmed and held to the end of the week')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Cancel run' })).toHaveCount(0);

    expect(
        deployment.requests('GET', `/discovery/runs/${discovery.runId}`).map(({ query }) => query.get('since')),
    ).toStrictEqual([null, '6']);
});

// A passage is taken from the document's own selection when the gesture that made it ends, in the reading pane. jsdom
// has a Selection API that answers whatever a test put in it; a browser is the one place the selection is what a
// triple-click actually selected — and what is asked of it here is that none of it reaches Discover: the words are not
// quoted under its field, and the run it starts reads all mail rather than the message that was open.
test('asks Discover about all mail while a passage of the message being read is selected', async ({
    page,
    deployment,
}) => {
    await openTheFirstMessage(page);

    const passage = 'You wrote: send me the list.';

    await page.getByRole('article', messageRegion).getByText(passage).click({ clickCount: 3 });
    await page.getByRole('link', { name: 'Discover' }).click();

    const asking = page.getByRole('search', { name: 'Ask your mail' });
    await expect(asking).toBeVisible();
    await expect(asking.getByText(passage)).toHaveCount(0);

    const question = page.getByRole('searchbox', { name: 'Ask your mail' });
    await question.fill('Which list did they mean?');
    await question.press('Enter');

    await expect(page.getByText(theAnswer)).toBeVisible();

    expect(deployment.requests('POST', '/discovery/runs').map(({ body }) => body)).toStrictEqual([
        { question: 'Which list did they mean?', accounts: [], folders: [], thread: null, emails: [] },
    ]);
});

test('carries a question from Discover into the agent', async ({ page, deployment }) => {
    await openSignedIn(page, '/#/discover');

    const question = page.getByRole('searchbox', { name: 'Ask your mail' });
    await question.fill('How many bays were confirmed?');
    await question.press('Enter');

    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();
    await expect(page.getByText(theAnswer)).toBeVisible();

    expect(deployment.requests('POST', '/discovery/runs').map(({ body }) => body)).toStrictEqual([
        { question: 'How many bays were confirmed?', accounts: [], folders: [], thread: null, emails: [] },
    ]);

    await page.getByRole('button', { name: 'Hand to the agent' }).click();

    // The agent opens a new conversation about this answer rather than about the whole mailbox, and says so above the
    // field before anything is asked in it.
    await expect(page.getByText('Context: Discover answer “How many bays were confirmed?”')).toBeVisible();

    await page.getByRole('textbox', { name: 'Tell the agent what to do' }).fill('Who confirmed them?');
    await page.getByRole('button', { name: 'Send', exact: true }).click();

    const thread = page.getByRole('log', { name: 'Conversation with the agent' });

    await expect(thread.getByText('Who confirmed them?')).toBeVisible();
    await expect(thread.getByText('Four bays were confirmed, held to the end of the week.')).toBeVisible();

    const asked = deployment.issued.filter(
        ({ method, route }) => method === 'POST' && /^\/agent\/conversations\/[^/]+\/messages$/u.test(route),
    );

    expect(asked.map(({ body }) => body)).toStrictEqual([
        expect.objectContaining({
            text: 'Who confirmed them?',
            scope: { kind: 'DiscoveryRun', subject: discovery.runId },
        }),
    ]);
});
