// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useRef, type ReactNode } from 'react';
import type {
    MailBody,
    MailBodyAvailability,
    MailBodyTruncation,
    MailDocument,
    MailDocumentBlock,
    MailDocumentFooter,
    MailDocumentQuotation,
    MailDocumentRefusal,
} from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { FoldedFooter } from '../footers/FoldedFooter';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import type { Locale } from '../localization/locale';
import { AuthorsPresentation } from './authorsPresentation';
import { firstLineOfText } from './firstLineOfText';
import { LinksNotOpened } from './linksNotOpened';
import { EmbeddedMessageMarkup } from './MessageMarkupFrame';
import { MessageBlocks } from './MessageBlocks';
import { splitQuotedHistory } from './quotedHistory';

// One message's body, drawn from the closed document tree the service reduced it to — or, where the reader chose the
// embedded HTML view, from the self-contained representation the same read carries. What this component owns is
// everything around either: whether there is a body at all, whether the reduction refused it and the words are what is
// read instead, what the message asked to load from somebody else's server, and what a bound left out. None of it is
// silent — a fallback nobody is told about reads as a message the sender wrote badly.

// The measure a message's own words are set at, which is the design's body: the step above what the client
// says *about* a message, loosened, and in the softer of the two text tones so the sender's words sit under the head
// rather than beside it. It is stated once here and inherited by every block, which is what makes a message of nothing
// but paragraphs read as plain text and a message full of headings and tables read as the design draws one.
const writtenWords = 'flex flex-col gap-2.75 text-lg leading-relaxed text-pretty text-text-soft';

const availabilityMessages: Readonly<Record<Exclude<MailBodyAvailability, 'Readable'>, MessageKey>> = {
    EncryptedNotReadableLocally: 'body.encryptedNotReadable',
    NotStoredExceededSizeLimit: 'body.notStoredExceededSizeLimit',
    NotStoredAwaitingStorageHeadroom: 'body.notStoredAwaitingStorageHeadroom',
};

const refusalMessages: Readonly<Record<Exclude<MailDocumentRefusal, 'None'>, MessageKey>> = {
    NoHtmlPart: 'body.refusedNoHtmlPart',
    ReductionFailed: 'body.refusedReductionFailed',
    NothingRenderable: 'body.refusedNothingRenderable',
};

// Why the embedded view fell back to the reduced tree, which is said in every case rather than in some of them: a
// message quietly drawn as text under a setting that promised markup reads as the setting having stopped working.
// A bound that cut the representation is one of those cases — half a document in a frame is a message that stops
// mid-sentence with nothing saying so, and the tree beside it is whole.
const markupCutShort: Readonly<Record<Exclude<MailBodyTruncation, 'None'>, MessageKey>> = {
    BodyCharacterLimit: 'body.markupTruncated',
    ReadCharacterBudget: 'body.markupTruncated',
    SensitiveContentScanCeiling: 'body.markupTruncated',
    InlineImageOctetLimit: 'body.markupPicturesTruncated',
};

// What the embedded view draws for this message: the representation where there is a whole one, and otherwise the
// reason it is not being drawn, with the reduced tree underneath it. Exactly one of the two is ever present.
function markupOrWhyNot(body: MailBody): {
    readonly markup: string | null;
    readonly insteadBecause: MessageKey | null;
} {
    const representation = body.selfContainedHtml;

    if (representation === null || representation.text === '') {
        return { markup: null, insteadBecause: 'body.markupAbsent' };
    }

    return representation.truncation === 'None'
        ? { markup: representation.text, insteadBecause: null }
        : { markup: null, insteadBecause: markupCutShort[representation.truncation] };
}

