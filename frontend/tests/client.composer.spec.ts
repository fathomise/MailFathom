// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect, type Locator, type Page } from '@playwright/test';

import { holdTheClock, messageRegion, openSignedIn, test } from './client.harness';
import type { FakeDeployment } from './fakeDeployment';
import * as clipboard from './fixtures/clipboard';
import * as deploymentCorpus from './fixtures/deployment';
import * as drafts from './fixtures/drafts';
import * as messages from './fixtures/messages';

// Writing mail, as journeys across the reading pane, the composer, the send confirmation, the draft book, and the
// folder list: what the composer opens holding, what leaves the client when it is sent, and what a draft left behind
// reads as once it is opened again from Drafts. Each is asserted by what the screen holds afterwards and by the requests
// the page issued, because a unit test of the composer proves what it does with a value it was handed and nothing about
// what reaches the deployment or what the next screen draws.
//
// The second half is every way writing does not go as planned: a send the deployment refuses, a send stopped on its
// way, a message given up or kept, a machine that went offline or reloaded, and a sign-in that ended while somebody was
// writing. What each of those can lose is what somebody wrote, and where it went is only visible on the next screen.

const recipient = 'sales@nordwind.example';
const subject = 'Racking for the yard';
const words = 'The racking arrives on the ninth.';
const file = { name: 'yard-plan.txt', mimeType: 'text/plain', buffer: Buffer.from('bay 4, bay 5, bay 6\n') };

/** Signs in to Mail as a credential that may write and send mail, which is what the composer is drawn for. */
async function openMailWriting(page: Page, deployment: FakeDeployment): Promise<void> {
    deployment.grant('mailfathom.mail.drafts.write', 'mailfathom.mail.send');

    await openSignedIn(page, '/#/mail');
}

/** Writes an address into one header and commits it, the way a person finishes typing one. */
async function address(page: Page, header: 'To' | 'Cc' | 'Bcc', written: string): Promise<void> {
    await page.getByRole('combobox', { name: header, exact: true }).fill(written);
    await page.getByRole('combobox', { name: header, exact: true }).press('Enter');
    await expect(page.getByRole('button', { name: `Remove ${written} from ${header}` })).toBeVisible();
}

/** Writes the words of a message into its body. */
async function write(page: Page, written: string): Promise<void> {
    await page.getByRole('textbox', { name: 'Message' }).click();
    await page.keyboard.type(written);
}

/** Presses send and confirms it, which is the whole of sending: the composer closes and a toast says what became of it. */
async function send(page: Page): Promise<void> {
    await page.getByRole('button', { name: 'Send', exact: true }).click();
    await page.getByRole('dialog', { name: 'Send this message?' }).getByRole('button', { name: /^Send/u }).click();

    await expect(sent(page)).toBeVisible();
}

/** The notice a send the deployment queued leaves standing over whatever the person turned to next. */
function sent(page: Page) {
    return page.getByRole('list', { name: 'Notices' }).getByRole('listitem').filter({ hasText: 'Queued to go out.' });
}

/** The one draft route a check expects a request on, read off the order the page wrote them in. */
function draftRoutesOf(deployment: FakeDeployment, method: string, suffix: string): string[] {
    return deployment.issued
        .filter(
            (issued) =>
                issued.method === method && issued.route.startsWith('/drafts/') && issued.route.endsWith(suffix),
        )
        .map(({ route }) => route);
}

test('sends a new message carrying exactly the recipient, the subject, and the words typed into it', async ({
    page,
    deployment,
}) => {
    await openMailWriting(page, deployment);
    await page.getByRole('button', { name: 'New message', exact: true }).first().click();

    await address(page, 'To', recipient);
    await page.getByLabel('Subject').fill(subject);
    await write(page, words);
    await send(page);

    await expect(page.getByRole('region', { name: 'New message' })).toHaveCount(0);

    expect(deployment.requests('POST', '/drafts').map(({ body }) => body)).toStrictEqual([
        {
            plainTextBody: words,
            htmlBody: words,
            pictures: [],
            to: [recipient],
            cc: [],
            bcc: [],
            requestsDeliveryNotification: false,
            requestsReadReceipt: false,
            account: deploymentCorpus.workAccount.id,
            subject,
        },
    ]);
    expect(draftRoutesOf(deployment, 'POST', '/send')).toHaveLength(1);
});

test('draws the footer the account chosen under From sends with, follows another choice, and sends none of it as words', async ({
    page,
    deployment,
}) => {
    deployment.troubleTheAccounts();
    await openMailWriting(page, deployment);
    await page.getByRole('button', { name: 'New message', exact: true }).first().click();

    const composer = page.getByRole('region', { name: 'New message' });
    const footer = composer.getByRole('group', { name: 'Footer added when you send' });

    await footer.getByRole('button', { name: 'Your footer is hidden' }).click();
    await expect(footer.getByText('Head of Procurement · Nordwind')).toBeVisible();
    await expect(footer.getByRole('button', { name: 'Hide your footer' })).toHaveAttribute('aria-expanded', 'true');

    await composer.getByLabel('From').selectOption({ label: deploymentCorpus.accountBehind.displayName });
    await expect(footer.getByText('+48 600 555 012')).toBeVisible();
    await expect(footer.getByText('Head of Procurement · Nordwind')).toHaveCount(0);

    await composer.getByLabel('From').selectOption({ label: deploymentCorpus.failingAccount.displayName });
    await expect(footer).toHaveCount(0);

    await composer.getByLabel('From').selectOption({ label: deploymentCorpus.workAccount.displayName });
    await expect(footer.getByRole('button', { name: 'Your footer is hidden' })).toBeVisible();

    await address(page, 'To', recipient);
    await page.getByLabel('Subject').fill(subject);
    await write(page, words);
    await send(page);

    expect(deployment.requests('POST', '/drafts').map(({ body }) => body)).toStrictEqual([
        expect.objectContaining({ plainTextBody: words, htmlBody: words, account: deploymentCorpus.workAccount.id }),
    ]);
});

