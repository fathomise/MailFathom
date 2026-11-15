// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { randomUUID } from 'node:crypto';
import { connect, type Socket } from 'node:net';
import { required } from './deployment';

// The mail server itself, as a spec reaches it beside the client: the second witness, and the post a spec's own mail
// arrives by.
//
// **The second witness is what the mail server holds once the client has acted, read over IMAP.** What a person cares
// about is where their mail ended up, and the screen is the client's account of that while the mailbox is the outcome
// itself — a client drawing a row gone over a mailbox that still holds it is the defect this file exists to catch. The
// database is the service's own business and is not read. The reading never writes: every folder is opened with
// EXAMINE, which the protocol defines as read-only, so asking about a message cannot mark it seen under the spec that
// asked.
//
// **A spec acts on mail it delivered itself, over SMTP, under a subject nobody else wrote.** The corpus is exchanges, so
// a subject read off the screen names a whole conversation rather than one message, and the protocol searches subjects
// as substrings, so no subject the corpus carries names one message on the server either. Mail a spec delivered is
// mail it can name exactly on both sides, and acting on it leaves the corpus as the next spec expects it whatever the
// act removed. It arrives the way the corpus did — submitted to the mail server and synchronized out of it — so the
// client meets it as it meets any other mail.
//
// Both protocol clients are written here rather than taken from a package, for the reason the desktop suite writes its
// WebDriver client: a handful of commands over a socket to a server on loopback that speaks no TLS is a page of code,
// against a pinned dependency, a register row, and a re-counted census. Each understands exactly what its commands
// answer and refuses anything else, so a server answering something new fails a spec loudly rather than being half-read.

/** Where the mail server is and how the mailbox is reached, handed over by `scripts/run-end-to-end-client.sh`. */
const mailServer = {
    host: '127.0.0.1',
    imapPort: Number(required('MAILFATHOM_MAIL_SERVER_IMAP_PORT')),
    smtpPort: Number(required('MAILFATHOM_MAIL_SERVER_SMTP_PORT')),
    address: required('MAILFATHOM_MAIL_SERVER_ADDRESS'),
    login: required('MAILFATHOM_MAIL_SERVER_LOGIN'),
    password: required('MAILFATHOM_MAIL_SERVER_PASSWORD'),
};

/** The address every spec's own mail is delivered to: the mailbox the deployment reads. */
export const mailboxAddress = mailServer.address;

/** One message as the mail server holds it: the identifier it gave it in its folder, and the flags set on it. */
export interface StoredMessage {
    readonly uid: number;
    readonly flags: readonly string[];
}

/** A bound on one exchange, so a server that stops answering fails the spec rather than holding it to its timeout. */
const answerTimeout = 15_000;

/** One connection to the mail server, read a line at a time. */
class Conversation {
    private readonly socket: Socket;
    private received = Buffer.alloc(0);
    private wake: (() => void) | null = null;
    private failure: Error | null = null;

    public constructor(port: number) {
        this.socket = connect(port, mailServer.host);
        this.socket.setTimeout(answerTimeout);
        this.socket.on('data', (chunk: Buffer) => {
            this.received = Buffer.concat([this.received, chunk]);
            this.wake?.();
        });
        this.socket.on('timeout', () => this.socket.destroy(new Error('The mail server stopped answering.')));
        this.socket.on('error', (error) => {
            this.failure = error;
            this.wake?.();
        });
        this.socket.on('close', () => {
            this.failure ??= new Error('The mail server closed the connection.');
            this.wake?.();
        });
    }

    /** Waits until at least one more chunk has arrived, or the connection has failed. */
    private async more(): Promise<void> {
        if (this.failure === null) {
            await new Promise<void>((resolve) => {
                this.wake = resolve;
            });
            this.wake = null;
        }

        if (this.failure !== null) {
            throw this.failure;
        }
    }

    /** One line the server sent, without its line ending, with any IMAP literal it announced read into it. */
    public async line(): Promise<string> {
        let line = '';

        for (;;) {
            let end = this.received.indexOf('\r\n');

            while (end < 0) {
                await this.more();
                end = this.received.indexOf('\r\n');
            }

            const part = this.received.subarray(0, end).toString('utf8');
            this.received = this.received.subarray(end + 2);
            line += part;

            const announced = /\{(\d+)\}$/u.exec(part);

            if (announced === null) {
                return line;
            }

            const length = Number(announced[1]);

            while (this.received.length < length) {
                await this.more();
            }

            line += this.received.subarray(0, length).toString('utf8');
            this.received = this.received.subarray(length);
        }
    }

