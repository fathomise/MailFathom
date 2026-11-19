// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect } from '@playwright/test';

// The contract set: what the browser suite's journeys rely on the deployment doing, stated once and asked of two
// targets. `deploymentContract.spec.ts` asks it of the fake every journey signs in to, in process, on every pull request
// that reaches the client; `end-to-end/deploymentContract.spec.ts` asks it of the deployment the end-to-end run stands
// up, over HTTP. A case passing against one and failing against the other is a fake that misreads the service, or a
// service that is wrong, and `frontend/tests/AGENTS.md` § *The contract set* says which is corrected and where.
//
// **A case is relational, never literal.** The fake starts from the corpus and the deployment from generated mail, so no
// case names a message, a folder, or a count. Each reads where it starts, makes one write, and asserts how the reads
// after it moved — and then puts back what it changed, because the browser specs that run after it on the deployment
// meet the same mailbox.
//
// **Every answer is held to the shape the service published**, read out of
// `backend/tests/PublicSurfaces.UnitTests/http-api-contract.json`, the recording of the HTTP surface: a field the fake
// renamed, left out, or typed differently fails the case that read it, naming the field. A case never states a shape of
// its own, so there is no third copy of the contract to fall out of step with the other two.
//
// No browser runs here and nothing here knows which target it is asking. What only the fake can do — stop answering,
// refuse the next change, serve signals, run an Agent answer — stays in `fakeDeployment.spec.ts`.

/** One answer a target gave: its status and its body as JSON, or `null` where the body was empty. */
export interface DeploymentAnswer {
    readonly status: number;
    readonly body: unknown;
}

/** What a case is asked of: one request beneath `/api/client`, and what came back. */
export interface ContractTarget {
    ask(method: string, route: string, body?: unknown): Promise<DeploymentAnswer>;
}

/** A body as a case reads it: JSON where it is JSON, the text itself where it is not, and `null` where it is empty. */
export function bodyOf(text: string): unknown {
    if (text === '') {
        return null;
    }

    try {
        return JSON.parse(text) as unknown;
    } catch {
        return text;
    }
}

/** One behaviour the browser suite relies on, stated once for both targets. */
export interface ContractCase {
    readonly title: string;
    readonly run: (deployment: ContractDeployment) => Promise<void>;
}

// The recording, read once per process. Its path is the service's, because that is where the surface is recorded
// byte for byte and where a change to the service moves it.
type Schema = Readonly<Record<string, unknown>>;

const recording = JSON.parse(
    readFileSync(
        join(import.meta.dirname, '..', '..', 'backend', 'tests', 'PublicSurfaces.UnitTests', 'http-api-contract.json'),
        'utf8',
    ),
) as {
    paths: Record<string, Record<string, { responses?: Record<string, Schema> }>>;
    components: { schemas: Record<string, Schema> };
};

const clientPrefix = '/api/client';

/**
 * The recorded route a request reached, as its template: the literal one where a literal and a parameter would both
 * match — `/tasks/proposed` rather than `/tasks/{taskId}` — because that is how the service's own routing chooses.
 */
function templateOf(route: string): string | undefined {
    const path = `${clientPrefix}${route.split('?')[0] ?? ''}`;
    const parameters = (template: string) => (template.match(/\{[^}]+\}/gu) ?? []).length;

    return Object.keys(recording.paths)
        .filter((template) =>
            new RegExp(
                `^${template.replace(/[.*+?^$()|[\]\\]/gu, '\\$&').replace(/\\?\{[^}]+\\?\}/gu, '[^/]+')}$`,
                'u',
            ).test(path),
        )
        .sort((one, other) => parameters(one) - parameters(other))[0];
}

/** The schema the recording publishes for one answer, or `undefined` where it publishes no body for that status. */
function recordedSchema(method: string, route: string, status: number): Schema | undefined {
    const template = templateOf(route);
    const operation = template === undefined ? undefined : recording.paths[template]?.[method.toLowerCase()];
    const response = operation?.responses?.[String(status)];

    if (response === undefined) {
        throw new Error(
            `The recording publishes no ${String(status)} answer to ${method} ${template ?? route}, so the answer itself disagrees with the service.`,
        );
    }

    const content = response['content'] as Record<string, { schema?: Schema }> | undefined;

    return content?.['application/json']?.schema;
}

function resolved(schema: Schema): Schema {
    const reference = schema['$ref'];

    if (typeof reference !== 'string') {
        return schema;
    }

    const named = recording.components.schemas[reference.split('/').at(-1) ?? ''];

    if (named === undefined) {
        throw new Error(`The recording names a schema it does not hold: ${reference}.`);
    }

    return named;
}