// Each way of answering opens where the reader stands — the mailbox and the list beside it, at the same address — and
// is addressed the way the client promises: the author, everybody, or nobody yet. What it answers travels as the
// message's identity rather than as quoted text, because the deployment derives the subject, the threading, and what a
// forward carries from the message itself.
for (const answer of [
    { pressed: 'Reply', titled: 'Reply', answers: 'senderOnly', to: ['news@example.invalid'], cc: [], prefix: 'Re' },
    {
        pressed: 'Reply all',
        titled: 'Reply to everyone',
        answers: 'everyone',
        to: ['news@example.invalid'],
        cc: ['reader@example.invalid'],
        prefix: 'Re',
    },
    { pressed: 'Forward', titled: 'Forward', answers: 'forward', to: [], cc: [], prefix: 'Fwd' },
] as const) {
    test(`opens ${answer.pressed.toLowerCase()} beside the message it answers, addressed as it promises, and sends it as an answer`, async ({
        page,
        deployment,
    }) => {
        await openMailWriting(page, deployment);
        await page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first().click();
        await expect(page.getByRole('article', messageRegion)).toBeVisible();
        await page
            .getByRole('toolbar', { name: 'Mail actions' })
            .getByRole('button', { name: answer.pressed, exact: true })
            .click();

        const composer = page.getByRole('region', { name: answer.titled });

        await expect(composer.getByLabel('Subject')).toContainText(`${answer.prefix}: ${messages.newsletterSubject}`);
        await expect(page.getByRole('listbox', { name: 'Messages' })).toBeVisible();
        await expect(page).toHaveURL(/#\/mail$/u);

        for (const addressed of answer.to) {
            await expect(composer.getByRole('button', { name: `Remove ${addressed} from To` })).toBeVisible();
        }

        for (const copied of answer.cc) {
            await expect(composer.getByRole('button', { name: `Remove ${copied} from Cc` })).toBeVisible();
        }

        if (answer.to.length === 0) {
            await address(page, 'To', recipient);
        }

        await write(page, words);
        await send(page);

        expect(deployment.requests('POST', '/drafts').map(({ body }) => body)).toStrictEqual([
            {
                plainTextBody: words,
                htmlBody: words,
                pictures: [],
                to: answer.to.length === 0 ? [recipient] : answer.to,
                cc: answer.cc,
                bcc: [],
                requestsDeliveryNotification: false,
                requestsReadReceipt: false,
                answeredEmailId: 'message-0',
                answers: answer.answers,
            },
        ]);
        expect(draftRoutesOf(deployment, 'POST', '/send')).toHaveLength(1);
    });
}

test('shows the copy headers, keeps what they hold while hidden, and sends nothing from them once they are hidden', async ({
    page,
    deployment,
}) => {
    const copied = 'yard@example.invalid';
    const blind = 'ledger@example.invalid';

    await openMailWriting(page, deployment);
    await page.getByRole('button', { name: 'New message', exact: true }).first().click();

    await address(page, 'To', recipient);
    await page.getByLabel('Subject').fill(subject);
    await write(page, words);

    await page.getByRole('button', { name: 'Show CC and BCC fields' }).click();
    await address(page, 'Cc', copied);
    await address(page, 'Bcc', blind);

    // Hidden, the headers are gone from the screen and from what the confirmation says the message goes to.
    await page.getByRole('button', { name: 'Hide CC and BCC fields' }).click();
    await expect(page.getByRole('combobox', { name: 'Cc', exact: true })).toHaveCount(0);

    // Shown again, they hold what was written in them: hiding them is not giving the addresses up.
    await page.getByRole('button', { name: 'Show CC and BCC fields' }).click();
    await expect(page.getByRole('button', { name: `Remove ${copied} from Cc` })).toBeVisible();
    await expect(page.getByRole('button', { name: `Remove ${blind} from Bcc` })).toBeVisible();

    await page.getByRole('button', { name: 'Hide CC and BCC fields' }).click();
    await page.getByRole('button', { name: 'Send', exact: true }).click();

    const confirmation = page.getByRole('dialog', { name: 'Send this message?' });

    await expect(confirmation.getByText(`To ${recipient}`)).toBeVisible();
    await expect(confirmation.getByText(/^Copy to|^Blind copy to/u)).toHaveCount(0);

    await confirmation.getByRole('button', { name: /^Send/u }).click();
    await expect(sent(page)).toBeVisible();

    expect(deployment.requests('POST', '/drafts').map(({ body }) => body)).toStrictEqual([
        expect.objectContaining({ to: [recipient], cc: [], bcc: [] }),
    ]);
});

test('keeps a draft with its file when the composer is left, revises that one draft from Drafts, and sends it out of the folder', async ({
    page,
    deployment,
}) => {
    await openMailWriting(page, deployment);
    await page.getByRole('button', { name: 'New message', exact: true }).first().click();

    await address(page, 'To', recipient);
    await page.getByLabel('Subject').fill(subject);
    await write(page, words);

    const chosen = page.waitForEvent('filechooser');
    await page.getByRole('button', { name: 'Attach' }).click();
    await (await chosen).setFiles(file);

    await expect(page.getByRole('list', { name: 'Attached files' }).getByText(file.name)).toBeVisible();

    // Leaving the composer is asked about, and keeping what was written files it with the file it carries.
    await page.getByRole('button', { name: 'Close the message' }).click();
    await page
        .getByRole('dialog', { name: 'Discard this message?' })
        .getByRole('button', { name: 'Save draft' })
        .click();
    await expect(page.getByRole('region', { name: 'New message' })).toHaveCount(0);

    const folders = page.getByRole('tree', { name: 'Mailboxes and folders' });
    const list = page.getByRole('listbox', { name: 'Messages' });
    const kept = list.getByRole('option', { name: new RegExp(subject, 'u') });

    await folders.getByRole('treeitem', { name: /^Drafts/u }).click();
    await kept.click();

    // Opened again from Drafts, it holds everything that was written and attached.
    const draft = page.getByRole('region', { name: 'Draft' });

    await expect(draft.getByRole('button', { name: `Remove ${recipient} from To` })).toBeVisible();
    await expect(draft.getByLabel('Subject')).toHaveValue(subject);
    await expect(draft.getByRole('textbox', { name: 'Message' })).toHaveText(words);
    await expect(draft.getByRole('list', { name: 'Attached files' }).getByText(file.name)).toBeVisible();

    // Drafts holds that one draft and no second copy of it while it is being carried on.
    await expect(kept).toHaveCount(1);

    await send(page);

    // Only the first save wrote a draft: the save the send made first revised that one, and the send named it.
    expect(deployment.requests('POST', '/drafts')).toHaveLength(1);

    const [revised = ''] = draftRoutesOf(deployment, 'PUT', '');

    expect(draftRoutesOf(deployment, 'PUT', '')).toStrictEqual([revised]);
    expect(draftRoutesOf(deployment, 'POST', '/send')).toStrictEqual([`${revised}/send`]);

    // The file went up once, for the draft that was kept, and stayed staged against it through the send.
    expect(draftRoutesOf(deployment, 'POST', '/attachments')).toStrictEqual([`${revised}/attachments`]);

    // Sent, the draft has left Drafts rather than standing there as if it had never gone.
    await folders.getByRole('treeitem', { name: /^Inbox/u }).click();
    await folders.getByRole('treeitem', { name: /^Drafts/u }).click();
    await expect(kept).toHaveCount(0);
});

// What a focused field draws is read off the field and the row around it — an outline, a shadow, or an edge taking the
// accent — and compared with what the same two drew a keypress earlier, so the check holds whichever of those the
// composer marks focus with rather than naming one. Asked as expressions rather than as closures, for the reason
// `client.layout.spec.ts` gives: this suite is compiled without a DOM declaration on purpose.
const drawing = `((field) => [field, field.parentElement]
    .map((element) => {
        const style = getComputedStyle(element);
        return [style.outlineStyle, style.outlineColor, style.boxShadow, style.borderBottomColor, style.borderLeftColor].join(' ');
    })
    .join(' / '))`;
const drawingOfTheFocused = `(() => { window.fieldReached = document.activeElement; return ${drawing}(document.activeElement); })()`;
const drawingOfTheFieldLeft = `${drawing}(window.fieldReached)`;

test('draws a visible focus on every field of the composer reached by the keyboard', async ({ page, deployment }) => {
    await openMailWriting(page, deployment);
    await page.getByRole('button', { name: 'New message', exact: true }).first().click();
    await page.getByRole('button', { name: 'Show CC and BCC fields' }).click();

    for (const field of [
        page.getByRole('combobox', { name: 'To', exact: true }),
        page.getByRole('combobox', { name: 'Cc', exact: true }),
        page.getByRole('combobox', { name: 'Bcc', exact: true }),
        page.getByLabel('Subject'),
        page.getByRole('textbox', { name: 'Message' }),
    ]) {
        // Reached from the control before it, which is how a keyboard arrives rather than how a script places focus.
        await field.focus();
        await page.keyboard.press('Shift+Tab');
        await page.keyboard.press('Tab');
        await expect(field).toBeFocused();

        const focused = await page.evaluate<string>(drawingOfTheFocused);

        await page.keyboard.press('Shift+Tab');
        await expect(field).not.toBeFocused();

        expect(focused).not.toBe(await page.evaluate<string>(drawingOfTheFieldLeft));
    }
});

/** Opens a new message and writes all of it: the recipient, the subject, the words, and a file. */
async function writeAll(page: Page): Promise<Locator> {
    await page.getByRole('button', { name: 'New message', exact: true }).first().click();
    await address(page, 'To', recipient);
    await page.getByLabel('Subject').fill(subject);
    await write(page, words);

    const chosen = page.waitForEvent('filechooser');
    await page.getByRole('button', { name: 'Attach' }).click();
    await (await chosen).setFiles(file);

    const composer = page.getByRole('region', { name: 'New message' });

    await expect(composer.getByRole('list', { name: 'Attached files' }).getByText(file.name)).toBeVisible();

    return composer;
}

/** That a composer holds everything `writeAll` wrote into it, with the words it is expected to carry by now. */
async function holdsEverything(composer: Locator, written = words): Promise<void> {
    await expect(composer.getByRole('button', { name: `Remove ${recipient} from To` })).toBeVisible();
    await expect(composer.getByLabel('Subject')).toHaveValue(subject);
    await expect(composer.getByRole('textbox', { name: 'Message' })).toHaveText(written);
    await expect(composer.getByRole('list', { name: 'Attached files' }).getByText(file.name)).toBeVisible();
}

/** Opens Drafts in the folder column. */
async function openDrafts(page: Page): Promise<void> {
    await page
        .getByRole('tree', { name: 'Mailboxes and folders' })
        .getByRole('treeitem', { name: /^Drafts/u })
        .click();
}

/** The row Drafts lists for the message these checks write. */
function draftRow(page: Page): Locator {
    return page.getByRole('listbox', { name: 'Messages' }).getByRole('option', { name: new RegExp(subject, 'u') });
}

/** Every notice standing, which is where what became of a send is said once the composer has closed. */
function notices(page: Page): Locator {
    return page.getByRole('list', { name: 'Notices' }).getByRole('listitem');
}

// Screening refusing what the corpus refusal names, which is what changing the words is the answer to.
const refusedContent =
    'Screening refused what this message carries. Changing what it says, or what it attaches, is what would change that.';

test('keeps everything written in the composer when the deployment refuses the send, and sends the corrected message as the same draft', async ({
    page,
    deployment,
}) => {
    const corrected = 'The racking arrives on the tenth.';
    const refused: string[] = [];

    await page.clock.install();
    await openMailWriting(page, deployment);
    await page.route(
        '**/api/client/drafts/*/send',
        (route) => {
            refused.push(new URL(route.request().url()).pathname);

            return route.fulfill({
                status: 409,
                contentType: 'application/problem+json',
                body: JSON.stringify(drafts.refusedSend),
            });
        },
        { times: 1 },
    );

    const composer = await writeAll(page);

    await holdTheClock(page);
    await page.getByRole('button', { name: 'Send', exact: true }).click();
    await page.getByRole('dialog', { name: 'Send this message?' }).getByRole('button', { name: /^Send/u }).click();

    // Said in the notice for somebody who looked away, and beside the words it refused, which are all still there.
    await expect(notices(page).filter({ hasText: 'Message not sent' })).toContainText(refusedContent);
    await expect(composer.getByText(refusedContent)).toBeVisible();
    await holdsEverything(composer);

    await composer.getByRole('textbox', { name: 'Message' }).click();
    await page.keyboard.press('ControlOrMeta+A');
    await page.keyboard.type(corrected);
    await send(page);

    await expect(composer).toHaveCount(0);

    // One draft, filed once with its file and revised with the correction, is what went out — the refused send and the
    // one that followed it both named it, and nothing was filed or attached a second time.
    const [revised = ''] = draftRoutesOf(deployment, 'PUT', '');

    expect(deployment.requests('POST', '/drafts').map(({ body }) => body)).toStrictEqual([
        expect.objectContaining({ plainTextBody: words, subject, to: [recipient] }),
    ]);
    expect(deployment.requests('PUT', revised).map(({ body }) => body)).toStrictEqual([
        expect.objectContaining({ plainTextBody: corrected, subject, to: [recipient] }),
    ]);
    expect(draftRoutesOf(deployment, 'POST', '/attachments')).toStrictEqual([`${revised}/attachments`]);
    expect(refused).toStrictEqual([`/api/client${revised}/send`]);
    expect(draftRoutesOf(deployment, 'POST', '/send')).toStrictEqual([`${revised}/send`]);
});

// Taking a send back is the stop its notice carries, and when it lands decides nothing about where the message goes:
// asked while the deployment is still filing it, the stop waits for the send and withdraws it; asked while the
// deployment is queueing it, it withdraws what was queued. Either way the message is back in Drafts, whole, and the
// notices say so — which is the seam no unit test reaches, the stop being the toast's and the folder the list's.
for (const held of [
    { while: 'filing it', route: '**/api/client/drafts' },
    { while: 'queueing it', route: '**/api/client/drafts/*/send' },
]) {
    test(`takes a send stopped while the deployment is ${held.while} back into Drafts, whole, and says that is where it is`, async ({
        page,
        deployment,
    }) => {
        let release: () => void = () => undefined;
        const released = new Promise<void>((resolve) => {
            release = resolve;
        });

        let holding = true;

        await page.clock.install();
        await openMailWriting(page, deployment);
        // Held by a flag rather than by `times: 1`: a route that expires is removed the moment its handler starts, and
        // Playwright hands a request whose handler was removed straight on to the next one — so the deployment would
        // answer at once, and the stop would land on a send it had already queued.
        await page.route(held.route, async (route) => {
            if (holding && route.request().method() === 'POST') {
                holding = false;
                await released;
            }

            await route.fallback();
        });

        await writeAll(page);
        await holdTheClock(page);
        await page.getByRole('button', { name: 'Send', exact: true }).click();
        await page.getByRole('dialog', { name: 'Send this message?' }).getByRole('button', { name: /^Send/u }).click();

        await notices(page)
            .filter({ hasText: 'Sending your message…' })
            .getByRole('button', { name: 'Stop the operation' })
            .click();
        await page
            .getByRole('dialog', { name: 'Stop the operation?' })
            .getByRole('button', { name: 'Stop the operation' })
            .click();
        release();

        await expect(notices(page).filter({ hasText: 'Message taken back' })).toContainText(
            'Taken back before it went out.',
        );
        await expect(notices(page).filter({ hasText: 'Stopped' })).toContainText(
            'take the message back into your own drafts',
        );
        await expect(page.getByRole('region', { name: 'New message' })).toHaveCount(0);
        expect(deployment.requests('POST', '/outbox/cancellation')).toHaveLength(1);

        await openDrafts(page);
        await draftRow(page).click();

        await holdsEverything(page.getByRole('region', { name: 'Draft' }));
    });
}

test('keeps everything when the composer is left for writing again, and takes the filed draft out of Drafts for good on Discard', async ({
    page,
    deployment,
}) => {
    await openMailWriting(page, deployment);

    const composer = await writeAll(page);

    await composer.getByRole('button', { name: 'Save draft' }).click();
    await expect(composer.getByText('Draft filed in your own drafts.')).toBeVisible();
    await openDrafts(page);
    await expect(draftRow(page)).toBeVisible();

    const asked = page.getByRole('dialog', { name: 'Discard this message?' });

    await composer.getByRole('button', { name: 'Close the message' }).click();
    await asked.getByRole('button', { name: 'Back to writing' }).click();
    await holdsEverything(composer);

    await composer.getByRole('button', { name: 'Close the message' }).click();
    await asked.getByRole('button', { name: 'Discard', exact: true }).click();
    await expect(composer).toHaveCount(0);
    // The dialog and the composer close in two renders, and each gives back the history entry it stood on: the client
    // is still what the tab shows, rather than whatever the tab showed before it.
    await expect(page.getByRole('navigation', { name: 'Spaces' })).toBeVisible();
    await expect(page).toHaveURL(/#\/mail$/u);

    const [staged = ''] = draftRoutesOf(deployment, 'POST', '/attachments');

    await expect
        .poll(() => draftRoutesOf(deployment, 'DELETE', ''))
        .toStrictEqual([staged.replace(/\/attachments$/u, '')]);

    await page.reload();
    await openDrafts(page);

    await expect(page.getByText('There is no mail in this folder.')).toBeVisible();
    await expect(draftRow(page)).toHaveCount(0);
});

test('keeps what is being written across a reload and while offline, and sends all of it once the network is back', async ({
    page,
    deployment,
    context,
}) => {
    const writtenOffline = ' Written with no network.';

    await openMailWriting(page, deployment);
    await page.getByRole('button', { name: 'New message', exact: true }).first().click();
    await address(page, 'To', recipient);
    await page.getByLabel('Subject').fill(subject);
    await write(page, words);

    await page.reload();
    await expect(page.getByRole('navigation', { name: 'Spaces' })).toBeVisible();
    await page.getByRole('button', { name: 'New message', exact: true }).first().click();

    const composer = page.getByRole('region', { name: 'New message' });
    const message = composer.getByRole('textbox', { name: 'Message' });

    await expect(composer.getByRole('button', { name: `Remove ${recipient} from To` })).toBeVisible();
    await expect(composer.getByLabel('Subject')).toHaveValue(subject);
    await expect(message).toHaveText(words);

    await context.setOffline(true);
    await expect(composer.getByText(/^No connection to the server\./u)).toBeVisible();
    await message.click();
    await page.keyboard.press('ControlOrMeta+End');
    await page.keyboard.type(writtenOffline);
    await expect(composer.getByRole('button', { name: 'Send', exact: true })).toBeDisabled();

    await context.setOffline(false);
    await expect(composer.getByRole('button', { name: 'Send', exact: true })).toBeEnabled();
    await send(page);

    expect(deployment.requests('POST', '/drafts').map(({ body }) => body)).toStrictEqual([
        expect.objectContaining({ plainTextBody: `${words}${writtenOffline}`, subject, to: [recipient] }),
    ]);
    expect(draftRoutesOf(deployment, 'POST', '/send')).toHaveLength(1);
});

test('colours, sizes, and aligns what was written from the bar, keeps it across a reload, and sends it as inline styles', async ({
    page,
    deployment,
}) => {
    await openMailWriting(page, deployment);
    await page.getByRole('button', { name: 'New message', exact: true }).first().click();
    await address(page, 'To', recipient);
    await page.getByLabel('Subject').fill(subject);
    await write(page, words);
    await page.keyboard.press('ControlOrMeta+A');

    const formatting = page.getByRole('toolbar', { name: 'Formatting' });

    await formatting.getByRole('button', { name: 'Text colour - Automatic' }).click();
    await page
        .getByRole('menu', { name: 'Text colour' })
        .getByRole('menuitemradio', { name: 'Red', exact: true })
        .click();
    await formatting.getByRole('button', { name: 'Text size - Normal' }).click();
    await page
        .getByRole('menu', { name: 'Text size' })
        .getByRole('menuitemradio', { name: 'Large', exact: true })
        .click();

    // The third by the keyboard alone: the control opens its menu on the choice it holds, the arrow walks to the
    // next, and Enter takes it.
    await formatting.getByRole('button', { name: 'Alignment - Align left' }).focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('menuitemradio', { name: 'Align left' })).toBeFocused();
    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('Enter');

    await expect(page.getByRole('menu')).toHaveCount(0);
    await expect(formatting.getByRole('button', { name: 'Text colour - Red' })).toBeVisible();
    await expect(formatting.getByRole('button', { name: 'Text size - Large' })).toBeVisible();
    await expect(formatting.getByRole('button', { name: 'Alignment - Centre' })).toBeVisible();

    await page.reload();
    await expect(page.getByRole('navigation', { name: 'Spaces' })).toBeVisible();
    await page.getByRole('button', { name: 'New message', exact: true }).first().click();
    await expect(page.getByRole('textbox', { name: 'Message' })).toHaveText(words);
    await send(page);

    expect(deployment.requests('POST', '/drafts').map(({ body }) => body)).toStrictEqual([
        expect.objectContaining({
            plainTextBody: words,
            htmlBody: `<div style="text-align:center"><span style="color:#f73030;font-size:18px">${words}</span></div>`,
        }),
    ]);
});

test('takes a colour off again with Automatic, so the message goes out with no colour at all', async ({
    page,
    deployment,
}) => {
    await openMailWriting(page, deployment);
    await page.getByRole('button', { name: 'New message', exact: true }).first().click();
    await address(page, 'To', recipient);
    await page.getByLabel('Subject').fill(subject);
    await write(page, words);
    await page.keyboard.press('ControlOrMeta+A');

    const formatting = page.getByRole('toolbar', { name: 'Formatting' });

    await formatting.getByRole('button', { name: 'Text colour - Automatic' }).click();
    await page
        .getByRole('menu', { name: 'Text colour' })
        .getByRole('menuitemradio', { name: 'Red', exact: true })
        .click();
    await formatting.getByRole('button', { name: 'Text colour - Red' }).click();
    await page.getByRole('menu', { name: 'Text colour' }).getByRole('menuitemradio', { name: 'Automatic' }).click();

    await expect(formatting.getByRole('button', { name: 'Text colour - Automatic' })).toBeVisible();
    await send(page);

    expect(deployment.requests('POST', '/drafts').map(({ body }) => body)).toStrictEqual([
        expect.objectContaining({ plainTextBody: words, htmlBody: words }),
    ]);
});

const cellStyle = 'border:1px solid #d0d5dd;padding:4px 8px;vertical-align:top;text-align:left';

/** One row of a table as the composer sends it, each cell written out with the grid every cell is drawn in. */
function sentRow(element: 'td' | 'th', ...cells: readonly string[]): string {
    return `<tr>${cells.map((cell) => `<${element} style="${cellStyle}">${cell}</${element}>`).join('')}</tr>`;
}

// Every structural act writes the table back through the engine's own editing command, so what the region reads back
// after it — and after a reload, which draws the kept tree rather than what the engine wrote — is the tree that was
// written, or the message sent differs from the table on the screen.
test('inserts a table, fills it by Tab, adds a row and a column to it, keeps it across a reload, and sends it as a grid', async ({
    page,
    deployment,
}) => {
    await openMailWriting(page, deployment);
    await page.getByRole('button', { name: 'New message', exact: true }).first().click();
    await address(page, 'To', recipient);
    await page.getByLabel('Subject').fill(subject);
    await page.getByRole('textbox', { name: 'Message' }).click();

    await page.getByRole('toolbar', { name: 'Formatting' }).getByRole('button', { name: 'Insert table' }).click();

    const insertion = page.getByRole('menu', { name: 'Insert a table' });

    await insertion.getByRole('menuitem', { name: 'One row fewer' }).click();
    await insertion.getByRole('menuitem', { name: 'One column fewer' }).click();
    await insertion.getByRole('menuitem', { name: 'Insert a 2 × 2 table' }).click();

    const message = page.getByRole('textbox', { name: 'Message' });
    const strip = page.getByRole('toolbar', { name: 'Table' });

    await expect(message.getByRole('table')).toHaveCount(1);
    await expect(strip).toBeVisible();

    // Tab out of the last cell is what adds the third row.
    for (const cell of ['Day', 'Hours', 'Monday', '9']) {
        await page.keyboard.type(cell);
        await page.keyboard.press('Tab');
    }

    await page.keyboard.type('Tuesday');
    await page.keyboard.press('Tab');
    await page.keyboard.type('10');
    await strip.getByRole('button', { name: 'Column after' }).click();
    await page.keyboard.press('Tab');
    await page.keyboard.type('closes 6');

    await expect(message.getByRole('row')).toHaveCount(3);
    await expect(message.getByRole('row').last().getByRole('cell')).toHaveText(['Tuesday', '10', 'closes 6']);

    // Each of the other acts, undone again by the one that takes it away, so the table sent is the one built above.
    await strip.getByRole('button', { name: 'Row above' }).click();
    await strip.getByRole('button', { name: 'Row below' }).click();
    await strip.getByRole('button', { name: 'Column before' }).click();
    await expect(message.getByRole('row')).toHaveCount(5);
    await expect(message.getByRole('row').nth(3).getByRole('cell')).toHaveText(['Tuesday', '10', '', 'closes 6']);

    await message.getByRole('row').first().getByRole('columnheader').nth(2).click();
    await strip.getByRole('button', { name: 'Remove column' }).click();
    await message.getByRole('row').nth(2).getByRole('cell').first().click();
    await strip.getByRole('button', { name: 'Remove row' }).click();
    await message.getByRole('row').last().getByRole('cell').first().click();
    await strip.getByRole('button', { name: 'Remove row' }).click();
    await expect(message.getByRole('row')).toHaveCount(3);

    await page.reload();
    await expect(page.getByRole('navigation', { name: 'Spaces' })).toBeVisible();
    await page.getByRole('button', { name: 'New message', exact: true }).first().click();
    await expect(page.getByRole('textbox', { name: 'Message' }).getByRole('row')).toHaveCount(3);
    await send(page);

    expect(deployment.requests('POST', '/drafts').map(({ body }) => body)).toStrictEqual([
        expect.objectContaining({
            plainTextBody: 'Day | Hours |\nMonday | 9 |\nTuesday | 10 | closes 6',
            htmlBody: `<table style="border-collapse:collapse">${sentRow('th', 'Day', 'Hours', '')}${sentRow('td', 'Monday', '9', '')}${sentRow('td', 'Tuesday', '10', 'closes 6')}</table>`,
        }),
    ]);
});

test('deletes a table from its strip and takes the deletion back with one Ctrl+Z', async ({ page, deployment }) => {
    await openMailWriting(page, deployment);
    await page.getByRole('button', { name: 'New message', exact: true }).first().click();
    await page.getByRole('textbox', { name: 'Message' }).click();
    await page.getByRole('toolbar', { name: 'Formatting' }).getByRole('button', { name: 'Insert table' }).click();
    await page
        .getByRole('menu', { name: 'Insert a table' })
        .getByRole('menuitem', { name: /^Insert a / })
        .click();
    await page.keyboard.type('Day');

    const message = page.getByRole('textbox', { name: 'Message' });

    await page.getByRole('toolbar', { name: 'Table' }).getByRole('button', { name: 'Delete table' }).click();

    await expect(message.getByRole('table')).toHaveCount(0);
    await expect(page.getByRole('toolbar', { name: 'Table' })).toHaveCount(0);

    await page.keyboard.press('Control+Z');

    await expect(message.getByRole('table')).toHaveCount(1);
    await expect(message.getByRole('columnheader').first()).toHaveText('Day');
});

/**
 * Writes a message, has the deployment stop accepting the sign-in at the next read of a folder, and signs in again as
 * the login named — which is where what was written either comes back or does not.
 */
async function turnedAwayWhileWriting(page: Page, deployment: FakeDeployment, login: string): Promise<Locator> {
    await openMailWriting(page, deployment);
    await page.getByRole('button', { name: 'New message', exact: true }).first().click();
    await address(page, 'To', recipient);
    await page.getByLabel('Subject').fill(subject);
    await write(page, words);

    await page.route('**/api/client/emails?*', (route) => route.fulfill({ status: 401 }), { times: 1 });
    await openDrafts(page);
    await expect(
        page.getByText('This server has stopped accepting the sign-in that was kept. Sign in again.'),
    ).toBeVisible();

    await page.getByRole('textbox', { name: 'Login' }).fill(login);
    await page.getByLabel('Password', { exact: true }).fill(deploymentCorpus.password);
    await page.getByRole('button', { name: 'Connect' }).click();
    await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first()).toBeVisible();

    return page.getByRole('region', { name: 'New message' });
}

