// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useId, useRef, useState, type ReactNode } from 'react';
import type { AnswerBlock } from '@mailfathom/client-backend';
import { namedZone, sendTimeAt } from '../../composer/sendLater';
import { useSendTimeWording } from '../../composer/useSendTimeWording';
import { useCoarsePointer } from '../../shell/useWideWorkspace';
import { Icon } from '../../controls/Icon';
import { FoldedFooter } from '../../footers/FoldedFooter';
import { useFooterOf } from '../../footers/footersInForce';
import { useLocalization } from '../../localization/useLocalization';
import { useReadingZone } from '../../localization/useReadingZone';
import { PassageRewriteBar } from '../../rewriting/PassageRewriteBar';
import { RewriteNote } from '../../rewriting/RewriteNote';
import { usePassageRewriter, useRewriteDock } from '../../rewriting/passageRewriting';
import { linePoint, passagesTouched, withPassagesRewritten, type TouchedPassage } from '../../rewriting/passages';
import { usePassageChange, type PassageChange, type RewriteHow } from '../../rewriting/usePassageChange';
import { useSelectedPassage } from '../../rewriting/useSelectedPassage';
import { UnrecognisedAnswerBlock } from '../AnswerBlockCard';
import { ProposalCard, type ProposalControl } from '../ProposalCard';
import { useProposalAnswering } from '../proposalAnswering';
import { Citation } from './Citation';
import { SupportVerdictChip } from './StandingChips';
import { draftDispositions } from './blockWording';

// Text to be sent, where the answer to a question is a message rather than a fact about one. Everything about how this
// is drawn follows from one property of the contract: a plan carries a draft and never an act, and the set the
// disposition is closed over holds no member meaning sent.
//
// **Nothing sends it, and the card says so where the text is.** The chip above the draft states what has become of it
// locally — composed here, written into the drafts folder, waiting in the outbox — and each of those sentences says that
// nothing left. A reader looking at a message addressed to somebody else is owed that beside the text rather than in a
// legend, and a control that sent it would belong to the surface that governs sending rather than to a block of an
// answer.
//
// **It is editable, and the edit stays on this screen.** The draft is somebody's to correct before they put their name
// to it, so the body opens in a text area on request; saving it anywhere is the mail surface's and is stated under the
// area rather than implied by an absent control. That is what the design draws too — its own editing state swaps
// the paragraphs for a text area.
//
// **In a conversation it is a proposal, and sending is how it is accepted.** The conversation answers it with *Send*,
// *Edit here* and *Discard*, and the service sends what it proposed — which is the whole of what accepting one carries.
// So an edit that changed the text holds *Send* rather than sending the text the reader moved away from, and says why
// beside the field: sending what somebody typed needs the route to carry it, which it does not yet.
//
// **Part of it is rewritten where it stands.** Selecting text in a pending draft nobody is editing by hand raises the
// AI button, and the rewrite comes back as the old sentences struck beside the new ones, with *Keep the change*, *Try
// again* and *Undo* in place of *Send* until one of them is pressed — nothing is sent while a rewrite waits. A kept
// rewrite is an edit like any other, so it holds *Send* for the reason an edit does.
//
// **A draft the person asked to leave later says when, as the composer does once a time is chosen.** The time is the
// proposal's, so nothing here clears or moves it: asking the Agent again is how it changes, and *Edit here* keeps it.
//
// **The footer its account adds stands under the letter, folded to one line**, so it does not lengthen the block and
// the closing can still be checked before *Send* is pressed. A discarded draft draws none, being nothing that will go.
//
// Everything quoted out of the draft carries an empty `lang`, because the contract states no language for it and the
// conversation around it is in the reader's own: the agent writes a reply in the language of the thread it answers.

/**
 * Who a draft is addressed to, as one list in the reader's own language.
 *
 * Addresses rather than names, because an address is the whole of what the contract publishes here — and it is also what
 * somebody about to put their name to a message is checking, a name alone being exactly what hides a reply addressed to
 * the wrong one of two people called the same thing. `Intl` joins them, so which separator a language uses and what it
 * puts before the last one are the locale's answers rather than a comma written here.
 */
function addressedTo(recipients: readonly string[], locale: string): string {
    return new Intl.ListFormat(locale).format(recipients);
}

