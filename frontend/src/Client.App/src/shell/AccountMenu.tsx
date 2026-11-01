// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useId, useRef, useState } from 'react';
import type { MailAccount, OwnPresence } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { MailboxMark } from '../controls/MailboxMark';
import { PersonAvatar } from '../controls/PersonAvatar';
import { SharedMark } from '../controls/SharedMark';
import type { TelemetryForwarding } from '../deployment/telemetryForwarding';
import { useLocalization } from '../localization/useLocalization';
import type { ClientPreferencesInForce } from '../preferences/useClientPreferences';
import { OwnPresenceRow } from '../presence/OwnPresenceRow';
import { PresenceDot } from '../presence/PresenceDot';
import type { OwnProfileInForce } from '../profile/useOwnProfile';
import { Settings } from '../settings/Settings';
import { accountInScope, scopeOfAccount } from '../workspace/mailScope';
import { useWorkspace } from '../workspace/useWorkspace';
import { useFocusKeptWhenWithdrawn } from '../controls/useFocusKeptWhenWithdrawn';
import { ConnectionStatusLine } from './ConnectionStatusLine';
import { disconnected, useConnectionStatus } from './connectionStatus';
import { TabModeSwitch, ThemeSegments } from './Preferences';
import { useScreenLayer } from './screenLayers';
import { useNavigationRail } from './useWideWorkspace';
import { Departing } from './Departing';

// The menu at the foot of the rail, which is where the design puts everything that is about the person rather
// than about the mail: who they are, which mailboxes this deployment reads for them and which of those the client is
// scoped to, the two settings that follow them between machines, the way into everything else about them, and the way
// out. It is the platform's own popover rather than a menu built out of state — it opens and closes from the control
// that names it, closes on Escape and on a press outside it, and hands focus back to that control, none of which this
// file has to write.
//
// Two things a reader might expect here are deliberately elsewhere, both because the design draws them there.
// The language is on the settings screen: the menu holds what somebody reaches for between messages, and what language
// the client reads in is set once. What the client and the deployment are running is on the sign-in screen and at the
// foot of that same settings screen, which is where somebody looks for a version — this menu only carries the
// deployment's version through to the screen its own row opens.
//
// The way out of a deployment somebody named themselves is not here either, and that one is not a design decision:
// pointing the client elsewhere ends the session anyway, so the sign-in screen the frame falls back to is where it is
// offered and where it still works when a kept password has stopped being accepted.

/** What a withdrawn choice does when something still reaches it, which is nothing. */
function nothingChosen(): void {
    // A disabled fieldset already keeps a press away; this is what holds where something reaches the input anyway.
}