// A sign-in the deployment ends while somebody is writing ends the frame, and what they wrote is theirs rather than
// the machine's: the same person signing in again finds the composer as they left it, and anybody else finds nothing
// of it — which is the seam between the frame that ends a sign-in and the composer that keeps a composition.
test('keeps what was being written when the deployment stops accepting the sign-in, for the person who wrote it', async ({
    page,
    deployment,
}) => {
    const composer = await turnedAwayWhileWriting(page, deployment, deploymentCorpus.userName);

    await expect(composer.getByRole('button', { name: `Remove ${recipient} from To` })).toBeVisible();
    await expect(composer.getByLabel('Subject')).toHaveValue(subject);
    await expect(composer.getByRole('textbox', { name: 'Message' })).toHaveText(words);
});

test('offers nothing of what was being written to anybody else who signs in after the deployment ended the sign-in', async ({
    page,
    deployment,
}) => {
    const composer = await turnedAwayWhileWriting(page, deployment, 'somebody.else');

    await expect(composer).toHaveCount(0);

    await page.getByRole('button', { name: 'New message', exact: true }).first().click();

    await expect(composer.getByLabel('Subject')).toHaveValue('');
    await expect(composer.getByRole('textbox', { name: 'Message' })).toHaveText('');
});

// A paste is read from the real clipboard, through the keys a person presses, because Ctrl+V and Ctrl+Shift+V raise the
// same event and only the key that raised it tells them apart.
const pastedAddress = 'https://example.invalid/invoice';