    public write(written: string | Buffer): void {
        this.socket.write(written);
    }

    public close(): void {
        this.socket.destroy();
    }
}

/** A string written as an IMAP quoted string. */
function quoted(value: string): string {
    return `"${value.replaceAll('\\', '\\\\').replaceAll('"', '\\"')}"`;
}

/** An IMAP session over one conversation, numbering its own commands. */
class ImapSession {
    private readonly conversation: Conversation;
    private tagNumber = 0;

    public constructor(conversation: Conversation) {
        this.conversation = conversation;
    }

    /**
     * Sends one command and answers with the untagged lines it produced, refusing anything but OK.
     *
     * A `Buffer` part is sent as a literal, after the server's continuation, which is the one way the protocol carries a
     * string a quoted one could not — a subject may hold any character.
     */
    public async command(...parts: readonly (string | Buffer)[]): Promise<string[]> {
        this.tagNumber += 1;
        const tag = `E${String(this.tagNumber)}`;
        let pending = `${tag} `;

        for (const part of parts) {
            if (typeof part === 'string') {
                pending += part;
                continue;
            }

            this.conversation.write(`${pending}{${String(part.length)}}\r\n`);

            const continuation = await this.conversation.line();

            if (!continuation.startsWith('+')) {
                throw new Error(`The mail server refused a literal: ${continuation}`);
            }

            this.conversation.write(part);
            pending = '';
        }

        this.conversation.write(`${pending}\r\n`);

        const untagged: string[] = [];

        for (;;) {
            const line = await this.conversation.line();

            if (line.startsWith(`${tag} `)) {
                if (!line.startsWith(`${tag} OK`)) {
                    // The verb alone: the rest of a command can be the mailbox's credential.
                    const verb = typeof parts[0] === 'string' ? (parts[0].split(' ')[0] ?? '') : 'a literal';

                    throw new Error(`The mail server refused ${verb}: ${line}`);
                }

                return untagged;
            }

            untagged.push(line);
        }
    }
}

/** Signs in to the mailbox, runs one reading, and leaves, whatever the reading did. */
async function reading<T>(read: (session: ImapSession) => Promise<T>): Promise<T> {
    const conversation = new Conversation(mailServer.imapPort);
    const session = new ImapSession(conversation);

    try {
        const greeting = await conversation.line();

        if (!greeting.startsWith('* OK')) {
            throw new Error(`The mail server did not greet the connection: ${greeting}`);
        }

        await session.command(`LOGIN ${quoted(mailServer.login)} ${quoted(mailServer.password)}`);

        const answer = await read(session);

        await session.command('LOGOUT');

        return answer;
    } finally {
        conversation.close();
    }
}

/** A name the server wrote quoted, or as an atom. */
function unquoted(written: string): string {
    return written.startsWith('"') ? written.slice(1, -1).replaceAll(/\\(.)/gu, '$1') : written;
}

/**
 * Every folder the mailbox holds, by the name the server gives it.
 *
 * A level is written with the server's own separator, which is `.` on the server this run starts — so a folder made
 * inside `Archive` is `Archive.Suppliers` here and `Archive / Suppliers` on the screen.
 */
export async function foldersOnTheMailServer(): Promise<string[]> {
    return reading(async (session) => {
        const listed = await session.command('LIST "" "*"');

        return listed.flatMap((line) => {
            const listing = /^\* LIST \([^)]*\) (?:NIL|"(?:[^"\\]|\\.)*") (.+)$/u.exec(line);

            return listing === null ? [] : [unquoted(listing[1] ?? '')];
        });
    });
}

/** How many messages of one folder the mail server holds without the seen flag — the count a person reads as unread. */
export async function unseenIn(folder: string): Promise<number> {
    return reading(async (session) => {
        const [status = ''] = await session.command(`STATUS ${quoted(folder)} (UNSEEN)`);
        const unseen = /\bUNSEEN (\d+)/u.exec(status);

        if (unseen === null) {
            throw new Error(`The mail server reported no unseen count for ${folder}: ${status}`);
        }

        return Number(unseen[1]);
    });
}

