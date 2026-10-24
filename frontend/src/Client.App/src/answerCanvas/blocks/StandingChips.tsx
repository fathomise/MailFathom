// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { AnswerConfidence, BlockSupport } from '@mailfathom/client-backend';
import type { ReactNode } from 'react';
import { Icon } from '../../controls/Icon';
import { useTip } from '../../controls/useTip';
import { useLocalization } from '../../localization/useLocalization';
import { confidenceBands, supportVerdicts } from './blockWording';

// The chips a block says where it stands with, each of which explains itself. A label such as *moderate confidence*
// or *cited* is a word somebody may not know the meaning of, and the explanation stands one gesture away rather than in
// a legend on another screen. The chip does nothing else, so a tap is what brings the sentence up under a finger;
// `controls/useTip.tsx` is every other way in and every way it goes.

function ExplainedChip({
    label,
    explanation,
    tint,
    children,
}: {
    readonly label: string;
    readonly explanation: string;
    readonly tint: string;
    readonly children?: ReactNode;
}) {
    const { anchor, tip } = useTip({ says: explanation, describes: true });

    return (
        <>
            <button
                {...anchor}
                className={`touch-extended inline-flex w-fit cursor-help items-center gap-1 rounded-full px-2.25 py-0.5 text-xs ${tint}`}
                type="button"
            >
                {children}
                {label}
            </button>
            {tip}
        </>
    );
}

/** What the correspondence does for a block, as a chip that says what that verdict means. */
export function SupportVerdictChip({ support }: { readonly support: BlockSupport }) {
    const { translate } = useLocalization();
    const verdict = supportVerdicts[support];

    return (
        <ExplainedChip explanation={translate(verdict.note)} label={translate(verdict.label)} tint={verdict.tint}>
            <Icon className="size-icon-xs" name={verdict.icon} />
        </ExplainedChip>
    );
}

/** How far an answer is worth trusting, as a chip that says what that band means. */
export function ConfidenceBandChip({ confidence }: { readonly confidence: AnswerConfidence }) {
    const { translate } = useLocalization();
    const band = confidenceBands[confidence];

    return <ExplainedChip explanation={translate(band.note)} label={translate(band.label)} tint={band.tint} />;
}