/** Puts text on the clipboard, which the page may write to once it has been granted that. */
async function copy(page: Page, text: string): Promise<void> {
    await page.context().grantPermissions(['clipboard-read', 'clipboard-write']);
    await page.evaluate(`navigator.clipboard.writeText(${JSON.stringify(text)})`);
}

/** Opens a new message addressed and titled, with words written into it, ready to paste into. */
async function writeToPasteInto(page: Page, deployment: FakeDeployment, written: string): Promise<Locator> {
    await openMailWriting(page, deployment);
    await page.getByRole('button', { name: 'New message', exact: true }).first().click();
    await address(page, 'To', recipient);
    await page.getByLabel('Subject').fill(subject);
    await write(page, written);

    return page.getByRole('textbox', { name: 'Message' });
}

/** Selects the word before the caret, the way somebody holding Ctrl and Shift over the left arrow would. */
async function selectTheWordBefore(page: Page): Promise<void> {
    await page.keyboard.press('Control+Shift+ArrowLeft');
}

test('links the selected words to an address pasted over them, takes it back with one Ctrl+Z, and sends the link', async ({
    page,
    deployment,
}) => {
    const message = await writeToPasteInto(page, deployment, 'Pay the invoice');

    await copy(page, pastedAddress);
    await selectTheWordBefore(page);
    await page.keyboard.press('Control+V');

    await expect(message.getByRole('link', { name: 'invoice', exact: true })).toHaveAttribute('href', pastedAddress);
    await expect(message).toHaveText('Pay the invoice');

    await page.keyboard.press('Control+Z');

    await expect(message.getByRole('link')).toHaveCount(0);
    await expect(message).toHaveText('Pay the invoice');

    await page.keyboard.press('End');
    await selectTheWordBefore(page);
    await page.keyboard.press('Control+V');
    await send(page);

    expect(deployment.requests('POST', '/drafts').map(({ body }) => body)).toStrictEqual([
        expect.objectContaining({
            htmlBody: `Pay the <a href="${pastedAddress}">invoice</a>`,
            plainTextBody: 'Pay the invoice',
        }),
    ]);
});