/** Where a place in the document stands in the letter's text, which is the one text node the letter holds. */
function placeIn(letter: HTMLElement | null, node: Node, offset: number): number | null {
    if (!letter?.contains(node)) {
        return null;
    }

    const before = document.createRange();
    before.setStart(letter, 0);
    before.setEnd(node, offset);

    return before.toString().length;
}

/**
 * The letter while a rewrite is in hand: each passage pulsing while it is written, then struck beside what came back.
 * The struck words are a `del` and the new ones an `ins`, so what changed is said by the markup as well as the colour.
 */
function changedLetter(body: string, change: PassageChange): ReactNode[] {
    const lines = body.split('\n');
    const lineStart = (line: number) => lines.slice(0, line).reduce((total, text) => total + text.length + 1, 0);
    const drawn: ReactNode[] = [];
    let at = 0;

    change.passages.forEach((passage, index) => {
        const start = lineStart(passage.paragraph) + passage.start;
        const end = lineStart(passage.paragraph) + passage.end;
        const rewritten = change.rewritten?.[index];

        drawn.push(body.slice(at, start));
        drawn.push(
            rewritten === undefined ? (
                <mark
                    key={start}
                    className="rounded-xs bg-accent-soft text-inherit animate-working motion-reduce:animate-none"
                >
                    {body.slice(start, end)}
                </mark>
            ) : (
                <span key={start}>
                    <del className="text-muted decoration-faint">{body.slice(start, end)}</del>{' '}
                    <ins className="rounded-xs bg-accent-soft px-px text-text no-underline shadow-rewritten">
                        {rewritten}
                    </ins>
                </span>
            ),
        );
        at = end;
    });

    drawn.push(body.slice(at));

    return drawn;
}