function typeOf(value: unknown): string {
    if (value === null) {
        return 'null';
    }

    if (Array.isArray(value)) {
        return 'array';
    }

    if (typeof value === 'number') {
        return Number.isInteger(value) ? 'integer' : 'number';
    }

    return typeof value;
}

/**
 * Every way `value` departs from `schema`, each naming where: a field the service never answers, which is how a renamed
 * one arrives, and a field typed otherwise than the service types it. It reads the part of JSON Schema the recording
 * uses — `type`, `properties`, `items`, `additionalProperties`, `enum`, `oneOf`, `anyOf`, and `$ref` — and leaves
 * `format` and `pattern` alone, because the fake mints identities a reader can tell apart rather than UUIDs.
 *
 * A field left out is not a departure, on the rule `scripts/test-agent-workflow.sh` argues for the corpus: an answer the
 * fake composes states the part a journey reads, and the case reading a field is what asserts it is there.
 */
export function departuresFrom(schema: Schema, value: unknown, at = '$'): string[] {
    const stated = resolved(schema);
    const alternatives = (stated['oneOf'] ?? stated['anyOf']) as Schema[] | undefined;

    if (alternatives !== undefined) {
        const each = alternatives.map((alternative) => departuresFrom(alternative, value, at));

        return each.some((departures) => departures.length === 0)
            ? []
            : (each.sort((one, other) => one.length - other.length)[0] ?? []);
    }

    const declared = stated['type'];
    const types = typeof declared === 'string' ? [declared] : ((declared ?? []) as readonly string[]);
    const actual = typeOf(value);

    if (types.length > 0 && !types.includes(actual) && !(actual === 'integer' && types.includes('number'))) {
        return [`${at} is ${actual} where the service answers ${types.join(' or ')}`];
    }

    const allowed = stated['enum'] as unknown[] | undefined;

    if (allowed !== undefined && !allowed.includes(value)) {
        return [`${at} is ${JSON.stringify(value)}, which the service never answers`];
    }

    if (Array.isArray(value)) {
        const items = stated['items'] as Schema | undefined;

        return items === undefined
            ? []
            : value.flatMap((item, index) => departuresFrom(items, item, `${at}[${String(index)}]`));
    }

    if (actual !== 'object') {
        return [];
    }

    const properties = stated['properties'] as Readonly<Record<string, Schema>> | undefined;
    const additional = stated['additionalProperties'] as Schema | boolean | undefined;

    // An object the recording states neither fields nor values for is one whose content the service does not fix.
    if (properties === undefined && typeof additional !== 'object') {
        return [];
    }

    return Object.entries(value as Readonly<Record<string, unknown>>).flatMap(([name, held]) => {
        const property = properties?.[name] ?? (typeof additional === 'object' ? additional : undefined);

        return property === undefined
            ? [`${at}.${name} is a field the service does not answer`]
            : departuresFrom(property, held, `${at}.${name}`);
    });
}

// What the cases read, typed as far as they read it. Each is cast only after `answered` held the whole answer against
// the recording, which is the check that earns the cast.
interface Folder {
    readonly id: string;
    readonly role: string | null;
    readonly path: readonly string[];
    readonly storedEmailCount: number;
    readonly unreadEmailCount: number;
}

interface MoveAnswer {
    readonly results: readonly {
        outcome: string;
        destinationFolder: string | null;
        change: RecordedChange | null;
    }[];
}

interface FolderTree {
    readonly accounts: readonly {
        readonly account: { readonly id: string };
        readonly folders: readonly Folder[];
        readonly outbox: { readonly waiting: number; readonly refused: number } | null;
    }[];
}

interface Row {
    readonly id: string;
    readonly unread: boolean;
    readonly flagged: boolean;
}

interface RecordedChange {
    readonly recordId: string;
    readonly state: string;
}

interface ManagedFolder {
    readonly id: string;
    readonly parentId: string | null;
    readonly name: string;
}

interface FolderAct {
    readonly change: string;
    readonly folder: ManagedFolder;
}

interface Draft {
    readonly draftId: string;
    readonly subject: string;
    readonly revision: number;

    /** The message the drafts folder shows the draft as, which moves with each save. */
    readonly filedEmailId: string | null;
}

/** The mailbox a case acts on: the account whose inbox holds mail, and that inbox. */
export interface Mailbox {
    readonly account: string;
    readonly inbox: Folder;
}

/**
 * A key for one act, generated once per act as the client generates one: the service answers a repeat under a key with
 * the first outcome, so two acts sharing one — in two runs against one deployment as much as in one — would read as one.
 */
function nextRequestId(): string {
    return crypto.randomUUID();
}

/** A change a write opened, followed until the service reports it finished. */
const unfinished = new Set(['pending', 'converging']);