test('inserts a pasted address as a link showing it where nothing is selected, and keeps what is typed next out of it', async ({
    page,
    deployment,
}) => {
    const message = await writeToPasteInto(page, deployment, 'See ');

    await copy(page, pastedAddress);
    await page.keyboard.press('Control+V');
    await page.keyboard.type('today');

    await expect(message.getByRole('link', { name: pastedAddress })).toHaveText(pastedAddress);
    await expect(message).toHaveText(new RegExp(`^See\\s${pastedAddress}\\stoday$`, 'u'));

    await send(page);

    expect(deployment.requests('POST', '/drafts').map(({ body }) => body)).toStrictEqual([
        expect.objectContaining({
            htmlBody: expect.stringContaining(`<a href="${pastedAddress}">${pastedAddress}</a>`),
            plainTextBody: `See ${pastedAddress} today`,
        }),
    ]);
});

test('pastes as text an address it would not link to, words around an address, and an address under Ctrl+Shift+V', async ({
    page,
    deployment,
}) => {
    const message = await writeToPasteInto(page, deployment, 'Notes');
    const pasted: readonly (readonly [string, string])[] = [
        ['Control+V', 'javascript:alert(1)'],
        ['Control+V', `pay ${pastedAddress}`],
        ['Control+Shift+V', pastedAddress],
    ];

    for (const [keys, text] of pasted) {
        await page.keyboard.press('Enter');
        await copy(page, text);
        await page.keyboard.press(keys);

        await expect(message).toContainText(text);
    }

    await expect(message.getByRole('link')).toHaveCount(0);
});