/**
 * Every message of one folder whose subject holds the words given, with its flags, oldest first.
 *
 * The subject is matched the way the protocol matches one, as a substring without regard to case. A folder the mailbox
 * does not hold answers nothing rather than failing, because a folder gone is an answer a spec asks for.
 */
export async function messagesIn(folder: string, subject: string): Promise<StoredMessage[]> {
    return reading(async (session) => {
        const listed = await session.command(`LIST "" ${quoted(folder)}`);

        if (listed.length === 0) {
            return [];
        }

        await session.command(`EXAMINE ${quoted(folder)}`);

        const searched = await session.command('UID SEARCH CHARSET UTF-8 SUBJECT ', Buffer.from(subject, 'utf8'));
        const uids = searched.flatMap((line) =>
            line.startsWith('* SEARCH') ? line.slice('* SEARCH'.length).trim().split(/\s+/u).filter(Boolean) : [],
        );

        if (uids.length === 0) {
            return [];
        }

        const fetched = await session.command(`UID FETCH ${uids.join(',')} (UID FLAGS)`);

        return fetched
            .flatMap((line) => {
                const uid = /\bUID (\d+)/u.exec(line);
                const flags = /\bFLAGS \(([^)]*)\)/u.exec(line);

                return uid === null || flags === null
                    ? []
                    : [{ uid: Number(uid[1]), flags: (flags[1] ?? '').split(' ').filter(Boolean) }];
            })
            .sort((first, second) => first.uid - second.uid);
    });
}

/** Reads one SMTP reply, which may run over several lines, and refuses any code but the one expected. */
async function reply(conversation: Conversation, expected: string): Promise<void> {
    for (;;) {
        const line = await conversation.line();

        if (!line.startsWith(expected)) {
            throw new Error(`The mail server answered ${line} where ${expected} was expected.`);
        }

        if (line.charAt(3) !== '-') {
            return;
        }
    }
}

/** A message a spec delivered, by what names it in the mailbox and what a reply to it would refer to. */
export interface DeliveredMessage {
    readonly subject: string;
    readonly messageId: string;
}

/**
 * Delivers one message into the mailbox over SMTP, from a correspondent nobody else writes as, and answers with what
 * names it.
 *
 * The subject is the caller's words followed by a mark drawn for this delivery, so a subject is never shared with the
 * corpus, with another spec, or with an earlier run against the same mailbox. A message given as `answering` makes this
 * one a reply to it, which is how a spec builds a conversation of its own.
 */
export async function deliver(
    words: string,
    options: { readonly answering?: DeliveredMessage; readonly body?: string } = {},
): Promise<DeliveredMessage> {
    const subject =
        options.answering === undefined ? `${words} ${randomUUID().slice(0, 8)}` : `Re: ${options.answering.subject}`;
    const messageId = `<${randomUUID()}@end-to-end.test>`;
    const threading =
        options.answering === undefined
            ? []
            : [`In-Reply-To: ${options.answering.messageId}`, `References: ${options.answering.messageId}`];
    const message = [
        'From: End-to-end correspondent <correspondent@end-to-end.test>',
        `To: ${mailServer.address}`,
        `Subject: ${subject}`,
        `Message-ID: ${messageId}`,
        ...threading,
        `Date: ${new Date().toUTCString()}`,
        'MIME-Version: 1.0',
        'Content-Type: text/plain; charset=utf-8',
        'Content-Transfer-Encoding: 8bit',
        '',
        options.body ?? `${words}, written by the end-to-end suite.`,
    ].join('\r\n');

    const conversation = new Conversation(mailServer.smtpPort);

    try {
        await reply(conversation, '220');

        for (const [command, expected] of [
            ['EHLO end-to-end.test', '250'],
            ['MAIL FROM:<correspondent@end-to-end.test>', '250'],
            [`RCPT TO:<${mailServer.address}>`, '250'],
            ['DATA', '354'],
        ] as const) {
            conversation.write(`${command}\r\n`);
            await reply(conversation, expected);
        }

        // A line that opens with a dot is doubled, which is the one transformation the protocol asks of a message body.
        conversation.write(Buffer.from(`${message.replaceAll(/^\./gmu, '..')}\r\n.\r\n`, 'utf8'));
        await reply(conversation, '250');

        conversation.write('QUIT\r\n');
        await reply(conversation, '221');
    } finally {
        conversation.close();
    }

    return { subject, messageId };
}
