// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ClientFailureReason, Colleague } from '@mailfathom/client-backend';
import { useContactPhoto } from '../contactBook/contactPhotos';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import { SenderAvatar } from '../controls/SenderAvatar';
import { SkeletonLines, type SkeletonLine } from '../controls/Skeleton';
import type { MessageKey } from '../localization/en';
import { useLocalization, type Translate } from '../localization/useLocalization';
import { PresenceDot } from '../presence/PresenceDot';
import { presenceSaid } from '../presence/presenceWords';
import type { ColleagueStructureInForce } from './useColleagueStructure';

// Where a colleague sits in the organization, as the design draws it: the chain of managers from the top down to the
// open colleague, joined by a stroke, and under it the colleague's direct reports. Every row but the open colleague
// leads to that member's own page; the open colleague is marked instead, and so is the reader, who is nobody's colleague
// to themselves and so has no page here to lead to.
//
// The structure is read apart from the colleague, so the card waits and fails on its own without holding up the rest of
// the page: three ragged lines while it reads, with the wait said out of sight for whoever cannot see them, and a
// warning notice naming why it failed.

const structureWaiting: readonly SkeletonLine[] = [100, 88, 64].map((fills) => ({ fills, height: 'h-2.75' }));

const structureFailures: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'people.structureUnauthenticated',
    unauthorized: 'people.structureUnauthorized',
    unavailable: 'people.structureUnavailable',
    unreadable: 'people.structureUnreadable',
    missing: 'people.structureMissing',
};

export function OrganizationCard({
    colleague,
    reading,
    onOpen,
}: {
    readonly colleague: Colleague;
    readonly reading: ColleagueStructureInForce;

    /** Opens one member of the structure in the open colleague's place. */
    readonly onOpen: (member: Colleague) => void;
}) {
    const { translate } = useLocalization();
    const heading = translate('people.organizationCard');

    return (
        <section
            aria-label={heading}
            className="flex min-w-0 flex-col gap-2 rounded-lg border border-line bg-panel px-4.25 py-3.75"
        >
            <h3 className="text-xs leading-natural font-semibold text-muted">{heading}</h3>

            {reading.reading ? (
                <>
                    <p className="sr-only" role="status">
                        {translate('people.structureReading')}
                    </p>
                    <SkeletonLines lines={structureWaiting} className="gap-2.5" />
                </>
            ) : reading.failure !== null ? (
                <div
                    role="alert"
                    className="flex items-start gap-2.5 rounded-md border border-warning bg-warning-soft px-3.25 py-2.75 text-sm leading-normal"
                >
                    <Icon name="error" className="size-icon-sm shrink-0 text-warning-text" />
                    <div className="flex flex-col items-start gap-2">
                        <span className="text-pretty">{translate(structureFailures[reading.failure])}</span>
                        {reading.failure === 'missing' ? null : (
                            <SecondaryButton
                                label={translate('people.structureAgain')}
                                shape="compact"
                                onActivate={reading.readAgain}
                            />
                        )}
                    </div>
                </div>
            ) : reading.structure === null ? null : (
                <>
                    <ol className="flex flex-col">
                        {[...reading.structure.managers, colleague].map((member) => {
                            const open = member.id === colleague.id;

                            return (
                                <li key={member.id} className="flex flex-col">
                                    <StructureRow
                                        member={member}
                                        open={open}
                                        reader={member.id === reading.structure?.readerId}
                                        onOpen={onOpen}
                                    />
                                    {open ? null : <span aria-hidden="true" className="ms-5.5 h-2.5 w-0.5 bg-line" />}
                                </li>
                            );
                        })}
                    </ol>

                    <h4 className="pt-1.5 text-xs leading-natural font-semibold text-muted">
                        {translate('people.directReports')}
                    </h4>

                    <div className="ms-5.5 border-s-2 border-line ps-5">
                        {reading.structure.directReports.length === 0 ? (
                            <p className="py-2 text-sm text-faint">{translate('people.noDirectReports')}</p>
                        ) : (
                            <ul className="flex flex-col gap-0.5">
                                {reading.structure.directReports.map((member) => (
                                    <li key={member.id}>
                                        <StructureRow
                                            member={member}
                                            open={false}
                                            reader={member.id === reading.structure?.readerId}
                                            onOpen={onOpen}
                                        />
                                    </li>
                                ))}
                            </ul>
                        )}
                    </div>
                </>
            )}
        </section>
    );
}

// The line under a member's name: their title and their presence, or their presence alone where they have no title.
function lineOf(member: Colleague, translate: Translate): string {
    const presence = translate(presenceSaid[member.presence]);

    return member.jobTitle === null ? presence : translate('presence.afterRole', { role: member.jobTitle, presence });
}

// One member of the structure: their portrait with their presence on it, name, title, and presence in words, leading
// to their page unless they are the colleague already open or the reader.
function StructureRow({
    member,
    open,
    reader,
    onOpen,
}: {
    readonly member: Colleague;
    readonly open: boolean;
    readonly reader: boolean;
    readonly onOpen: (member: Colleague) => void;
}) {
    const { translate } = useLocalization();
    const picture = useContactPhoto(member);
    const content = (
        <>
            <span className="relative flex shrink-0">
                <SenderAvatar displayName={member.displayName} address={member.preferredAddress} picture={picture} />
                <PresenceDot presence={member.presence} place="structure" />
            </span>
            <span className="flex min-w-0 flex-1 flex-col gap-px text-start">
                <span className="truncate text-md leading-natural font-semibold">{member.displayName}</span>
                <span className="truncate text-sm leading-natural text-muted">{lineOf(member, translate)}</span>
            </span>
        </>
    );
    // Every row carries the leading edge, transparent where nothing is marked, so the open one lines up with the rest.
    const shape = 'flex min-h-12.5 items-center gap-2.75 rounded-md border-s-3 py-1.25 pe-2 ps-1.25';

    if (open || reader) {
        return (
            <div
                aria-current={open ? 'true' : undefined}
                className={`${shape} ${open ? 'border-s-accent bg-accent-soft' : 'border-s-transparent'}`}
            >
                {content}
            </div>
        );
    }

    return (
        <button
            type="button"
            className={`${shape} w-full border-s-transparent hover:bg-hover`}
            onClick={() => {
                onOpen(member);
            }}
        >
            {content}
            <Icon name="chevron_right" className="size-icon-sm text-faint" />
        </button>
    );
}