// A picture of one pixel, whole, because the engine decodes what is written to its clipboard and re-encodes it.
const onePixelPng = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==';

/** Puts a picture on the clipboard beside markup naming a remote one, which is how a browser's *Copy image* writes it. */
async function copyPicture(page: Page): Promise<void> {
    await page.context().grantPermissions(['clipboard-read', 'clipboard-write']);
    // Written as the page's own script, as `copy` above is, because this suite is compiled without the DOM's types.
    await page.evaluate(`navigator.clipboard.write([
        new ClipboardItem({
            'image/png': new Blob([Uint8Array.from(atob('${onePixelPng}'), (character) => character.charCodeAt(0))], {
                type: 'image/png',
            }),
            'text/html': new Blob(['<img src="https://pictures.example.invalid/chart.png">'], { type: 'text/html' }),
        }),
    ])`);
}

test('pastes a picture from an address of its own, takes its removal back with one Ctrl+Z, keeps it across a reload, and sends it inside the message', async ({
    page,
    deployment,
}) => {
    const hostsAsked = new Set<string>();

    page.on('request', (request) => hostsAsked.add(new URL(request.url()).host));

    const message = await writeToPasteInto(page, deployment, 'The chart');
    const picture = message.getByRole('img');

    await copyPicture(page);
    await page.keyboard.press('Control+V');

    await expect(picture).toHaveCount(1);
    await expect(picture).toHaveAttribute('src', /^blob:/u);

    await picture.click();
    await page.getByRole('toolbar', { name: 'Picture' }).getByRole('button', { name: 'Remove picture' }).click();

    await expect(picture).toHaveCount(0);

    await page.keyboard.press('Control+Z');

    await expect(picture).toHaveCount(1);

    await page.reload();
    await expect(page.getByRole('navigation', { name: 'Spaces' })).toBeVisible();
    await page.getByRole('button', { name: 'New message', exact: true }).first().click();

    await expect(picture).toHaveCount(1);
    await expect(message).toHaveText('The chart');

    await send(page);

    const [sent] = deployment.requests('POST', '/drafts').map(({ body }) => body as Record<string, unknown>);
    const pictures = sent?.['pictures'] as readonly { readonly name: string; readonly content: string }[];

    expect(pictures).toHaveLength(1);
    expect(sent).toMatchObject({
        plainTextBody: 'The chart',
        htmlBody: expect.stringContaining(`src="cid:${pictures[0]?.name ?? ''}" width="1" height="1"`),
    });
    expect(hostsAsked).not.toContain('pictures.example.invalid');
});