/** The states a record has finished in. A dead-lettered one finished as well, which the case asserting it then reports. */
const finishedWell = new Set(['completed', 'cancelled']);

/** The two answers a mail write may give that both mean the change was taken: made at once, or written down to make. */
const taken = expect.stringMatching(/^(?:applied|recorded)$/u);

/**
 * One target, asked the way every case asks it: each answer held to its recorded status and shape, and each write the
 * service applies to the mailbox later waited on through its own report of it.
 */
export class ContractDeployment {
    private readonly target: ContractTarget;

    constructor(target: ContractTarget) {
        this.target = target;
    }

    /** Asks one route, and answers its body once its status and its shape are the ones the service publishes. */
    async answered(method: string, route: string, body?: unknown, status = 200): Promise<unknown> {
        const answer = await this.target.ask(method, route, body);

        expect(
            answer.status,
            `${method} ${route} answered ${String(answer.status)}: ${JSON.stringify(answer.body)}`,
        ).toBe(status);

        const schema = recordedSchema(method, route, status);

        if (schema !== undefined) {
            expect(
                departuresFrom(schema, answer.body),
                `${method} ${route} answered a shape the service does not`,
            ).toStrictEqual([]);
        }

        return answer.body;
    }

    async folderTree(): Promise<FolderTree> {
        return (await this.answered('GET', '/folders')) as FolderTree;
    }

    /** The account whose inbox holds mail, which is the one mailbox both targets fill. */
    async mailbox(): Promise<Mailbox> {
        const tree = await this.folderTree();
        const found = tree.accounts
            .map(({ account, folders }) => ({
                account: account.id,
                inbox: folders.find(({ role }) => role === 'Inbox'),
            }))
            .find(({ inbox }) => inbox !== undefined && inbox.storedEmailCount > 0);

        if (found?.inbox === undefined) {
            throw new Error(
                'No account holds an inbox with mail in it, so there is nothing for a mail case to act on.',
            );
        }

        return { account: found.account, inbox: found.inbox };
    }

    /** One folder as the folder tree answers it now, or `undefined` where the tree no longer holds it. */
    async folder(folderId: string): Promise<Folder | undefined> {
        return (await this.folderTree()).accounts.flatMap(({ folders }) => folders).find(({ id }) => id === folderId);
    }

    /** The first page of a folder, narrowed by whatever filters `query` adds. */
    async rows(folderId: string, query = ''): Promise<readonly Row[]> {
        const page = (await this.answered('GET', `/emails?folder=${encodeURIComponent(folderId)}${query}`)) as {
            emails: readonly Row[] | null;
        };

        return page.emails ?? [];
    }

    async rowIds(folderId: string, query = ''): Promise<readonly string[]> {
        return (await this.rows(folderId, query)).map(({ id }) => id);
    }

    /** The first row of the first page of a folder, which is the message a mail case acts on. */
    async firstRow(folderId: string, matching: (row: Row) => boolean = () => true): Promise<Row> {
        const row = (await this.rows(folderId)).find(matching);

        if (row === undefined) {
            throw new Error(`The first page of folder ${folderId} holds no message a case can act on.`);
        }

        return row;
    }

    /** The records named, as the service reports them now. */
    async changes(recordIds: readonly string[]): Promise<readonly RecordedChange[]> {
        const query = recordIds.map((recordId) => `record=${encodeURIComponent(recordId)}`).join('&');

        return ((await this.answered('GET', `/mutations?${query}`)) as { changes: readonly RecordedChange[] }).changes;
    }

    /**
     * Waits until every change named has finished, through the service's own report of each — never a duration, because
     * how long the mailbox takes to converge is the service's to say. The fake reports them finished at once.
     */
    async settled(recordIds: readonly string[]): Promise<void> {
        if (recordIds.length === 0) {
            return;
        }

        await expect
            .poll(
                async () => {
                    const states = (await this.changes(recordIds)).map(({ state }) => state);

                    return states.length === recordIds.length && states.every((state) => !unfinished.has(state));
                },
                { message: `Changes ${recordIds.join(', ')} did not finish`, timeout: 120_000 },
            )
            .toBe(true);

        for (const { recordId, state } of await this.changes(recordIds)) {
            expect(finishedWell.has(state), `Change ${recordId} finished ${state}`).toBe(true);
        }
    }

    /** Asks for flags on one message, and answers once the change it opened has finished. */
    async flag(storedEmailId: string, flags: { seen?: boolean; flagged?: boolean }): Promise<void> {
        const answer = (await this.answered('POST', '/mutations/flags', {
            requestId: nextRequestId(),
            changes: [{ storedEmailId, flags }],
        })) as {
            results: readonly { outcome: string; changes: readonly RecordedChange[] }[];
        };

        expect(answer.results.map(({ outcome }) => outcome)).toStrictEqual([taken]);
        await this.settled(answer.results.flatMap(({ changes }) => changes.map(({ recordId }) => recordId)));
    }