export function MessageBody({
    body,
    asking,
    embeddedHtml = false,
    quotedHistoryOnRequest = false,
    readOnly = false,
    answer = null,
    onShowRemotePictures,
}: {
    readonly body: MailBody;
    readonly asking: boolean;

    /**
     * Whether this message is being drawn as the sender's own markup rather than as the reduced tree.
     *
     * It is what the read actually asked for rather than what the setting says, which is what keeps a message drawn
     * from an answer read before the setting moved from being reported as markup that was never fetched.
     */
    readonly embeddedHtml?: boolean;

    /**
     * Whether the conversation this message quoted is folded away until a reader asks for it.
     *
     * Off where one message is what is being read, because there the quotation is the only context on the screen. On in
     * a thread, where the message it quotes is a row of its own a few lines up and drawing it again is the repetition
     * that makes a long conversation unreadable.
     */
    readonly quotedHistoryOnRequest?: boolean;

    /**
     * Whether the message is marked spam, which reads it in the reduced view whatever view is chosen, with no offer of
     * its remote content, and with links that open nothing.
     */
    readonly readOnly?: boolean;

    /**
     * What the message asks of its reader beside its words — a read receipt — drawn at the top of the body under what
     * it asked to load, as the design places it.
     */
    readonly answer?: ReactNode;

    readonly onShowRemotePictures: () => void;
}) {
    const { translate } = useLocalization();

    // The document that is actually drawn, which is one the reduction did not refuse. Everything else — a refusal, and
    // a body this deployment sent no document for — is the message read as words instead.
    const drawn = body.document?.refusal === 'None' ? body.document : null;

    if (body.availability !== 'Readable') {
        return <p className="text-warning">{translate(availabilityMessages[body.availability])}</p>;
    }

    const embedded = embeddedHtml && !readOnly ? markupOrWhyNot(body) : null;
    const markup = embedded?.markup ?? null;
    const insteadBecause = embedded?.insteadBecause ?? null;

    // What the message asked to load from somebody else's server stands above either rendering rather than only above
    // the reduced one. The ask is part of the read, so it reaches the representation exactly as it reaches the tree —
    // and this is the only place in the client it can be made, which is what would have made a message opened in the
    // embedded view one whose blocked pictures nobody could ever ask for.
    return (
        <div className="flex flex-col gap-4">
            {insteadBecause === null ? null : <p className="text-sm text-muted">{translate(insteadBecause)}</p>}

            {body.document === null || readOnly ? null : (
                <RemoteContent
                    document={body.document}
                    requested={body.remoteImagesRequested}
                    asking={asking}
                    onShow={onShowRemotePictures}
                />
            )}

            {answer}

            {markup !== null ? (
                <EmbeddedMessageMarkup markup={markup} />
            ) : drawn === null ? (
                <ReadAsWords body={body} />
            ) : (
                <article className={writtenWords}>
                    <LinksNotOpened value={readOnly}>
                        <AuthorsPresentation value={drawn.authorIsProven}>
                            <Written
                                blocks={drawn.blocks}
                                footer={drawn.footer}
                                quotation={drawn.quotation}
                                quotedHistoryOnRequest={quotedHistoryOnRequest}
                            />
                        </AuthorsPresentation>
                    </LinksNotOpened>
                    {drawn.truncated ? <p className="text-sm text-muted">{translate('body.truncated')}</p> : null}
                </article>
            )}
        </div>
    );
}