// A recorded clipboard document reaches the composer through an event the page's own script raises, carrying exactly
// the recording. The asynchronous clipboard sanitizes what is written to it on the way in, which would take out the
// very constructs the hostile document exists to put in front of the reader, and would make each recording whatever
// Chromium's sanitizer left of it rather than what the source wrote.
async function deliver(page: Page, kind: 'paste' | 'drop', flavours: Readonly<Record<string, string>>): Promise<void> {
    const region = page.getByRole('textbox', { name: 'Message' });
    const box = await region.boundingBox();

    await region.focus();
    // Written as the page's own script, as `copy` above is, because this suite is compiled without the DOM's types.
    await page.evaluate(`(() => {
        const carried = new DataTransfer();

        for (const [flavour, value] of Object.entries(${JSON.stringify(flavours)})) {
            carried.setData(flavour, value);
        }

        document.activeElement.dispatchEvent(
            ${JSON.stringify(kind)} === 'paste'
                ? new ClipboardEvent('paste', { clipboardData: carried, bubbles: true, cancelable: true })
                : new DragEvent('drop', {
                      dataTransfer: carried,
                      clientX: ${String((box?.x ?? 0) + (box?.width ?? 0) / 2)},
                      clientY: ${String((box?.y ?? 0) + (box?.height ?? 0) - 4)},
                      bubbles: true,
                      cancelable: true,
                  }),
        );
    })()`);
}

