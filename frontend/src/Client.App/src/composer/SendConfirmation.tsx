// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { type RefObject } from 'react';
import { Confirmation } from '../confirmation/Confirmation';
import { useTip } from '../controls/useTip';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { whatWouldBeMissing, type Composition, type SendCaution } from './composition';

// Sending, and the question in front of it. Nothing is sent without this: the commonest irreversible mistake in mail is
// not the words, it is who received them, so the confirmation is where every address is read one last time — the blind
// copies included, those being the ones a header row never shows back.
//
// The question itself is `confirmation/Confirmation.tsx`, which every act that leaves the deployment is asked through;
// what stays here is the send's own half of it — which headers are worth saying, what the message would go out
// without, and that it can be taken back for as long as the deployment has not handed it on. The control and the
// question belong in one component for the reason `TabStrip.tsx` gives about the same pairing: the dialog is the
// platform's own, so whether it is open is the element's state rather than a second copy of it, and leaving it either
// way puts focus back on the control that opened it without anything here remembering which.

// What a send would go out without, each said as a sentence rather than as a field name. Exhaustive by its own type, so
// a caution added to the composition fails to compile until it has words.
const cautionSaid: Readonly<Record<SendCaution, MessageKey>> = {
    noRecipient: 'compose.cautionNoRecipient',
    noSubject: 'compose.cautionNoSubject',
    noWords: 'compose.cautionNoWords',
};

export function SendConfirmation({
    asked,
    composition,
    draftUnaccepted,
    disabled,
    refused = null,
    onSend,
}: {
    /**
     * The dialog itself, held by the composer so that the keyboard shortcut it draws reaches the same question.
     *
     * The element is the state, which is what the note above is about: two ways to ask are still one dialog, and
     * neither this component nor the composer holds a second copy of whether it is open.
     */
    readonly asked: RefObject<HTMLDialogElement | null>;

    readonly composition: Composition;

    /**
     * Whether the words are a draft the deployment wrote that nobody has accepted yet.
     *
     * A caution beside the three the composition itself produces, and the one that is not about something missing:
     * what would go out is a message somebody else composed, and the moment before it leaves is the moment to notice.
     * Accepting the draft in the composer is what clears it.
     */
    readonly draftUnaccepted: boolean;

    /** Whether the message is already on its way, which is what keeps one press from queueing two. */
    readonly disabled: boolean;

    /**
     * Why sending cannot be asked for now, where it cannot — no connection to the server. The control stays focusable
     * and says so on a hover, a focus, or a tap, rather than being a button that does nothing and says nothing.
     */
    readonly refused?: string | null;

    readonly onSend: () => void;
}) {
    const { locale, translate } = useLocalization();
    const refusal = useTip({ says: refused ?? translate('compose.send'), describes: true, onPress: () => undefined });
    const missing = whatWouldBeMissing(composition);

    const cautions = [
        ...missing.map((caution) => translate(cautionSaid[caution])),
        ...(draftUnaccepted ? [translate('compose.cautionUnreadDraft')] : []),
    ];
    const addresses = new Intl.ListFormat(locale, { style: 'long', type: 'conjunction' });

    // Each header is said only where somebody is written in it, because a confirmation that lists two empty headers is
    // one a reader learns to skim — and skimming is exactly what this exists to stop.
    function headers(): readonly { readonly key: MessageKey; readonly written: readonly string[] }[] {
        return [
            { key: 'compose.confirmTo' as const, written: composition.to },
            { key: 'compose.confirmCc' as const, written: composition.cc },
            { key: 'compose.confirmBcc' as const, written: composition.bcc },
        ].filter((header) => header.written.length > 0);
    }

    return (
        <>
            <button
                {...(refused === null
                    ? {
                          onClick: () => {
                              asked.current?.showModal();
                          },
                      }
                    : refusal.anchor)}
                type="button"
                disabled={disabled && refused === null}
                aria-disabled={refused === null ? undefined : true}
                className="flex items-center rounded-md bg-accent px-4.5 py-2.25 text-base font-semibold text-on-accent transition hover:opacity-90 disabled:opacity-60 aria-disabled:cursor-not-allowed aria-disabled:opacity-60 pointer-coarse:px-5 pointer-coarse:text-md"
            >
                {translate('compose.send')}
            </button>
            {refused === null ? null : refusal.tip}

            <Confirmation
                asked={asked}
                mark="send"
                question={translate('compose.confirmQuestion')}
                cautions={cautions}
                reversal={{ kind: 'recallable', said: translate('compose.confirmRecallable') }}
                ways={[
                    { said: translate('compose.backToEditing'), manner: 'back' },
                    {
                        said: translate(cautions.length === 0 ? 'compose.send' : 'compose.sendAnyway'),
                        manner: 'act',
                        run: onSend,
                    },
                ]}
                consequence={
                    <>
                        {headers().length === 0 ? (
                            <p className="text-base text-muted">{translate('compose.confirmNobody')}</p>
                        ) : (
                            headers().map((header) => (
                                <p key={header.key} className="text-base text-muted text-pretty">
                                    {translate(header.key, { addresses: addresses.format(header.written) })}
                                </p>
                            ))
                        )}

                        <p className="text-base text-muted text-pretty">
                            {translate('compose.confirmSubject', {
                                subject:
                                    composition.subject.trim() === ''
                                        ? translate('compose.confirmNoSubject')
                                        : composition.subject,
                            })}
                        </p>
                    </>
                }
            />
        </>
    );
}