// What the message says, with the footer folded to one line under the words where the document names one, and the
// conversation it quoted under that. The footer always sits above the quotation, so drawing the blocks around it in
// order is what puts the three in the order the design draws them, in a single message and in a conversation. A footer
// with no line of text has nothing for the fold to say, so it is drawn with the words.
function Written({
    blocks,
    footer,
    quotation,
    quotedHistoryOnRequest,
}: {
    readonly blocks: readonly MailDocumentBlock[];
    readonly footer: MailDocumentFooter | null;
    readonly quotation: MailDocumentQuotation | null;
    readonly quotedHistoryOnRequest: boolean;
}) {
    const footerEnd = footer === null ? 0 : footer.firstBlock + footer.blockCount;
    const footerBlocks = footer === null ? [] : blocks.slice(footer.firstBlock, footerEnd);
    const firstLine = firstLineOfText(footerBlocks);

    if (footer === null || firstLine === null) {
        return (
            <WordsAndQuotation
                blocks={blocks}
                namedQuotationAt={quotation?.firstBlock ?? null}
                quotedHistoryOnRequest={quotedHistoryOnRequest}
            />
        );
    }

    return (
        <>
            <MessageBlocks blocks={blocks.slice(0, footer.firstBlock)} />
            <FoldedFooter blocks={footerBlocks} firstLine={firstLine} />
            <WordsAndQuotation
                blocks={blocks.slice(footerEnd)}
                namedQuotationAt={quotation === null ? null : quotation.firstBlock - footerEnd}
                quotedHistoryOnRequest={quotedHistoryOnRequest}
                wordsAbove={footer.firstBlock > 0}
            />
        </>
    );
}

// The words, with the conversation they quoted either under them or one gesture away from them. The disclosure is the
// browser's own rather than one built out of a button and a piece of state, for the reason
// `readingPane/MessageHeaders.tsx` gives: it is operable from the keyboard and announces whether it is open, and it
// costs no code to be so.
function WordsAndQuotation({
    blocks,
    namedQuotationAt,
    quotedHistoryOnRequest,
    wordsAbove = false,
}: {
    readonly blocks: readonly MailDocumentBlock[];
    readonly namedQuotationAt: number | null;
    readonly quotedHistoryOnRequest: boolean;
    readonly wordsAbove?: boolean;
}) {
    const { translate } = useLocalization();
    const written = quotedHistoryOnRequest ? splitQuotedHistory(blocks, wordsAbove, namedQuotationAt) : null;

    if (written === null || written.quotedHistory.length === 0) {
        return <MessageBlocks blocks={blocks} />;
    }

    return (
        <>
            <MessageBlocks blocks={written.contribution} />

            <details className="group">
                <summary className="-ms-1 flex min-h-7 w-fit max-w-full cursor-pointer items-center gap-1 rounded-md ps-1 pe-2.5 text-base leading-natural text-muted transition pointer-coarse:min-h-11 hover:bg-hover hover:text-text-soft [&::-webkit-details-marker]:hidden">
                    <Icon name="chevron_right" className="size-icon-md shrink-0 transition group-open:rotate-90" />
                    <span className="truncate">{written.attribution ?? translate('body.quotedHistory')}</span>
                </summary>

                <div className={`mt-3 ${writtenWords}`}>
                    <AuthorsPresentation value={false}>
                        <MessageBlocks blocks={written.quotedHistory} />
                    </AuthorsPresentation>
                </div>
            </details>
        </>
    );
}

function ReadAsWords({ body }: { readonly body: MailBody }) {
    const { translate } = useLocalization();

    const refusal = body.document?.refusal ?? 'None';

    // A sender who wrote no formatted version is not a refusal a reader is owed a sentence about: plain text is what
    // that message is, and the design draws it as the message rather than as a message with a note over it. The other
    // two are this deployment saying it could not do what it meant to, which is the reader's to know.
    const reason = refusal === 'NoHtmlPart' ? null : refusal === 'None' ? 'body.notReduced' : refusalMessages[refusal];

    return (
        <article className={writtenWords}>
            {reason === null ? null : <p className="text-sm text-muted">{translate(reason)}</p>}
            <p className="whitespace-pre-wrap">{body.plainText.text}</p>
            {body.plainText.truncation === 'None' ? null : (
                <p className="text-sm text-muted">{translate('body.textTruncated')}</p>
            )}
        </article>
    );
}