/** The one message a check sent, as the deployment received it. */
function sentBody(deployment: FakeDeployment): Record<string, unknown> {
    const [body] = deployment.requests('POST', '/drafts').map((request) => request.body as Record<string, unknown>);

    return body ?? {};
}

test('reads a hostile document pasted and dropped to its words, with no request leaving the page and no script run', async ({
    page,
    deployment,
}) => {
    const hostsAsked = new Set<string>();

    page.on('request', (request) => hostsAsked.add(new URL(request.url()).host));

    const message = await writeToPasteInto(page, deployment, 'Notes');

    await deliver(page, 'paste', { 'text/html': clipboard.hostileDocument });
    await deliver(page, 'drop', { 'text/html': clipboard.hostileDocument });

    await expect(message.getByRole('cell', { name: 'Kept' })).toHaveCount(2);
    await expect(message.getByText('Quarterly notes')).toHaveCount(2);
    await expect(message.getByRole('link')).toHaveCount(0);
    await expect(message.getByRole('img')).toHaveCount(0);

    await send(page);

    expect(sentBody(deployment)['htmlBody']).not.toMatch(/invalid|javascript|script|onload|onerror|onclick/u);
    expect(await page.evaluate(`globalThis.${clipboard.ranMarker} ?? null`)).toBeNull();
    expect([...hostsAsked].filter((host) => host !== new URL(page.url()).host)).toEqual([]);
});

test('pastes a formatted passage keeping what the closed set holds, takes it back with one Ctrl+Z, and sends it as inline styles', async ({
    page,
    deployment,
}) => {
    const message = await writeToPasteInto(page, deployment, 'Yard notes');

    await deliver(page, 'paste', { 'text/html': clipboard.formattedBlock, 'text/plain': clipboard.formattedBlockText });

    await expect(message.getByRole('link', { name: 'the plan' })).toHaveAttribute(
        'href',
        'https://yard.example.invalid/plan',
    );
    await expect(message.getByRole('listitem')).toHaveCount(3);
    await expect(message.getByRole('img')).toHaveCount(0);

    await page.keyboard.press('Control+Z');

    await expect(message).toHaveText('Yard notes');

    await deliver(page, 'paste', { 'text/html': clipboard.formattedBlock, 'text/plain': clipboard.formattedBlockText });
    await expect(message.getByRole('listitem')).toHaveCount(3);
    await send(page);

    expect(sentBody(deployment)).toMatchObject({
        htmlBody: expect.stringMatching(
            /<span style="font-size:32px"><b>Yard handover<\/b><\/span>[\s\S]*<span style="color:#b42318">Gate code changes Friday\.<\/span>[\s\S]*<a href="https:\/\/yard\.example\.invalid\/plan">the plan<\/a> or nothing\./u,
        ),
        plainTextBody: expect.stringContaining('- Bay 4\n- Bay 5'),
    });
});

test('pastes a range copied from a spreadsheet as a bordered table with its header row, and sends it as a grid', async ({
    page,
    deployment,
}) => {
    const message = await writeToPasteInto(page, deployment, 'Figures');

    await deliver(page, 'paste', { 'text/html': clipboard.excelRange, 'text/plain': clipboard.rangeText });

    await expect(message.getByRole('columnheader')).toHaveText(['Region', 'Units', 'Change']);
    await expect(message.getByRole('cell', { name: '-35' })).toBeVisible();

    // Typed straight after the paste, so the line after the table holding them is where the caret was left.
    await page.keyboard.type('Totals follow.');

    await send(page);

    expect(sentBody(deployment)).toMatchObject({
        htmlBody: expect.stringMatching(/<\/table><p>Totals follow\.<\/p>/u),
    });

    expect(sentBody(deployment)).toMatchObject({
        htmlBody: expect.stringMatching(
            /<th style="border:1px solid #d0d5dd;padding:4px 8px;vertical-align:top;background-color:#d9e1f2;text-align:left"><b>Region<\/b><\/th>[\s\S]*<td style="[^"]*text-align:right"><span style="color:#c00000">-35<\/span><\/td>/u,
        ),
        plainTextBody: expect.stringContaining('Region | Units | Change\nNorth yard | 1,240 | -35'),
    });
});

test('pastes the words alone under Ctrl+Shift+V, and reads the formatting the real clipboard carries under Ctrl+V', async ({
    page,
    deployment,
}) => {
    const message = await writeToPasteInto(page, deployment, 'Plan');

    await page.context().grantPermissions(['clipboard-read', 'clipboard-write']);
    await page.evaluate(`navigator.clipboard.write([
        new ClipboardItem({
            'text/html': new Blob([${JSON.stringify('<p>See <a href="https://yard.example.invalid/plan">the plan</a></p>')}], { type: 'text/html' }),
            'text/plain': new Blob(['See the plan'], { type: 'text/plain' }),
        }),
    ])`);

    await page.keyboard.press('Enter');
    await page.keyboard.press('Control+Shift+V');

    await expect(message).toContainText('See the plan');
    await expect(message.getByRole('link')).toHaveCount(0);

    await page.keyboard.press('Enter');
    await page.keyboard.press('Control+V');

    await expect(message.getByRole('link', { name: 'the plan' })).toHaveAttribute(
        'href',
        'https://yard.example.invalid/plan',
    );
});