export function AccountMenu({
    accounts,
    deploymentVersion,
    telemetryForwarding,
    writesProfile,
    preferences,
    profile,
    presence,
    onSignOut,
}: {
    /** The mailboxes this deployment reads for the signed-in person, which is empty while nothing has answered. */
    readonly accounts: readonly MailAccount[];

    /** What the deployment answered it is running, or `null` while nothing has answered, for the screen below. */
    readonly deploymentVersion: string | null;

    /**
     * What this deployment has said about forwarding this client's own records, which the settings screen behind this
     * menu's own row words. A forwarded one carries the deployment's address whether or not somebody typed it, because
     * what that screen has to name is where the records actually go rather than how the client came to be pointed
     * there.
     */
    readonly telemetryForwarding: TelemetryForwarding;

    /** Whether this credential may change what the deployment holds about the person, which the settings screen reads. */
    readonly writesProfile: boolean;

    /** The settings that follow the person, and the four ways of changing one. */
    readonly preferences: ClientPreferencesInForce;

    /** Who the client is drawing, which this menu shows and the screen behind its own row edits. */
    readonly profile: OwnProfileInForce;

    /** What the member reads as to themselves, or `null` for a reader with no presence — one in no organization. */
    readonly presence: OwnPresence | null;

    readonly onSignOut: () => void;
}) {
    const { translate } = useLocalization();
    const inRail = useNavigationRail();
    const preferencesRefused = disconnected(useConnectionStatus()) ? translate('preferences.noConnection') : null;
    const refusalSaid = useFocusKeptWhenWithdrawn<HTMLParagraphElement>(preferencesRefused !== null);
    const [settingsOpen, setSettingsOpen] = useState(false);
    const [standing, setStanding] = useState(false);
    const menu = useRef<HTMLDivElement>(null);
    const settingsRow = useRef<HTMLButtonElement>(null);

    // The member's presence, drawn on the circle in the rail and named to whoever reaches it there: the dot is the only
    // telling the circle carries, so it describes the control rather than standing beside words. Where the bar stands the row
    // leading to the status menu heads the *More* sheet instead of standing in this menu.
    const presenceNamed = useId();
    const presenceDrawn = inRail ? presence : null;

    // The menu is folded away while the screen behind it is open, which is what the design draws, and put back
    // when it closes so that focus returns to the row it was opened from rather than to the rail. The platform's own
    // two methods rather than a state of ours: the popover is the platform's, and a second opinion about whether it is
    // open is the pair that comes to disagree.
    function openSettings(): void {
        menu.current?.hidePopover();
        setSettingsOpen(true);
    }

    // The menu comes back where the screen behind it was left by a way out of that screen — a control, Escape, or one
    // press of the back gesture — and does not where the shell cleared everything on the way to another destination.
    // Putting it back there would open a menu over the space somebody has just arrived at, with focus moved into it,
    // which is the one thing clearing the screen exists to prevent.
    function closeSettings(clearingTheScreen: boolean): void {
        setSettingsOpen(false);

        if (clearingTheScreen) {
            return;
        }

        menu.current?.showPopover();
        settingsRow.current?.focus();
    }

    // The menu stands over whichever screen the rail is beside, so the back gesture closes it before it navigates and
    // taking the navigation to another destination leaves none of it behind. Whether it is open stays the platform's
    // answer for the reason above: what is recorded here is the way to close it, never a second opinion about whether
    // it is.
    useScreenLayer(standing, () => {
        menu.current?.hidePopover();
    });

    return (
        <>
            {/* The control is the circle alone at the foot of the rail, where the design draws it beside six
                other symbols, and a named row at the foot of the overflow sheet, where it stands among rows that each
                carry a name. Same control, same menu, same accessible name — what changes is that a row in a list of
                rows says what it is rather than leaving a lone circle to be recognised. */}
            <button
                type="button"
                popoverTarget="account-menu"
                aria-label={inRail ? translate('shell.account') : undefined}
                aria-describedby={presenceDrawn === null ? undefined : presenceNamed}
                className={
                    inRail
                        ? `flex size-10 shrink-0 items-center justify-center rounded-full text-text-soft shadow-raised transition hover:-translate-y-px hover:shadow-overlay ${standing ? 'outline-2 outline-offset-2 outline-accent-line' : ''}`
                        : 'flex min-h-13 w-full items-center gap-3.5 rounded-md px-2.5 text-start text-text transition hover:bg-hover'
                }
            >
                <span className="relative shrink-0">
                    <PersonAvatar displayName={profile.displayName} picture={profile.picture} place="menu" />
                    {presenceDrawn === null ? null : (
                        <PresenceDot presence={presenceDrawn.state} place="rail" id={presenceNamed} />
                    )}
                </span>
                {inRail ? null : <span className="truncate">{translate('shell.account')}</span>}
            </button>

            {/* No display utility on the popover itself: the platform hides a closed popover with `display: none` from
                its own stylesheet, and a utility on the element would outrank that and draw the menu open forever. */}
            <div
                ref={menu}
                id="account-menu"
                popover="auto"
                onToggle={(event) => {
                    setStanding(event.newState === 'open');
                }}
                aria-label={translate('shell.accountMenu')}
                className="surface-pop inset-x-4 top-auto bottom-22 m-0 overflow-hidden rounded-lg border border-line bg-panel p-0 text-base text-text shadow-overlay rail:inset-x-auto rail:bottom-4.5 rail:left-26.5 rail:w-54"
            >
                <ConnectionStatusLine place="menu" />

                <div className="flex flex-col gap-0.5 border-b border-line-soft px-3.25 py-2.75">
                    {profile.displayName === null ? null : (
                        <p className="truncate font-semibold">{profile.displayName}</p>
                    )}

                    {presenceDrawn === null ? null : (
                        <OwnPresenceRow
                            presence={presenceDrawn}
                            place="menu"
                            displayName={profile.displayName}
                            picture={profile.picture}
                        />
                    )}

                    <Mailboxes accounts={accounts} />
                </div>

                {/* Both rows write what follows the person, so while the server is out of reach they are withdrawn
                    together — a fieldset being the platform's own way of withdrawing every control in it — and one
                    line under them says why, rather than a choice that would be taken and then not reach anybody. */}
                <fieldset disabled={preferencesRefused !== null} className="contents [&:disabled>*]:opacity-60">
                    <TabModeSwitch
                        on={preferences.openMailInTabs}
                        onChange={preferencesRefused === null ? preferences.chooseTabMode : nothingChosen}
                    />

                    {/* One rule under the theme block and none above it, which is where the design draws the
                        menu's second divider: the tab row and the theme row read as one group of settings. */}
                    <div className="border-b border-line-soft">
                        <ThemeSegments
                            onChoose={preferencesRefused === null ? preferences.chooseTheme : nothingChosen}
                        />

                        {preferencesRefused === null ? null : (
                            <p
                                ref={refusalSaid}
                                role="status"
                                tabIndex={-1}
                                className="px-3.25 pb-2 text-xs text-muted outline-none"
                            >
                                {preferencesRefused}
                            </p>
                        )}
                    </div>
                </fieldset>

                <button
                    ref={settingsRow}
                    type="button"
                    className="flex w-full items-center gap-2.5 px-3.25 py-2.5 text-start transition hover:bg-hover"
                    onClick={openSettings}
                >
                    <Icon name="settings" className="size-icon-lg" />
                    {translate('settings.title')}
                </button>

                {preferences.notStated || preferences.stateUnknown ? (
                    <p className="border-t border-line-soft px-3.25 py-2 text-xs text-warning-text">
                        {translate(preferences.stateUnknown ? 'preferences.stateUnknown' : 'preferences.notStated')}
                    </p>
                ) : null}

                <button
                    type="button"
                    className="flex w-full items-center gap-2.5 border-t border-line-soft px-3.25 py-2.5 text-start text-muted transition hover:bg-hover hover:text-text"
                    onClick={onSignOut}
                >
                    <Icon name="logout" className="size-icon-lg" />
                    {translate('shell.signOut')}
                </button>
            </div>

            {/* Outside the popover rather than inside it, because the menu is folded away while this is open and a
                dialog inside something the platform sets `display: none` on is a dialog nobody can see. Rendered only
                while it is open and while it leaves, which is what keeps the tab it was last left on out of anything that
                outlives it. */}
            <Departing value={settingsOpen || null}>
                {() => (
                    <Settings
                        profile={profile}
                        preferences={preferences}
                        telemetryForwarding={telemetryForwarding}
                        writesProfile={writesProfile}
                        deploymentVersion={deploymentVersion}
                        onClose={closeSettings}
                    />
                )}
            </Departing>
        </>
    );
}