// What the message asked to load from somebody else's server, and the reader's own answer to it. Asking is a request
// this read carries and nothing on either side writes down: the addresses were removed while the tree was built, so
// there is nothing here to decline, and opening the message again asks again.
//
// The design draws the two states unequally on purpose. What was removed is a card, because it asks something of the
// reader and says what answering costs; what was loaded is one quiet line, because it is a fact about the message and
// nothing more is owed on it. The line takes no focus: it is not a control, and a reader who pressed the button has
// finished with the card rather than landed somewhere new — so it is announced rather than focused.
function RemoteContent({
    document,
    requested,
    asking,
    onShow,
}: {
    readonly document: MailDocument;
    readonly requested: boolean;
    readonly asking: boolean;
    readonly onShow: () => void;
}) {
    const { locale, translate } = useLocalization();

    // The line is no place to land, but the button it replaces is where somebody's focus was, and letting that fall to
    // the document would restart keyboard reading at the top of the page. So it goes to the message the line stands
    // in — the region the reading pane and a conversation each draw a message as — and only where the button was on the
    // screen first: a message read with the pictures already asked for takes focus from nobody.
    const line = useRef<HTMLParagraphElement>(null);
    const asked = useRef(false);

    useEffect(() => {
        if (!requested) {
            asked.current = true;
        } else if (asked.current) {
            line.current?.closest<HTMLElement>('[tabindex="-1"]')?.focus({ preventScroll: true });
        }
    }, [requested]);

    if (requested) {
        return (
            <>
                <p className="flex items-center gap-2 text-sm text-muted" ref={line} role="status">
                    <Icon name="image" className="size-icon-sm shrink-0" />
                    {translate('body.remotePicturesShown')}
                </p>
                <UndrawnPictures undrawn={document.undrawnInlineImageCount} />
            </>
        );
    }

    if (document.removedRemoteReferenceCount === 0) {
        return <UndrawnPictures undrawn={document.undrawnInlineImageCount} />;
    }

    return (
        <aside className="flex flex-col items-start gap-2.25 rounded-lg border border-line bg-sunken px-4 py-3.25">
            <p className="text-md text-pretty text-text">{translate('body.remoteContentRemoved')}</p>
            <p className="text-base text-muted">
                {translate('body.remoteContentRemovedCount', {
                    count: count(locale, document.removedRemoteReferenceCount),
                })}
            </p>
            <p className="text-base text-pretty text-muted">{translate('body.showRemotePicturesReveals')}</p>

            {/* The button stays where it is while the read it started is in flight: what a reader clicked is what
                their focus is on, and a message swapped for a one-line notice would drop that focus to the top of the
                document and move everything below it under their cursor. It is `aria-disabled` rather than `disabled`
                for the same reason — a disabled control is not focusable, so disabling the element somebody has just
                pressed is itself what drops their focus to the body. The handler is what refuses the second press. */}
            <button
                aria-disabled={asking}
                className="rounded-md bg-accent px-3.5 py-1.75 text-base font-semibold text-on-accent transition hover:bg-accent-strong aria-disabled:opacity-60"
                type="button"
                onClick={() => {
                    if (!asking) {
                        onShow();
                    }
                }}
            >
                {translate('body.showRemotePictures')}
            </button>

            {asking ? (
                <p className="text-base text-muted" role="status">
                    {translate('body.remotePicturesLoading')}
                </p>
            ) : null}
            <UndrawnPictures undrawn={document.undrawnInlineImageCount} />
        </aside>
    );
}

function UndrawnPictures({ undrawn }: { readonly undrawn: number }) {
    const { locale, translate } = useLocalization();

    return undrawn === 0 ? null : (
        <p className="text-sm text-muted">
            {translate('body.undrawnPicturesCount', { count: count(locale, undrawn) })}
        </p>
    );
}

// A number a person reads is written by the platform under the active locale rather than by a catalogue, which is the
// same rule a date and a relative time follow.
function count(locale: Locale, value: number): string {
    return new Intl.NumberFormat(locale).format(value);
}
