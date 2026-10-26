// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useState, type ReactNode, type RefObject } from 'react';
import {
    isSpam,
    markMailSpam,
    mostMessagesPerMutation,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailFathomTransport,
    type MailFolderDirectory,
    type MailFolderRole,
    type MailSpamMark,
    type MailSpamMarkResult,
    type MailSpamSubcategory,
    type MailSpamVerdict,
} from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import type { ActedMessage, MarkedSpam } from '../mailboxActs/useMailboxActs';
import { useToasts } from '../toasts/useToasts';
import { SpamQuestions, type SpamAsking } from './SpamQuestions';
import { subcategoryLabels } from './spamWords';

// The spam control's half of acting on a mailbox: one toggle over any set of messages, the two questions it can stand in
// front of, the mark itself, and what is said once the deployment answers. Kept beside the other acts rather than among
// them because it is not one of the folder or flag acts the provider performs: it records a verdict, and whether it also
// moves the message is the account's own switch, which the deployment decides and reports per message as `moved`.
//
// What is said afterwards is built from that answer rather than from what was predicted at the press: the prediction
// only decides whether the not-spam question is asked, and a row is drawn from it until the answer corrects it.
//
// A mark is not followed in the pending-changes queue: the verdict is final the moment it is recorded, and what remains
// for the mail server — its keywords and a move — is not yet an act that queue words. Following it there as an act of
// its own is the upgrade, and it is needed once a mark's move can be refused after it was written down.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
};

interface Marks {
    readonly session: ClientSession | null;
    readonly marked: ReadonlyMap<string, MarkedSpam>;
}

interface Asked {
    readonly session: ClientSession;
    readonly question: SpamAsking;
    readonly messages: readonly ActedMessage[];
    readonly onMarked: (() => void) | undefined;
}

/** The person's verdict as this client draws it until the deployment's next read reports the same. */
function ownVerdict(mark: MailSpamMark): MailSpamVerdict {
    const subcategory = mark.verdict === 'Spam' ? mark.spamSubcategory : null;

    return {
        verdict: mark.verdict,
        decidedBy: 'Person',
        spamSubcategory: subcategory,
        spamSubcategoryNamedBy: subcategory === null ? null : 'Person',
        phishingRequest: null,
        impersonatedParty: null,
    };
}

function oneOrOther(locale: string, count: number): 'one' | 'other' {
    return new Intl.PluralRules(locale).select(count) === 'one' ? 'one' : 'other';
}

/** What the spam control holds and does, for the provider to hand on and to draw. */
export interface SpamMarks {
    readonly marked: ReadonlyMap<string, MarkedSpam>;
    readonly toggle: (messages: readonly ActedMessage[], onMarked?: () => void) => void;

    /** Asks for the kind of spam a move into the Spam folder has just marked, over the messages it marked. */
    readonly filedIntoJunk: (messages: readonly ActedMessage[]) => void;
    readonly notSpamMoves: (messages: readonly ActedMessage[]) => boolean;
    readonly questions: ReactNode;
}

