// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { standingViews, type StandingView } from '../messageList/listing';
import { useListedMail } from '../messageList/useListedMail';
import { useAiFiltersShown } from '../preferences/aiFilters';
import { useWorkspace } from '../workspace/useWorkspace';

// The section the design draws at the top of the mailbox column, above every account: three standing views of the
// mailbox, over what MailFathom's own reading of the mail already produced.
//
// Each entry is a shortcut to filter criteria and nothing else. Pressing one narrows the list in front of the reader
// by what a derivation recorded, exactly as the filter panel does, and the criteria then stand in that panel where
// every other narrowing stands — removable one at a time and changeable there. There is no second way of asking the
// deployment for mail behind these, and nothing here derives anything: what a view can find is what MailFathom already
// read, so a deployment that has read nothing answers each of them with a folder narrowed to no mail.
//
// **It collapses exactly as an account group does**: its header carries the same chevron, and folding it is kept with
// the account folds in the workspace, under a key of its own, so it stays folded as the reader moves around and comes
// back folded after a reload. Folded to the rail, the header is the section's symbol alone and still folds it.
//
// The design draws no reaction and no lit state on these entries — only the folder rows carry one — so nothing here
// draws a view as being in force. What says so is the filter panel's own count and the criteria in it, which is where
// the design does draw what a list is narrowed by.

const views: Readonly<Record<StandingView, { readonly icon: IconName; readonly label: MessageKey }>> = {
    needsDecision: { icon: 'pending_actions', label: 'aiFilters.needsDecision' },
    commitments: { icon: 'handshake', label: 'aiFilters.commitments' },
    deadlinesThisWeek: { icon: 'schedule', label: 'aiFilters.deadlinesThisWeek' },
};

/** The key the section's fold is kept under, beside the keys of the account rows the tree folds. */
export const aiFiltersFold = 'aiFilters';

export function AiFilters({ folded }: { readonly folded: boolean }) {
    const { translate } = useLocalization();
    const { workspace, revise } = useWorkspace();
    const listed = useListedMail();
    const shown = useAiFiltersShown();
    const open = !workspace.foldsToggled.includes(aiFiltersFold);

    // Turned off is the section gone rather than a heading with nothing under it: what it says when it is off is
    // nothing at all, and a heading over three missing entries would say the views had failed.
    if (!shown) {
        return null;
    }

    function toggle(): void {
        revise({
            foldsToggled: open
                ? [...workspace.foldsToggled, aiFiltersFold]
                : workspace.foldsToggled.filter((key) => key !== aiFiltersFold),
        });
    }

    const heading = translate('aiFilters.heading');

    return (
        <section
            aria-label={heading}
            className={`mb-2 flex shrink-0 flex-col border-b border-line pb-2.5 ${folded ? 'gap-0.75' : 'gap-0.5'}`}
        >
            <button
                type="button"
                aria-expanded={open}
                title={translate(open ? 'aiFilters.collapse' : 'aiFilters.show')}
                className={`flex items-center rounded-sm transition hover:bg-hover ${
                    folded
                        ? 'h-6 justify-center'
                        : 'gap-2 px-2.25 py-1.25 text-xs font-semibold text-muted hover:text-text'
                }`}
                onClick={toggle}
            >
                <span className="flex w-2 shrink-0 justify-center">
                    <Icon name="auto_awesome" className="size-icon-xs shrink-0 text-accent-deep" />
                </span>

                {/* The rail has no room for the name; the reader who hears the control rather than sees it is given it
                    anyway, which is what keeps a column of symbols something a screen reader can work through. */}
                <span className={folded ? 'sr-only' : 'min-w-0 flex-1 text-start'}>{heading}</span>

                {folded ? null : (
                    <Icon name={open ? 'expand_more' : 'chevron_right'} className="size-icon-sm text-faint" />
                )}
            </button>

            {open
                ? standingViews.map((view) => (
                      <button
                          key={view}
                          type="button"
                          title={translate('aiFilters.entryTitle', { view: translate(views[view].label) })}
                          className={`flex cursor-pointer items-center rounded-md text-text-soft transition hover:bg-hover ${
                              folded ? 'justify-center py-1.25' : 'gap-2.5 px-2.75 py-1.75 text-md'
                          }`}
                          onClick={() => {
                              listed.stand(view);
                          }}
                      >
                          <Icon
                              name={views[view].icon}
                              className={folded ? 'size-icon-2xl text-muted' : 'size-icon-md text-muted'}
                          />

                          {folded ? (
                              <span className="sr-only">{translate(views[view].label)}</span>
                          ) : (
                              <span className="min-w-0 flex-1 truncate text-start">{translate(views[view].label)}</span>
                          )}
                      </button>
                  ))
                : null}
        </section>
    );
}