    /**
     * Moves one message, and answers once the move has finished.
     *
     * A mirrored account files into a folder made a moment ago once the pass making it started has bound it, and
     * answers `destination-not-found` until then. That answer records nothing, so the move is asked again, as a new act,
     * until the service says it is taken or answers anything else.
     */
    async move(storedEmailId: string, destinationFolder: string): Promise<void> {
        let answer: MoveAnswer = { results: [] };

        await expect
            .poll(
                async () => {
                    answer = (await this.answered('POST', '/mutations/moves', {
                        requestId: nextRequestId(),
                        moves: [{ storedEmailId, destinationFolder }],
                    })) as MoveAnswer;

                    return answer.results[0]?.outcome;
                },
                { message: `Folder ${destinationFolder} never took a message`, timeout: 60_000 },
            )
            .not.toBe('destination-not-found');

        expect(answer.results).toMatchObject([{ outcome: taken, destinationFolder }]);
        await this.settled(answer.results.flatMap(({ change }) => (change === null ? [] : [change.recordId])));
    }

    async managedFolders(
        account: string,
    ): Promise<{ creatableRoles: readonly string[]; folders: readonly ManagedFolder[] }> {
        return (await this.answered('GET', `/managed-folders?account=${encodeURIComponent(account)}`)) as {
            creatableRoles: readonly string[];
            folders: readonly ManagedFolder[];
        };
    }

    async folderAct(route: string, body: unknown): Promise<FolderAct> {
        return (await this.answered('POST', route, body)) as FolderAct;
    }

    async drafts(account: string): Promise<readonly Draft[]> {
        return (
            (await this.answered('GET', `/drafts?account=${encodeURIComponent(account)}`)) as {
                drafts: readonly Draft[];
            }
        ).drafts;
    }

    /**
     * Makes sure the account files drafts somewhere, which a draft is written into. The corpus's account has a drafts
     * folder and a mailbox the end-to-end run filled has none, so this asks for one only where the account offers it.
     */
    async withDraftsFolder(account: string): Promise<void> {
        if ((await this.managedFolders(account)).creatableRoles.includes('Drafts')) {
            expect(await this.folderAct('/managed-folders', { account, role: 'Drafts' })).toMatchObject({
                change: 'Created',
            });
        }
    }

    /** Writes one draft of a message of its own, addressed to nobody who exists. */
    async writeDraft(account: string, subject: string): Promise<Draft> {
        return (await this.answered('POST', '/drafts', draftOf(account, subject))) as Draft;
    }
}

function draftOf(account: string, subject: string) {
    return {
        account,
        subject,
        plainTextBody: 'Written by a contract case, and given up by it again.',
        htmlBody: null,
        to: ['nobody@contract.invalid'],
        cc: [],
        bcc: [],
        answeredEmailId: null,
        answers: null,
    };
}

/** The day a case dates something on, as `yyyy-mm-dd` in UTC, so the fake and the deployment read the same day. */
function dayAfter(days: number): string {
    return new Date(Date.UTC(2026, 0, 1) + days * 86_400_000).toISOString().slice(0, 10);
}

/** A day far from anything either target holds, so a window around it holds exactly what a case wrote there. */
const caseDay = dayAfter(3_650);