export function Draft({ block }: { readonly block: AnswerBlock }) {
    const { locale, translate } = useLocalization();
    const answering = useProposalAnswering();
    const written = useId();
    const zone = useReadingZone();
    const [shownAt] = useState(() => Date.now());
    const wordSendTime = useSendTimeWording(shownAt, zone);

    // The edit is this block's own state and nothing outside it reads one, which is why it is held here rather than
    // lifted: it is what somebody is typing, not a copy of anything the deployment holds.
    //
    // Two pieces rather than one, because closing the editor and discarding what was typed are two different acts and
    // only one of them was asked for: *Stop editing* puts the text back as a paragraph, and what it says is still what
    // the reader wrote. Nothing here throws away an edit, which is what the sentence under the area promises.
    const [editing, setEditing] = useState(false);
    const [typed, setTyped] = useState<string | null>(null);
    const footer = useFooterOf(block.type === 'draft' ? block.draft.account : null);
    const rewriter = usePassageRewriter();
    const dock = useRewriteDock();
    const letter = useRef<HTMLParagraphElement>(null);
    const rewriting = usePassageChange(rewriter);
    const rewriteAsked = useRef(0);
    // The text a rewrite left the letter holding, so the meta and the toast's *Undo* each ask whether it still does.
    const [keptAs, setKeptAs] = useState<string | null>(null);
    const coarse = useCoarsePointer();
    const change = rewriting.change;

    // A draft's letter is a place to select from only while the proposal waits, nobody is editing it by hand, and no
    // rewrite is already in hand — and only where the deployment rewrites at all.
    const rewritable = rewriter !== null && answering?.phase === 'pending' && !editing && change === null;
    const selection = useSelectedPassage((node, offset) => placeIn(letter.current, node, offset), rewritable, coarse);

    if (block.type !== 'draft') {
        return <UnrecognisedAnswerBlock />;
    }

    const { draft, evidence } = block;
    const body = typed ?? draft.body;
    const to = addressedTo(draft.recipients, locale);
    const changed = typed !== null && typed !== draft.body;
    const leaves = draft.dueAt === null ? null : wordSendTime(sendTimeAt(Date.parse(draft.dueAt), zone));
    const sendSaid = leaves === null ? translate('draft.send') : translate('draft.sendAt', { when: leaves.short });

    function rewrite(how: RewriteHow): void {
        const selected = selection.selected;

        if (selected === null) {
            return;
        }

        const lines = body.split('\n');
        const passages = passagesTouched(lines, linePoint(body, selected.from), linePoint(body, selected.to));

        selection.close();

        if (passages.length > 0) {
            rewriteParts(lines, passages, how, false);
        }
    }

    /** Asks for a rewrite, and gives the focus back to the card where nothing comes of it, since the note had it. */
    function rewriteParts(
        lines: readonly string[],
        passages: readonly TouchedPassage[],
        how: RewriteHow,
        again: boolean,
    ): void {
        const ask = ++rewriteAsked.current;

        void rewriting.rewrite(lines, passages, how, again).then((rewritten) => {
            if (rewritten === null && ask === rewriteAsked.current) {
                letter.current?.closest('article')?.focus();
            }
        });
    }

    function keepTheChange(kept: PassageChange): void {
        const before = typed;
        const after = withPassagesRewritten(body.split('\n'), kept.passages, kept.rewritten ?? []).join('\n');

        setTyped(after);
        setKeptAs(after);
        rewriting.settle();
        letter.current?.closest('article')?.focus();
        // An edit made since the keep is somebody's own words, which *Undo* of the rewrite does not reach.
        rewriting.sayKept(kept, false, () => {
            setTyped((current) => (current === after ? before : current));
        });
    }

    const controls: readonly ProposalControl[] =
        answering === null
            ? []
            : change !== null
              ? change.rewritten === null
                  ? []
                  : [
                        {
                            said: translate('rewrite.keep'),
                            name: translate('rewrite.keepDraftName', { to }),
                            primary: true,
                            run: () => {
                                keepTheChange(change);
                            },
                        },
                        {
                            said: translate('rewrite.again'),
                            name: translate('rewrite.againName'),
                            run: () => {
                                rewriteParts(body.split('\n'), change.passages, rewriting.howAgain(change), true);
                            },
                        },
                        {
                            said: translate('rewrite.undo'),
                            name: translate('rewrite.undoDraftName'),
                            run: () => {
                                rewriting.settle();
                                letter.current?.closest('article')?.focus();
                            },
                        },
                    ]
              : editing
                ? [
                      {
                          said: sendSaid,
                          name:
                              leaves === null
                                  ? translate('draft.sendEditedName', { to })
                                  : translate('draft.sendEditedAtName', { to, when: leaves.long }),
                          primary: true,
                          held: changed,
                          run: () => {
                              answering.answer('accepted');
                          },
                      },
                      {
                          said: translate('draft.stopEditing'),
                          name: translate('draft.stopEditingName', { to }),
                          run: () => {
                              setEditing(false);
                          },
                      },
                  ]
                : [
                      {
                          said: sendSaid,
                          name:
                              leaves === null
                                  ? translate('draft.sendName', { to })
                                  : translate('draft.sendAtName', { to, when: leaves.long }),
                          primary: true,
                          held: changed,
                          run: () => {
                              answering.answer('accepted');
                          },
                      },
                      {
                          said: translate('draft.editHere'),
                          name: translate('draft.editHereName', { to }),
                          run: () => {
                              setEditing(true);
                          },
                      },
                      {
                          said: translate('draft.discard'),
                          name: translate('draft.discardName', { to }),
                          run: () => {
                              answering.answer('declined');
                          },
                      },
                  ];

    // The field is open only while the proposal can still be answered: a draft already sent or discarded keeps the
    // text the reader last saw as a paragraph rather than inviting an edit nothing could use.
    const editable = answering === null || answering.phase === 'pending';

    return (
        <ProposalCard
            citations={evidence.citations}
            controls={controls}
            kind="draft"
            label={translate('draft.label')}
            meta={keptAs !== null && typed === keptAs ? translate('draft.rewritten') : undefined}
            quotesMail
            title={draft.subject}
            trayNote={
                change === null ? undefined : (
                    <RewriteNote said={rewriting.noteOf(change)} busy={change.rewritten === null} />
                )
            }
            outcome={
                leaves === null
                    ? undefined
                    : {
                          done: translate('draft.scheduled', { when: leaves.long }),
                          failed: translate('draft.notScheduled'),
                      }
            }
        >
            <div className="flex flex-wrap items-center gap-2">
                {/* Once sent, the result line says what became of it, and a disposition saying nothing left would
                    contradict it. */}
                {answering?.phase === 'accepted' ? null : (
                    <span className="rounded-full bg-rail px-2.25 py-0.5 text-xs text-muted">
                        {translate(draftDispositions[draft.disposition])}
                    </span>
                )}

                <SupportVerdictChip support={evidence.support} />
            </div>

            <div className="flex flex-col gap-2 rounded-md border border-line bg-sunken px-3.75 py-3.25">
                <p className="flex flex-wrap gap-2 text-sm">
                    <span className="text-muted">{translate('draft.to')}</span>

                    <span lang="">{to}</span>
                </p>

                <p className="flex flex-wrap gap-2 text-sm">
                    <span className="text-muted">{translate('draft.subject')}</span>

                    <span className="font-semibold" lang="">
                        {draft.subject}
                    </span>
                </p>

                {editing && editable ? (
                    <>
                        <label className="text-xs font-semibold text-muted" htmlFor={written}>
                            {translate('draft.bodyLabel')}
                        </label>

                        {/* Focus goes where it was asked for: the control that opened the area is after it in the
                            document, so a reader who did not get the caret would have to come back past every citation
                            the draft carries. */}
                        <textarea
                            className="min-h-32 w-full rounded-md border border-line bg-panel px-2.75 py-2 text-sm leading-relaxed focus-visible:border-accent"
                            id={written}
                            lang=""
                            autoFocus
                            value={body}
                            onChange={(event) => {
                                setTyped(event.target.value);
                            }}
                        />
                    </>
                ) : (
                    <p
                        ref={letter}
                        className="text-sm leading-relaxed whitespace-pre-line text-text-soft text-pretty"
                        lang=""
                    >
                        {change === null ? body : changedLetter(body, change)}
                    </p>
                )}

                {/* The sentence stays with the text it is about rather than with the text area: once the editor closes,
                    the paragraph above draws what the reader wrote under a chip saying what became of the draft, and
                    nothing else would say that the text on the screen is not the text the deployment holds. */}
                {answering === null && (editing || typed !== null) ? (
                    <p className="text-xs text-faint text-pretty">{translate('draft.editKept')}</p>
                ) : null}

                {answering !== null && editable && changed ? (
                    <p className="text-xs text-faint text-pretty">{translate('draft.editedUnsendable')}</p>
                ) : null}

                {rewritable ? (
                    <p className="flex items-center gap-1.75 text-sm text-muted">
                        <Icon className="size-icon-xs text-faint" name="ink_highlighter" />
                        {translate('rewrite.hint')}
                    </p>
                ) : null}

                {selection.selected === null ? null : (
                    <PassageRewriteBar
                        selected={selection.selected}
                        open={selection.open}
                        inMessage={false}
                        dock={dock}
                        onOpen={selection.openBar}
                        onClose={() => {
                            selection.close();
                            letter.current?.closest('article')?.focus();
                        }}
                        onRewrite={rewrite}
                    />
                )}

                {evidence.citations.length === 0 ? null : (
                    <p className="flex flex-wrap items-center gap-1.5">
                        {evidence.citations.map((source, at) => (
                            <Citation key={source} position={at + 1} source={source} />
                        ))}
                    </p>
                )}
            </div>

            {footer === null || answering?.phase === 'declined' ? null : <FoldedFooter blocks={footer.blocks} />}

            {/* Gone once sent, where the result line names the time instead. */}
            {leaves === null || answering?.phase === 'accepted' ? null : (
                <p className="flex items-start gap-2 rounded-md border border-accent-line bg-accent-soft px-2.75 py-2 text-base text-text text-pretty pointer-coarse:px-3 pointer-coarse:py-2.5">
                    <Icon className="size-icon-sm shrink-0 text-accent-deep" name="schedule_send" />
                    <span className="flex min-w-0 flex-1 flex-wrap items-baseline gap-x-2 gap-y-0.5">
                        <span>{translate('draft.leavesAt', { when: leaves.long })}</span>
                        <span className="text-xs text-muted">
                            {translate('draft.leavesInZone', { zone: namedZone(zone) })}
                        </span>
                    </span>
                </p>
            )}

            {answering === null ? (
                <button
                    className="self-start rounded-md border border-line-strong px-3 py-1.75 text-sm text-text-soft transition hover:bg-hover"
                    type="button"
                    onClick={() => {
                        setEditing(!editing);
                    }}
                >
                    {translate(editing ? 'draft.stopEditing' : 'draft.edit')}
                </button>
            ) : null}
        </ProposalCard>
    );
}