// Which mailboxes this deployment reads for the person, and which of them the client is scoped to — the second place
// that is chosen, the folder tree being the first. It writes the same `MailScope` into the same workspace that tree
// writes, so the two are one decision drawn twice rather than two notions of which mailbox is in scope; anything that
// held its own would be the pair that comes to disagree.
//
// The mark is the one the folder tree draws in front of the same mailbox's folders, so a mailbox keeps one colour
// wherever it appears; the ordinal is shifted by one because that tree's first mark stands for every mailbox at once
// and this list has no such row.
//
// The check follows the account a scope belongs to rather than the scope itself, which is what makes it right for the
// folder scopes too: somebody reading one folder of their work mailbox is in that mailbox, and the row saying so is
// the same row they would press to widen the scope back out to all of it. Every mailbox at once, and a role spanning
// them, belong to no account and are checked nowhere — which is the tree's own answer as well.
//
// Nothing is drawn where nothing answered. An empty list is a deployment that has not answered yet, one whose accounts
// this credential may not read, and one that declares no mailbox — three sentences the connection summary already says
// in the space a person is looking at, and repeating any of them here would be saying it twice.
function Mailboxes({ accounts }: { readonly accounts: readonly MailAccount[] }) {
    const { translate } = useLocalization();
    const { workspace, revise } = useWorkspace();

    // The words over the list name the list rather than heading a section: the menu stands inside a space whose own
    // heading level is the space's, and a heading opened here would be one out of order in a popover.
    const named = useId();

    if (accounts.length === 0) {
        return null;
    }

    const inScope = accountInScope(workspace.scope);

    return (
        <>
            <p id={named} className="pt-1.5 text-xs font-semibold text-faint">
                {translate('shell.mailboxes')}
            </p>

            <ul aria-labelledby={named} className="flex flex-col">
                {accounts.map((account, ordinal) => (
                    <li key={account.id} className="flex">
                        {/* The row is a button rather than a list item somebody may click: what it does is an act, so
                            it takes focus, answers Enter and Space, and is announced as something to press without
                            any of that being written here. `aria-current` is what says which one is in scope — the
                            check beside it is that same fact drawn, for a reader who is looking. */}
                        <button
                            type="button"
                            aria-current={account.id === inScope ? 'true' : undefined}
                            className="-mx-1 flex min-w-0 flex-1 items-center gap-2 rounded-md px-1 py-0.75 text-start text-muted transition hover:bg-hover hover:text-text"
                            onClick={() => {
                                revise({ scope: scopeOfAccount(account.id) });
                            }}
                        >
                            <MailboxMark ordinal={ordinal + 1} className="size-1.5" />
                            <span className="min-w-0 flex-1 truncate text-xs">{account.displayName}</span>
                            {account.shared ? <SharedMark /> : null}

                            {account.id === inScope ? (
                                <Icon name="check" className="size-icon-xs shrink-0 text-accent-strong" />
                            ) : null}
                        </button>
                    </li>
                ))}
            </ul>
        </>
    );
}