export function useSpamMarks({
    session,
    transport,
    signedIn,
    directory,
    folderRoleOf,
    counted,
}: {
    readonly session: ClientSession | null;
    readonly transport: MailFathomTransport;
    readonly signedIn: RefObject<ClientSession | null>;
    readonly directory: MailFolderDirectory | null;
    readonly folderRoleOf: (message: ActedMessage) => MailFolderRole | null;
    readonly counted: (messages: number) => string;
}): SpamMarks {
    const { locale, translate } = useLocalization();
    const toasts = useToasts();
    const [kept, setKept] = useState<Marks>({ session: null, marked: new Map() });
    const [asked, setAsked] = useState<Asked | null>(null);
    const [serial, setSerial] = useState(0);

    // Derived rather than cleared, for the reason the provider gives about its own: signing in as somebody else keeps
    // this mounted, and one person's verdicts must not be drawn over the next person's mail.
    const marked = kept.session === session ? kept.marked : new Map<string, MarkedSpam>();
    const asking = asked !== null && asked.session === session ? asked : null;

    function drawn(message: ActedMessage): MailSpamVerdict | null {
        return marked.get(message.storedEmailId)?.verdict ?? message.spam;
    }

    function filesJunk(message: ActedMessage): boolean {
        return directory?.accounts.find((entry) => entry.account.id === message.account)?.account.filesJunk ?? false;
    }

    function sitsInJunk(message: ActedMessage): boolean {
        const own = marked.get(message.storedEmailId);

        return own?.leaves === true ? own.verdict.verdict === 'Spam' : folderRoleOf(message) === 'Junk';
    }

    function remember(entries: readonly (readonly [string, MarkedSpam])[]): void {
        setKept((current) => {
            const next = new Map(current.session === session ? current.marked : []);

            for (const [storedEmailId, mark] of entries) {
                next.set(storedEmailId, mark);
            }

            return { session, marked: next };
        });
    }

    /**
     * Replaces the marks one press drew with what its answer made of them — the corrected mark, or the one that stood
     * before the press — wherever that press's mark still stands. A later press has drawn a newer mark over an older
     * answer, and the older answer must not put back what the person has since changed.
     */
    function settle(entries: readonly (readonly [string, MarkedSpam, MarkedSpam | undefined])[]): void {
        setKept((current) => {
            const next = new Map(current.marked);

            for (const [storedEmailId, pressed, settled] of entries) {
                if (next.get(storedEmailId) !== pressed) {
                    continue;
                }

                if (settled === undefined) {
                    next.delete(storedEmailId);
                } else {
                    next.set(storedEmailId, settled);
                }
            }

            return { ...current, marked: next };
        });
    }

    function ask(question: SpamAsking, messages: readonly ActedMessage[], onMarked?: () => void): void {
        if (session === null) {
            return;
        }

        setSerial(question.serial);
        setAsked({ session, question, messages, onMarked });
    }

    /** What the toast says once the deployment answered a spam mark, from what it moved rather than from a guess. */
    function spamSaid(
        subcategory: MailSpamSubcategory | null,
        written: readonly ActedMessage[],
        moved: number,
        naming: boolean,
    ): string {
        const form = oneOrOther(locale, written.length);
        const outside = naming ? 0 : written.filter((message) => folderRoleOf(message) !== 'Junk').length;
        const kind =
            subcategory === null
                ? null
                : translate('spam.kindSaid', { kind: translate(subcategoryLabels[subcategory]) });

        const where =
            outside === 0
                ? null
                : moved === outside
                  ? translate(`spam.markedInSpam.${form}`)
                  : moved > 0
                    ? translate('spam.markedSomeMoved', { count: new Intl.NumberFormat(locale).format(moved) })
                    : translate(`spam.markedStays.${form}`);

        return [kind, where].filter((said) => said !== null).join(' ');
    }

    function notSpamSaid(written: readonly ActedMessage[], moved: number): string {
        const form = oneOrOther(locale, written.length);
        const leftInJunk = written.filter((message) => sitsInJunk(message)).length - moved;

        if (moved === 0) {
            return leftInJunk > 0
                ? translate(`spam.notSpamLeftInSpam.${form}`)
                : translate(`spam.notSpamStays.${form}`);
        }

        return moved === written.length
            ? translate(`spam.notSpamInInbox.${form}`)
            : translate('spam.notSpamSomeMoved', { count: new Intl.NumberFormat(locale).format(moved) });
    }

    /**
     * Records the person's verdict over the messages, drawing it from the press and correcting it from the answer.
     *
     * @param naming Whether a move into the Spam folder already marked them, which leaves only the kind to record.
     */
    function mark(messages: readonly ActedMessage[], spamMark: MailSpamMark, naming: boolean): void {
        if (session === null || messages.length === 0) {
            return;
        }

        const asking = session;
        const verdict = ownVerdict(spamMark);

        // Drawn from the press: a mark moves a message only on an account whose switch files junk, spam out of every
        // other folder and not spam out of the Spam folder. What stood before is kept, for a mark that is not written.
        const before = new Map(messages.map((message) => [message.storedEmailId, marked.get(message.storedEmailId)]));
        const pressed = new Map<string, MarkedSpam>(
            messages.map((message) => [
                message.storedEmailId,
                {
                    verdict,
                    from: message.folder,
                    leaves:
                        !naming &&
                        filesJunk(message) &&
                        (spamMark.verdict === 'Spam' ? !sitsInJunk(message) : sitsInJunk(message)),
                },
            ]),
        );

        remember([...pressed]);

        const batches: Promise<{
            readonly messages: readonly ActedMessage[];
            readonly answer: ClientResult<readonly MailSpamMarkResult[]>;
        }>[] = [];

        for (let from = 0; from < messages.length; from += mostMessagesPerMutation) {
            const batch = messages.slice(from, from + mostMessagesPerMutation);

            batches.push(
                markMailSpam(
                    asking,
                    transport,
                    spamMark,
                    batch.map((message) => message.storedEmailId),
                ).then((answer) => ({ messages: batch, answer })),
            );
        }

        void Promise.all(batches).then((answered) => {
            if (signedIn.current !== asking) {
                return;
            }

            const results = new Map(
                answered.flatMap(({ answer }) =>
                    answer.outcome === 'read'
                        ? answer.value
                              .filter((result) => result.outcome === 'recorded' || result.outcome === 'applied')
                              .map((result) => [result.storedEmailId, result] as const)
                        : [],
                ),
            );

            const written = messages.filter((message) => results.has(message.storedEmailId));
            const notWritten = messages.filter((message) => !results.has(message.storedEmailId));
            const moved = written.filter((message) => results.get(message.storedEmailId)?.moved === true);

            settle(
                messages.flatMap((message) => {
                    const drawnAtPress = pressed.get(message.storedEmailId);
                    const result = results.get(message.storedEmailId);

                    if (drawnAtPress === undefined) {
                        return [];
                    }

                    return [
                        [
                            message.storedEmailId,
                            drawnAtPress,
                            result === undefined
                                ? before.get(message.storedEmailId)
                                : { verdict, from: message.folder, leaves: result.moved },
                        ] as const,
                    ];
                }),
            );

            if (notWritten.length > 0) {
                const failure = answered.find(({ answer }) => answer.outcome === 'failed')?.answer;

                toasts.raise({
                    kind: 'error',
                    title: translate(written.length === 0 ? 'spam.nothingMarked' : 'spam.notAllMarked'),
                    body:
                        failure?.outcome === 'failed'
                            ? translate(failureLabels[failure.failure.reason])
                            : counted(notWritten.length),
                });
            }

            if (written.length === 0) {
                return;
            }

            toasts.raise(
                spamMark.verdict === 'Spam'
                    ? {
                          kind: 'success',
                          title: translate(naming ? 'spam.kindSetTitle' : 'spam.markedTitle', {
                              count: counted(written.length),
                          }),
                          body: spamSaid(spamMark.spamSubcategory, written, moved.length, naming),
                      }
                    : {
                          kind: 'success',
                          title: translate('spam.markedNotSpamTitle', { count: counted(written.length) }),
                          body: notSpamSaid(written, moved.length),
                      },
            );
        });
    }

    function notSpamMoves(messages: readonly ActedMessage[]): boolean {
        return messages.some((message) => sitsInJunk(message) && filesJunk(message));
    }

    function toggle(messages: readonly ActedMessage[], onMarked?: () => void): void {
        if (session === null || messages.length === 0) {
            return;
        }

        const spam = messages.map((message) => isSpam(drawn(message)));

        // A mix is refused with nothing sent: one control cannot mean both directions at once, and guessing which the
        // person meant would mark somebody's real mail spam.
        if (spam.some(Boolean) && !spam.every(Boolean)) {
            toasts.raise({ kind: 'error', title: translate('spam.nothingMarked'), body: translate('spam.mixed') });

            return;
        }

        if (spam.every(Boolean)) {
            const moving = messages.filter((message) => notSpamMoves([message])).length;

            // Confirmed only where it moves something, which is the one consequence somebody has to know about first.
            if (moving > 0) {
                ask({ kind: 'notSpam', serial: serial + 1, messages: messages.length, moving }, messages, onMarked);
            } else {
                mark(messages, { verdict: 'NotSpam' }, false);
                onMarked?.();
            }

            return;
        }

        const outside = messages.filter((message) => !sitsInJunk(message));

        ask(
            {
                kind: 'spam',
                serial: serial + 1,
                messages: messages.length,
                outside: outside.length,
                movers: outside.filter(filesJunk).length,
                moved: false,
            },
            messages,
            onMarked,
        );
    }

    /**
     * Draws the mark a move into the Spam folder recorded, and asks for the kind it did not name.
     *
     * The deployment records the mark with the move itself, so a message that was not spam is drawn spam from here on
     * and the chooser asks only for the kind; closing it leaves the move and the mark as they are. The move is what
     * takes the row out of its list, so the mark leaves nothing of its own.
     */
    function filedIntoJunk(messages: readonly ActedMessage[]): void {
        const marking = messages.filter((message) => !isSpam(drawn(message)));

        if (marking.length === 0) {
            return;
        }

        const verdict = ownVerdict({ verdict: 'Spam', spamSubcategory: null });

        remember(marking.map((message) => [message.storedEmailId, { verdict, from: message.folder, leaves: false }]));
        ask(
            { kind: 'spam', serial: serial + 1, messages: marking.length, outside: 0, movers: 0, moved: true },
            marking,
        );
    }

    const questions = (
        <SpamQuestions
            asking={asking?.question ?? null}
            onSpam={(subcategory) => {
                if (asking !== null && asking.question.kind === 'spam') {
                    mark(asking.messages, { verdict: 'Spam', spamSubcategory: subcategory }, asking.question.moved);
                    asking.onMarked?.();
                }
            }}
            onNotSpam={() => {
                if (asking !== null) {
                    mark(asking.messages, { verdict: 'NotSpam' }, false);
                    asking.onMarked?.();
                }
            }}
        />
    );

    return { marked, toggle, filedIntoJunk, notSpamMoves, questions };
}