export const contractCases: readonly ContractCase[] = [
    {
        title: 'answers a flag on the next read of its folder, finds it among the flagged, and forgets it once taken off',
        run: async (deployment) => {
            const { inbox } = await deployment.mailbox();
            const row = await deployment.firstRow(inbox.id, ({ flagged }) => !flagged);

            await deployment.flag(row.id, { flagged: true });

            expect((await deployment.rows(inbox.id)).find(({ id }) => id === row.id)).toMatchObject({ flagged: true });
            expect(await deployment.rowIds(inbox.id, '&flagged=true')).toContain(row.id);

            await deployment.flag(row.id, { flagged: false });

            expect((await deployment.rows(inbox.id)).find(({ id }) => id === row.id)).toMatchObject({ flagged: false });
            expect(await deployment.rowIds(inbox.id, '&flagged=true')).not.toContain(row.id);
        },
    },
    {
        title: 'takes one from its folder’s unread count for a message marked read, and puts it back once marked unread',
        run: async (deployment) => {
            const { inbox } = await deployment.mailbox();
            const row = await deployment.firstRow(inbox.id, ({ unread }) => unread);

            await deployment.flag(row.id, { seen: true });

            expect((await deployment.folder(inbox.id))?.unreadEmailCount).toBe(inbox.unreadEmailCount - 1);
            expect((await deployment.rows(inbox.id)).find(({ id }) => id === row.id)).toMatchObject({ unread: false });

            await deployment.flag(row.id, { seen: false });

            expect((await deployment.folder(inbox.id))?.unreadEmailCount).toBe(inbox.unreadEmailCount);
            expect((await deployment.rows(inbox.id)).find(({ id }) => id === row.id)).toMatchObject({ unread: true });
        },
    },
    {
        title: 'files a moved message in the other folder’s list and count and out of its own, and back again',
        run: async (deployment) => {
            const { account, inbox } = await deployment.mailbox();
            const made = await deployment.folderAct('/managed-folders', {
                account,
                parentId: null,
                name: 'Moved by a case',
            });
            const row = await deployment.firstRow(inbox.id);

            // Where the message stands is read until the reads agree rather than once: a mirrored account's pass that
            // rediscovers a message the first move filed can put it back there after the second move was taken, and the
            // next pass over the folder it was moved back to brings it home.
            const placed = async () => ({
                inInbox: (await deployment.rowIds(inbox.id)).includes(row.id),
                inMade: await deployment.rowIds(made.folder.id),
                inboxCount: (await deployment.folder(inbox.id))?.storedEmailCount,
                madeCount: (await deployment.folder(made.folder.id))?.storedEmailCount,
            });
            const settledAs = (expected: Awaited<ReturnType<typeof placed>>) =>
                expect
                    .poll(placed, { message: `Message ${row.id} never stood where the move left it`, timeout: 60_000 })
                    .toStrictEqual(expected);

            expect(made).toMatchObject({ change: 'Created', folder: { name: 'Moved by a case', parentId: null } });
            expect(await deployment.folder(made.folder.id)).toMatchObject({ storedEmailCount: 0 });

            await deployment.move(row.id, made.folder.id);
            await settledAs({
                inInbox: false,
                inMade: [row.id],
                inboxCount: inbox.storedEmailCount - 1,
                madeCount: 1,
            });

            await deployment.move(row.id, inbox.id);
            await settledAs({ inInbox: true, inMade: [], inboxCount: inbox.storedEmailCount, madeCount: 0 });

            await deployment.folderAct('/managed-folders/deletions', { account, folderId: made.folder.id });
        },
    },
    {
        title: 'takes a deleted message out of its folder’s list and count, and puts it back once the delete is withdrawn',
        run: async (deployment) => {
            const { inbox } = await deployment.mailbox();
            const row = await deployment.firstRow(inbox.id);
            const deleted = (await deployment.answered('POST', '/mutations/deletes', {
                requestId: nextRequestId(),
                deletes: [{ storedEmailId: row.id }],
            })) as {
                results: readonly { storedEmailId: string; outcome: string; change: RecordedChange | null }[];
            };

            // The record waits out the window in which the way back is offered, which is the state a withdrawal reaches.
            expect(deleted.results).toMatchObject([
                { storedEmailId: row.id, outcome: taken, change: { state: 'pending' } },
            ]);

            const recordId = deleted.results[0]?.change?.recordId ?? '';

            expect(await deployment.rowIds(inbox.id)).not.toContain(row.id);
            expect((await deployment.folder(inbox.id))?.storedEmailCount).toBe(inbox.storedEmailCount - 1);

            expect(
                await deployment.answered('POST', '/mutations/deletes/withdrawals', { recordIds: [recordId] }),
            ).toMatchObject({ changes: [{ recordId, state: 'cancelled' }] });

            expect(await deployment.changes([recordId])).toMatchObject([{ recordId, state: 'cancelled' }]);
            expect(await deployment.rowIds(inbox.id)).toContain(row.id);
            expect((await deployment.folder(inbox.id))?.storedEmailCount).toBe(inbox.storedEmailCount);
        },
    },
    {
        title: 'answers the spam verdict a person marked on every later read of the message, and the one that undid it',
        run: async (deployment) => {
            const { inbox } = await deployment.mailbox();
            const row = await deployment.firstRow(inbox.id);
            const mark = async (verdict: 'Spam' | 'NotSpam') => {
                const answer = (await deployment.answered('POST', '/mutations/spam', {
                    requestId: nextRequestId(),
                    verdict,
                    spamSubcategory: null,
                    storedEmailIds: [row.id],
                })) as { results: readonly { outcome: string; moved: boolean; changes: readonly RecordedChange[] }[] };

                expect(answer.results).toMatchObject([{ outcome: taken, moved: false }]);
                await deployment.settled(
                    answer.results.flatMap(({ changes }) => changes.map(({ recordId }) => recordId)),
                );

                return (await deployment.answered('GET', `/messages/${encodeURIComponent(row.id)}`)) as {
                    spam: { verdict: string } | null;
                };
            };

            expect(await mark('Spam')).toMatchObject({ spam: { verdict: 'Spam' } });
            expect(await mark('NotSpam')).toMatchObject({ spam: { verdict: 'NotSpam' } });
        },
    },
    {
        title: 'lists a draft it was written, revises it under the identity it was written at, and forgets it once given up',
        run: async (deployment) => {
            const { account } = await deployment.mailbox();

            await deployment.withDraftsFolder(account);

            const written = await deployment.writeDraft(account, 'Written by a contract case');

            expect(written).toMatchObject({ subject: 'Written by a contract case' });
            expect((await deployment.drafts(account)).find(({ draftId }) => draftId === written.draftId)).toMatchObject(
                {
                    subject: 'Written by a contract case',
                },
            );

            const revised = (await deployment.answered(
                'PUT',
                `/drafts/${encodeURIComponent(written.draftId)}`,
                draftOf(account, 'Revised by a contract case'),
            )) as Draft;

            expect(revised).toMatchObject({ draftId: written.draftId, subject: 'Revised by a contract case' });
            expect(revised.revision).toBeGreaterThan(written.revision);
            expect(
                (await deployment.drafts(account)).filter(({ draftId }) => draftId === written.draftId),
            ).toMatchObject([{ subject: 'Revised by a contract case' }]);

            expect(await deployment.answered('DELETE', `/drafts/${encodeURIComponent(written.draftId)}`)).toMatchObject(
                {
                    draftId: written.draftId,
                },
            );
            expect((await deployment.drafts(account)).map(({ draftId }) => draftId)).not.toContain(written.draftId);
        },
    },
    {
        title: 'files a draft as the message its record names, anew at each save, and gives the draft up with a delete of that message',
        run: async (deployment) => {
            const { account } = await deployment.mailbox();

            await deployment.withDraftsFolder(account);

            const draftsFolder = (await deployment.folderTree()).accounts
                .find((held) => held.account.id === account)
                ?.folders.find(({ role }) => role === 'Drafts');

            if (draftsFolder === undefined) {
                throw new Error('The account files drafts nowhere, so there is no folder to list a filed draft in.');
            }

            const written = await deployment.writeDraft(account, 'Filed by a contract case');
            const revised = (await deployment.answered(
                'PUT',
                `/drafts/${encodeURIComponent(written.draftId)}`,
                draftOf(account, 'Filed again by a contract case'),
            )) as Draft;
            const filed = revised.filedEmailId ?? '';

            // The folder lists the revision at once, as the message the record names, and no longer as the one before.
            expect(written.filedEmailId).toEqual(expect.any(String));
            expect(filed).not.toBe(written.filedEmailId);
            expect(await deployment.rowIds(draftsFolder.id)).toContain(filed);
            expect(await deployment.rowIds(draftsFolder.id)).not.toContain(written.filedEmailId);

            // Deleting that message is giving the draft up: nothing is written down to follow, and nothing is left
            // of it in the folder or in the book. The same key asked again is answered as it was.
            const requestId = nextRequestId();
            const givenUp = () =>
                deployment.answered('POST', '/mutations/deletes', { requestId, deletes: [{ storedEmailId: filed }] });
            const answered = { results: [{ storedEmailId: filed, outcome: 'applied', change: null }] };

            expect(await givenUp()).toMatchObject(answered);
            expect(await deployment.rowIds(draftsFolder.id)).not.toContain(filed);
            expect((await deployment.drafts(account)).map(({ draftId }) => draftId)).not.toContain(written.draftId);
            expect(await givenUp()).toMatchObject(answered);
        },
    },
    {
        title: 'lists a send it queued in the Outbox and counts it, and hands the draft back once the send is cancelled',
        run: async (deployment) => {
            const { account } = await deployment.mailbox();
            const outbox = async () =>
                (
                    (await deployment.answered('GET', `/outbox?account=${encodeURIComponent(account)}`)) as {
                        sends: readonly { outgoingEmail: string; stage: string }[];
                    }
                ).sends;
            const waiting = async () =>
                (await deployment.folderTree()).accounts.find((held) => held.account.id === account)?.outbox?.waiting;

            await deployment.withDraftsFolder(account);

            const written = await deployment.writeDraft(account, 'Sent later by a contract case');
            const waitingBefore = await waiting();

            // Sent for a day nobody reaches, so the send stands in the Outbox until it is cancelled, whatever the
            // deployment would otherwise do with a message to an address that does not exist.
            const sent = (await deployment.answered('POST', `/drafts/${encodeURIComponent(written.draftId)}/send`, {
                dueAt: `${caseDay}T09:00:00+00:00`,
                dueTimeZone: 'UTC',
            })) as { outgoingEmail: string };

            expect((await outbox()).find(({ outgoingEmail }) => outgoingEmail === sent.outgoingEmail)).toMatchObject({
                stage: 'Recorded',
            });
            expect(await waiting()).toBe((waitingBefore ?? 0) + 1);
            expect((await deployment.drafts(account)).map(({ draftId }) => draftId)).not.toContain(written.draftId);

            expect(
                await deployment.answered('POST', '/outbox/cancellation', { outgoingEmail: sent.outgoingEmail }),
            ).toMatchObject({ outgoingEmail: sent.outgoingEmail, outcome: 'Accepted' });

            expect((await outbox()).map(({ outgoingEmail }) => outgoingEmail)).not.toContain(sent.outgoingEmail);
            expect(await waiting()).toBe(waitingBefore);
            expect((await deployment.drafts(account)).map(({ draftId }) => draftId)).toContain(written.draftId);

            await deployment.answered('DELETE', `/drafts/${encodeURIComponent(written.draftId)}`);
        },
    },
    {
        title: 'answers a folder made, renamed, moved, and deleted at the place each act left it in both folder routes',
        run: async (deployment) => {
            const { account } = await deployment.mailbox();
            const placed = async (folderId: string) => ({
                tree: (await deployment.folder(folderId))?.path,
                managed: (await deployment.managedFolders(account)).folders.find(({ id }) => id === folderId),
            });

            // A mirrored account files what a folder act did on its server into the folder tree on the pass after it,
            // so where a folder now stands is read until it says so rather than once.
            const settledAt = (folderId: string) =>
                expect.poll(() => placed(folderId), {
                    message: `Folder ${folderId} never reached the place the act left it in`,
                    timeout: 120_000,
                });

            const parent = await deployment.folderAct('/managed-folders', {
                account,
                parentId: null,
                name: 'Case parent',
            });
            const made = await deployment.folderAct('/managed-folders', {
                account,
                parentId: null,
                name: 'Case child',
            });

            expect(made).toMatchObject({ change: 'Created', folder: { name: 'Case child', parentId: null } });
            await settledAt(made.folder.id).toMatchObject({
                tree: ['Case child'],
                managed: { name: 'Case child', parentId: null },
            });

            expect(
                await deployment.folderAct('/managed-folders/renames', {
                    account,
                    folderId: made.folder.id,
                    name: 'Case renamed',
                }),
            ).toMatchObject({ change: 'Renamed', folder: { id: made.folder.id, name: 'Case renamed' } });
            await settledAt(made.folder.id).toMatchObject({
                tree: ['Case renamed'],
                managed: { name: 'Case renamed' },
            });

            expect(
                await deployment.folderAct('/managed-folders/moves', {
                    account,
                    folderId: made.folder.id,
                    parentId: parent.folder.id,
                }),
            ).toMatchObject({ change: 'Moved', folder: { id: made.folder.id, parentId: parent.folder.id } });
            await settledAt(made.folder.id).toMatchObject({
                tree: ['Case parent', 'Case renamed'],
                managed: { parentId: parent.folder.id },
            });

            await deployment.folderAct('/managed-folders/deletions', { account, folderId: parent.folder.id });

            await settledAt(parent.folder.id).toStrictEqual({ tree: undefined, managed: undefined });
            await settledAt(made.folder.id).toStrictEqual({ tree: undefined, managed: undefined });
        },
    },
    {
        title: 'lists a task it was written, answers it done once marked, and forgets it once erased',
        run: async (deployment) => {
            const listed = async () =>
                (
                    (await deployment.answered('GET', '/tasks')) as {
                        tasks: readonly { id: string; completed: boolean }[];
                    }
                ).tasks;
            const written = (await deployment.answered('POST', '/tasks', {
                title: 'Written by a contract case',
                dueOn: caseDay,
                reminders: [],
                dueDayOffsetMinutes: null,
                sourceMessageId: null,
            })) as { id: string; title: string; dueOn: string | null };

            expect(written).toMatchObject({ title: 'Written by a contract case', dueOn: caseDay, completed: false });
            expect((await listed()).find(({ id }) => id === written.id)).toMatchObject({ completed: false });

            expect(
                await deployment.answered('POST', `/tasks/${encodeURIComponent(written.id)}/completion`, {
                    completed: true,
                }),
            ).toMatchObject({ id: written.id, completed: true });
            expect((await listed()).find(({ id }) => id === written.id)).toMatchObject({ completed: true });

            expect(await deployment.answered('DELETE', `/tasks/${encodeURIComponent(written.id)}`)).toStrictEqual({
                id: written.id,
                erased: true,
            });
            expect((await listed()).map(({ id }) => id)).not.toContain(written.id);
        },
    },
    {
        title: 'revises a task it was written to the whole record stated, which the list then answers',
        run: async (deployment) => {
            const record = {
                title: 'Written by a contract case',
                dueOn: caseDay,
                reminders: [],
                dueDayOffsetMinutes: null,
                sourceMessageId: null,
                direction: 'Owed',
                estimatedMinutes: 30,
                plan: null,
                anchor: null,
                counterpart: null,
            };
            const written = (await deployment.answered('POST', '/tasks', record)) as { id: string };
            const route = `/tasks/${encodeURIComponent(written.id)}`;

            expect(
                await deployment.answered('PUT', route, {
                    ...record,
                    title: 'Revised by a contract case',
                    dueOn: null,
                    direction: 'Awaited',
                    counterpart: { address: 'nobody@contract.example', displayName: null },
                }),
            ).toMatchObject({ id: written.id, title: 'Revised by a contract case', dueOn: null, direction: 'Awaited' });

            const listed = (
                (await deployment.answered('GET', '/tasks')) as {
                    tasks: readonly { id: string; title: string; direction: string; estimatedMinutes: number | null }[];
                }
            ).tasks.find(({ id }) => id === written.id);

            expect(listed).toMatchObject({
                title: 'Revised by a contract case',
                direction: 'Awaited',
                estimatedMinutes: 30,
                counterpart: { address: 'nobody@contract.example' },
            });

            await deployment.answered('DELETE', route);
        },
    },
    {
        title: 'places an event it was written in the window it falls in, where an amendment moves it, and nowhere once deleted',
        run: async (deployment) => {
            const window = async (day: string) =>
                (
                    (await deployment.answered(
                        'GET',
                        `/calendar?from=${day}T00:00:00.000Z&until=${day}T23:59:59.999Z`,
                    )) as { events: readonly { id: string; start: string }[] }
                ).events.map(({ id }) => id);
            const event = (title: string, day: string) => ({
                title,
                notes: null,
                start: `${day}T10:00:00.000Z`,
                end: `${day}T11:00:00.000Z`,
                isAllDay: false,
                reminders: [],
            });
            const nextDay = dayAfter(3_651);
            const written = (await deployment.answered('POST', '/calendar', {
                ...event('Written by a contract case', caseDay),
                sourceMessage: null,
            })) as { id: string };

            expect(written).toMatchObject({ title: 'Written by a contract case' });
            expect(await window(caseDay)).toContain(written.id);

            expect(
                await deployment.answered(
                    'PUT',
                    `/calendar/${encodeURIComponent(written.id)}`,
                    event('Moved by a contract case', nextDay),
                ),
            ).toMatchObject({ id: written.id, title: 'Moved by a contract case' });
            expect(await window(caseDay)).not.toContain(written.id);
            expect(await window(nextDay)).toContain(written.id);

            await deployment.answered('DELETE', `/calendar/${encodeURIComponent(written.id)}`, undefined, 204);

            expect(await window(nextDay)).not.toContain(written.id);
        },
    },
    {
        title: 'finds a person it was written in the book by a fragment of their name, as amended, and not once erased',
        run: async (deployment) => {
            const found = async () =>
                (
                    (await deployment.answered('GET', '/contacts?search=contract')) as {
                        contacts: readonly { id: string; note: string | null }[];
                    }
                ).contacts;
            const person = (note: string) => ({
                displayName: 'Written by a contract case',
                addresses: ['written@contract.invalid'],
                preferredAddress: 'written@contract.invalid',
                note,
            });
            const written = (await deployment.answered('POST', '/contacts', person('As written'))) as {
                outcome: string;
                contact: { id: string };
            };

            expect(written).toMatchObject({ outcome: 'Written', contact: { note: 'As written' } });
            expect((await found()).find(({ id }) => id === written.contact.id)).toMatchObject({ note: 'As written' });

            expect(
                await deployment.answered('PUT', `/contacts/${encodeURIComponent(written.contact.id)}`, {
                    ...person('As amended'),
                    restatedAddresses: [],
                }),
            ).toMatchObject({ contact: { id: written.contact.id, note: 'As amended' } });
            expect((await found()).find(({ id }) => id === written.contact.id)).toMatchObject({ note: 'As amended' });

            expect(
                await deployment.answered('DELETE', `/contacts/${encodeURIComponent(written.contact.id)}`),
            ).toMatchObject({
                contact: written.contact.id,
                wasHeld: true,
            });
            expect((await found()).map(({ id }) => id)).not.toContain(written.contact.id);
        },
    },
];
