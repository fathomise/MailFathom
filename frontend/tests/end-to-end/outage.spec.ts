// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { execFile } from 'node:child_process';
import { resolve } from 'node:path';
import { promisify } from 'node:util';
import { expect, test } from '@playwright/test';
import { required, signIn } from './deployment';

// What the client says while its deployment cannot reach the mail server, and that it stops saying it once the server is
// back. The browser suite draws the same line against a fake that reports an account unreachable; this one takes the
// mailbox away from a real MailFathom, through the fault proxy `scripts/run-end-to-end-client.sh` puts between it and
// the mail server, so what is proven is that the service notices the outage, reports it, and recovers from it, and that
// the client draws each of those as `design/` draws the freshness line: *Not current* while the server is away, *Mail
// server unreachable* inside it, and green again afterwards.
//
// The fault is started with `scripts/inject-fault.sh` rather than through the proxy's API, so the preset is the one a
// developer types and nothing about it is defined twice. `start` returns once the proxy holds the fault and `stop` is
// how the spec ends it, whatever happened in between; the duration is only a bound on an outage a killed run left
// behind.

const faultScriptPath = resolve(import.meta.dirname, '../../../scripts/inject-fault.sh');

// Read rather than defaulted, for the reason every value the run hands over is: the script's own default is the port the
// Compose overlay publishes, and a developer running one beside this suite would have the spec break the wrong proxy.
const faultControlPort = required('MAILFATHOM_FAULTS_CONTROL_PORT');

const runFaultScript = promisify(execFile);

/** Runs the fault script and answers with what it printed; a refusal rejects with the script's own reason. */
async function faultScript(...command: string[]): Promise<string> {
    const { stdout } = await runFaultScript('bash', [faultScriptPath, ...command], {
        env: { ...process.env, MAILFATHOM_FAULTS_CONTROL_PORT: faultControlPort },
    });

    return stdout;
}

test('says the mail server cannot be reached while it is away, and that every account is current once it is back', async ({
    page,
}) => {
    // The outage is noticed at the account's next run and drawn once it has held a minute, and the recovery waits for
    // the run after the server returns — minutes of a deployment's own pacing rather than of the client's.
    test.setTimeout(5 * 60_000);

    await signIn(page);
    await page.getByRole('link', { name: 'Mail' }).click();
    await expect(page.getByRole('tree', { name: 'Mailboxes and folders' })).toBeVisible();

    const current = page.getByText('Every account is up to date.');
    const notCurrent = page.getByRole('button', { name: /is not up to date\.$/u });

    await expect(current).toBeVisible();

    expect(await faultScript('start', 'imap-down', '--for', '10m')).toContain('Started imap-down:');

    let stopped: string;

    try {
        await expect(notCurrent).toBeVisible({ timeout: 3 * 60_000 });

        await notCurrent.click();

        await expect(page.getByText('Mail server unreachable', { exact: true })).toBeVisible();
        await expect(page.getByText(/^Its mail server cannot be reached/u)).toBeVisible();
    } finally {
        stopped = await faultScript('stop', 'imap-down');
    }

    expect(stopped).toContain('Removed imap-down.');

    await expect(current).toBeVisible({ timeout: 2 * 60_000 });
    await expect(notCurrent).toHaveCount(0);
});
