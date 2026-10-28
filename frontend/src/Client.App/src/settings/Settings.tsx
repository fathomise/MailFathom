// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useContext, useEffect, useId, useRef, useState } from 'react';
import {
    isNotificationTime,
    longestNotificationSeconds,
    shortestNotificationSeconds,
} from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { SymbolControl } from '../controls/SymbolControl';
import { useTabSwipe } from '../controls/useTabSwipe';
import type { IconName } from '../controls/icons';
import { PersonAvatar } from '../controls/PersonAvatar';
import { Switch } from '../controls/Switch';
import { VersionLine } from '../controls/VersionLine';
import type { TelemetryForwarding } from '../deployment/telemetryForwarding';
import { pressVersionLine } from '../developerInformation/developerAccess';
import type { MessageKey } from '../localization/en';
import { useFocusKeptWhenWithdrawn } from '../controls/useFocusKeptWhenWithdrawn';
import { useLocalization } from '../localization/useLocalization';
import { disconnected, useConnectionStatus } from '../shell/connectionStatus';
import { useScreenLayer } from '../shell/screenLayers';
import type { ClientPreferencesInForce } from '../preferences/useClientPreferences';
import type { OwnProfileInForce } from '../profile/useOwnProfile';
import { chooseSystemNotifications, useSystemNotificationsChosen } from '../preferences/systemNotifications';
import { LanguageSegments } from '../shell/Preferences';
import { useSystemNotifier } from '../shellOperations/systemNotifier';
import { useTelemetry } from '../telemetry/clientTelemetry';
import { chosenPortrait, type PortraitChoice } from './chosenPortrait';
import { MessageView, MessageViewWarning } from './MessageView';
import { DepartureContext } from '../shell/departure';
import { useTwoPanes } from '../shell/useWideWorkspace';

// The surface behind the account menu's own row, and the one place the client edits the person rather than the mail.
//
// **It takes the whole window at every composition**, the desktop included, which is what the design draws: the list
// of sections is meant to grow, and a card over a scrim leaves no room for a navigation that grows with it. Where the
// window has two panes the sections stand in a navigation down the left, grouped the way the design groups them and
// narrowed by *Find a setting*, with the open section's name and one sentence about it over its body; where it has one
// pane they are a tab strip under the head and the version moves to a footer. That is one question about the width,
// asked once through `useTwoPanes`, because the two are different controls rather than one control drawn two ways.
//
// **It offers only the sections the client has settings for.** The design draws five; the three here are the ones
// whose rows exist, and a section the client cannot fill is left out rather than drawn as a promise.
//
// It is a `dialog` opened as a modal rather than a panel drawn to look like one, because everything the acceptance asks
// of it is what that element already does: it takes focus, it keeps focus, it closes on Escape, it puts the page behind
// it out of reach, and closing it hands focus back to whatever opened it. A hand-written trap would be a second
// implementation of all five, and the one that gets them wrong.
//
// It is mounted while it is open and unmounted when it closes, which is what makes "the section last open is
// remembered nowhere" a property of the tree rather than a reset somebody has to remember to write.
//
// The *message view* section holds two controls in the order the design draws them: which of the two reading
// surfaces a message opens on, and whether a conversation opens with every message drawn. The first is a choice
// between two named things and the second is a switch, which is what the design draws and what each of them is.

/** The sections the client has settings for, in the order the design lists them. */
const sections = ['profile', 'notifications', 'application'] as const;

type SettingsSection = (typeof sections)[number];

/** What a section is called, what it holds, its symbol, and the group the navigation files it under. */
interface SectionMark {
    readonly name: MessageKey;
    readonly hint: MessageKey;
    readonly icon: IconName;
    readonly group: MessageKey;
}

const sectionMarks: Readonly<Record<SettingsSection, SectionMark>> = {
    profile: { name: 'settings.profile', hint: 'settings.profileHint', icon: 'person', group: 'settings.groupYou' },
    notifications: {
        name: 'settings.notifications',
        hint: 'settings.notificationsHint',
        icon: 'notifications',
        group: 'settings.groupAssistant',
    },
    application: {
        name: 'settings.application',
        hint: 'settings.applicationHint',
        icon: 'tune',
        group: 'settings.thisDevice',
    },
};

export function Settings({
    profile,
    preferences,
    telemetryForwarding,
    writesProfile,
    deploymentVersion,
    onClose,
}: {
    /** Who the client is drawing, and the three ways this surface changes it. */
    readonly profile: OwnProfileInForce;

    /** The settings that follow the person, of which this surface edits four. */
    readonly preferences: ClientPreferencesInForce;

    /** What this deployment has said about forwarding this client's telemetry, including that it has said nothing. */
    readonly telemetryForwarding: TelemetryForwarding;

    /**
     * Whether this credential may change what the deployment holds about the person — the picture, the zone, and the
     * settings that follow them. Reading all of it is a grant of its own, so a person may be shown what they may not
     * change, and what is theirs to set on this machine stays theirs either way.
     */
    readonly writesProfile: boolean;

    /** What the deployment answered it is running, or `null` while nothing has answered, for the line at the foot. */
    readonly deploymentVersion: string | null;

    /**
     * Leaves this screen, and says which of the two ways out did it: `false` for a control on the screen, Escape, or
     * one press of the back gesture, and `true` for the shell clearing everything on the way to another destination.
     * The menu this was opened from is put back by the first and by none of the second.
     */
    readonly onClose: (clearingTheScreen: boolean) => void;
}) {
    const { translate } = useLocalization();
    const panel = useRef<HTMLDialogElement>(null);
    const named = useId();
    const sectionId = useId();
    const [open, setOpen] = useState<SettingsSection>('profile');
    const sectionSwipe = useTabSwipe(sections, open, setOpen);
    const twoPanes = useTwoPanes();

    // What the deployment keeps is withdrawn for one of two reasons, each with its own sentence: this credential may
    // not change it, or the server is out of reach right now — when a change would have no route to it and is refused
    // before it is made rather than reported after. The grant's reason is said first, being for good where the
    // connection's is for now: somebody who may never change these is not told to wait for the server. What this
    // machine keeps for itself stays offered either way.
    const lostServer = disconnected(useConnectionStatus());
    const preferencesWithdrawn: MessageKey | null = !writesProfile
        ? 'settings.preferencesNotYours'
        : lostServer
          ? 'settings.preferencesNoConnection'
          : null;
    const profileWithdrawn: MessageKey | null = !writesProfile
        ? 'settings.profileNotYours'
        : lostServer
          ? 'settings.profileNoConnection'
          : null;

    // The one imperative browser API this surface synchronizes with, which is the whole of what an effect is for. It
    // runs once, because the element is in the document exactly as long as the surface is open.
    useEffect(() => {
        panel.current?.showModal();
    }, []);

    // It stands over whichever screen opened it, so the back gesture closes it before it navigates anywhere, and going
    // to another destination leaves it behind rather than over the screen somebody arrives at.
    // Which of the two asked, carried across the element's own `close` event, which says nothing about why it fired.
    // Every way out of this screen arrives at `onClose` through that one event — which is what keeps this screen with
    // one path out — so the answer is put down here and read on the other side of it.
    const clearingTheScreen = useRef(false);
    const leaving = useContext(DepartureContext);

    useScreenLayer(!leaving, (clearing) => {
        clearingTheScreen.current = clearing;
        panel.current?.close();
    });

    const body =
        open === 'profile' ? (
            <Profile profile={profile} withdrawn={profileWithdrawn} />
        ) : open === 'notifications' ? (
            <Notifications preferences={preferences} withdrawn={preferencesWithdrawn} />
        ) : (
            <Application
                preferences={preferences}
                telemetryForwarding={telemetryForwarding}
                withdrawn={preferencesWithdrawn}
            />
        );

    return (
        <dialog
            ref={panel}
            aria-labelledby={named}
            onClose={() => {
                onClose(clearingTheScreen.current);
            }}
            data-pull="down"
            // The insets are the screen's here rather than the frame's: a modal dialog stands in the platform's own top
            // layer, so the padding the frame carries is not around it and a full-bleed settings screen would run under
            // a cutout and under the gesture bar.
            className="max-panes:surface-sheet panes:surface-pop m-0 h-full max-h-full w-full max-w-full rounded-none border-0 bg-panel p-0 pt-safe-top pr-safe-right pb-safe-bottom pl-safe-left text-md text-text backdrop:bg-scrim open:flex open:flex-col"
        >
            <span aria-hidden="true" className="flex h-5 shrink-0 items-center justify-center bg-sunken panes:hidden">
                <span className="h-1 w-9.5 rounded-full bg-line-strong" />
            </span>

            <div className="flex shrink-0 items-center gap-2.5 border-b border-line bg-sunken pt-3 pr-3 pb-2.5 pl-4 panes:min-h-9 panes:py-2.5 panes:pr-3.5 panes:pl-5">
                <Icon name="settings" className="size-icon-xl text-muted" />

                <h2 id={named} className="text-2xl font-semibold panes:text-xl">
                    {translate('settings.title')}
                </h2>

                {/* Closed through the element rather than by answering upwards, so that the native `close` event is
                    the one path out and `onClose` is called once however the surface was left. */}
                <SymbolControl
                    label={translate('settings.close')}
                    className="ms-auto flex size-6.5 items-center justify-center rounded-md text-muted transition hover:bg-hover hover:text-text"
                    onPress={() => {
                        panel.current?.close();
                    }}
                >
                    <Icon name="close" className="size-icon-md" />
                </SymbolControl>
            </div>

            {twoPanes ? (
                <div className="flex min-h-0 flex-1">
                    <SettingsNavigation open={open} onOpen={setOpen} deploymentVersion={deploymentVersion} />

                    <div className="min-h-0 min-w-0 flex-1 overflow-y-auto">
                        <section
                            aria-labelledby={`${sectionId}-title`}
                            className="flex max-w-settings flex-col gap-3 px-7 pt-6.5 pb-10 roomy:px-12 roomy:pt-8.5 roomy:pb-12"
                        >
                            <div className="mb-1.5 flex flex-col gap-1 border-b border-line-soft pb-2.5">
                                <h3 id={`${sectionId}-title`} className="text-4xl font-semibold tracking-tight">
                                    {translate(sectionMarks[open].name)}
                                </h3>
                                <p className="max-w-settings-prose text-md text-pretty text-muted">
                                    {translate(sectionMarks[open].hint)}
                                </p>
                            </div>

                            {body}
                        </section>
                    </div>
                </div>
            ) : (
                <>
                    <SettingsTabs id={sectionId} open={open} onOpen={setOpen} />

                    <div
                        role="tabpanel"
                        id={`${sectionId}-panel`}
                        aria-labelledby={`${sectionId}-${open}`}
                        className="flex min-h-0 flex-1 touch-pan-y touch-pinch-zoom flex-col gap-2.75 overflow-y-auto px-4 pt-3.5 pb-7"
                        {...sectionSwipe}
                    >
                        {body}
                    </div>

                    {/* Outside the scrolling panel and under every section, which is where the design draws it on one
                        pane: what is running is about the client rather than about whichever section is open. */}
                    <VersionLine
                        deploymentVersion={deploymentVersion}
                        className="border-t border-line bg-sunken px-4 py-2.5 text-center text-sm text-faint"
                        onPress={pressVersionLine}
                    />
                </>
            )}
        </dialog>
    );
}

/** The groups the navigation files the sections under, each once, in the order the sections first reach them. */
function groupsOf(shown: readonly SettingsSection[]): readonly MessageKey[] {
    return [...new Set(shown.map((section) => sectionMarks[section].group))];
}

/**
 * The sections down the left of a window with two panes: *Find a setting* at the top, the sections grouped under the
 * design's headings, and what is running at the foot.
 *
 * A navigation of buttons rather than a tab list, because a tab list owns tabs and nothing else, and the group headings
 * are what make a long list of sections readable. The open one says so with `aria-current`, and the section beside it
 * is named by its own heading.
 *
 * The search narrows the sections by their names and by the sentence saying what each holds, in the language being
 * read, and it narrows the navigation only — the section that is open stays open.
 */
function SettingsNavigation({
    open,
    onOpen,
    deploymentVersion,
}: {
    readonly open: SettingsSection;
    readonly onOpen: (section: SettingsSection) => void;
    readonly deploymentVersion: string | null;
}) {
    const { locale, translate } = useLocalization();
    const [query, setQuery] = useState('');
    const sought = query.trim().toLocaleLowerCase(locale);
    const shown = sections.filter(
        (section) =>
            sought === '' ||
            `${translate(sectionMarks[section].name)} ${translate(sectionMarks[section].hint)}`
                .toLocaleLowerCase(locale)
                .includes(sought),
    );

    return (
        <nav
            aria-label={translate('settings.sections')}
            className="flex w-settings-navigation-narrow shrink-0 flex-col overflow-y-auto border-e border-line bg-sunken px-3 pt-4 pb-3.5 roomy:w-settings-navigation"
        >
            <label className="mx-0.5 mb-3 flex h-9 shrink-0 items-center gap-2 rounded-field border border-line bg-panel px-2.5 has-[:focus-visible]:border-accent">
                <Icon name="search" className="size-icon-md text-faint" />
                <span className="sr-only">{translate('settings.find')}</span>
                <input
                    type="search"
                    value={query}
                    placeholder={translate('settings.find')}
                    className="min-w-0 flex-1 bg-transparent text-md text-text outline-none placeholder:text-faint"
                    onChange={(event) => {
                        setQuery(event.target.value);
                    }}
                />
            </label>

            {groupsOf(shown).map((group) => (
                <div key={group} className="flex flex-col gap-0.5 pb-3">
                    <p className="px-3 pt-1 pb-1.5 text-xs font-semibold text-faint">{translate(group)}</p>

                    <ul className="flex flex-col gap-0.5">
                        {shown
                            .filter((section) => sectionMarks[section].group === group)
                            .map((section) => (
                                <li key={section}>
                                    <button
                                        type="button"
                                        aria-current={section === open ? 'true' : undefined}
                                        className={`flex w-full items-center gap-2.75 rounded-md px-3 py-2 text-start text-md transition ${
                                            section === open
                                                ? 'bg-accent-soft font-semibold text-accent-deep'
                                                : 'text-text-soft hover:bg-hover hover:text-text'
                                        }`}
                                        onClick={() => {
                                            onOpen(section);
                                        }}
                                    >
                                        <Icon
                                            name={sectionMarks[section].icon}
                                            className={`size-icon-lg shrink-0 ${section === open ? '' : 'text-muted'}`}
                                        />
                                        <span className="min-w-0 flex-1 truncate">
                                            {translate(sectionMarks[section].name)}
                                        </span>
                                    </button>
                                </li>
                            ))}
                    </ul>
                </div>
            ))}

            {shown.length === 0 ? (
                <p className="px-3 py-1.5 text-base text-muted">{translate('settings.nothingFound')}</p>
            ) : null}

            <VersionLine
                deploymentVersion={deploymentVersion}
                className="mt-auto truncate px-3 pt-3 pb-0.5 text-start text-sm text-faint"
                onPress={pressVersionLine}
            />
        </nav>
    );
}

/**
 * The sections as a tab strip, which is what the design draws on one pane and what a screen reader is owed there.
 *
 * Buttons carrying the tab role rather than radio inputs, because a tab list has its own keyboard contract: one tab
 * stop for the whole list, the arrow keys moving between the tabs inside it, and Home and End reaching the ends. That
 * is what the roving `tabIndex` below is — the selected tab is the one the Tab key lands on, and the arrows move both
 * the selection and the focus, which is the pattern for tabs whose panel follows the selection. The strip scrolls
 * sideways where the window is narrower than the sections.
 */
function SettingsTabs({
    id,
    open,
    onOpen,
}: {
    readonly id: string;
    readonly open: SettingsSection;
    readonly onOpen: (section: SettingsSection) => void;
}) {
    const { translate } = useLocalization();

    // The tabs themselves, so a key press can put focus on the one it selected. Held by identity rather than found by a
    // selector, because `useId` composes identifiers a CSS selector would have to be escaped for.
    const buttons = useRef(new Map<SettingsSection, HTMLButtonElement>());

    function move(to: SettingsSection): void {
        onOpen(to);
        buttons.current.get(to)?.focus();
    }

    return (
        <div
            role="tablist"
            aria-label={translate('settings.sections')}
            className="flex shrink-0 items-stretch gap-4.5 overflow-x-auto border-b border-line bg-sunken px-4"
            onKeyDown={(event) => {
                const stepped = steppedTo(event.key, open);

                if (stepped !== null) {
                    event.preventDefault();
                    move(stepped);
                }
            }}
        >
            {sections.map((offered) => (
                <button
                    key={offered}
                    ref={(element) => {
                        if (element === null) {
                            buttons.current.delete(offered);
                        } else {
                            buttons.current.set(offered, element);
                        }
                    }}
                    id={`${id}-${offered}`}
                    type="button"
                    role="tab"
                    aria-selected={offered === open}
                    aria-controls={`${id}-panel`}
                    tabIndex={offered === open ? 0 : -1}
                    className={`shrink-0 border-b-2 px-px pt-2.5 pb-2 text-md whitespace-nowrap transition pointer-coarse:px-1 pointer-coarse:pt-3.5 pointer-coarse:pb-3 pointer-coarse:text-xl ${
                        offered === open
                            ? 'border-accent font-semibold text-text'
                            : 'border-transparent text-muted hover:text-text'
                    }`}
                    onClick={() => {
                        onOpen(offered);
                    }}
                >
                    {translate(sectionMarks[offered].name)}
                </button>
            ))}
        </div>
    );
}

/**
 * Which section a key press moves to from the one that is open, or `null` where the press is not one this list
 * answers.
 *
 * The list does not wrap at its ends: holding an arrow down walks to the end and stays there rather than oscillating
 * the selection under the reader, and Home and End reach the two ends at once.
 */
function steppedTo(key: string, from: SettingsSection): SettingsSection | null {
    const at = sections.indexOf(from);
    const last = sections.length - 1;

    switch (key) {
        case 'ArrowLeft':
            return sections[Math.max(0, at - 1)] ?? null;
        case 'ArrowRight':
            return sections[Math.min(last, at + 1)] ?? null;
        case 'Home':
            return sections[0];
        case 'End':
            return sections[last] ?? null;
        default:
            return null;
    }
}

function Divider() {
    return <div className="my-1 h-px bg-line-soft" />;
}

function SectionName({ children }: { readonly children: string }) {
    return <p className="text-sm font-semibold text-faint">{children}</p>;
}

// What the client is read in and how it draws a conversation, in the design's own order: the language, then
// the message view, then the privacy section carrying the telemetry decision.
//
// The language is this machine's and the rest follow the person, which is what the group below is: the deployment
// keeps what follows somebody and takes a change to it under a grant of its own, so a credential without that grant is
// shown those rows as they stand, with the sentence saying why, and keeps the language, which no deployment is asked
// about.
function Application({
    preferences,
    telemetryForwarding,
    withdrawn,
}: {
    readonly preferences: ClientPreferencesInForce;
    readonly telemetryForwarding: TelemetryForwarding;

    /** Why the rows the deployment keeps are shown rather than offered, or `null` where they are offered. */
    readonly withdrawn: MessageKey | null;
}) {
    const { translate } = useLocalization();
    const reason = useFocusKeptWhenWithdrawn<HTMLParagraphElement>(withdrawn !== null);

    return (
        <>
            <SectionName>{translate('shell.language')}</SectionName>
            <LanguageSegments />

            <Divider />

            {withdrawn === null ? null : (
                <p ref={reason} tabIndex={-1} className="text-base text-muted outline-none">
                    {translate(withdrawn)}
                </p>
            )}

            <fieldset disabled={withdrawn !== null} className={followingThePerson}>
                <SectionName>{translate('settings.messageView')}</SectionName>
                <MessageView preferences={preferences} />
                <ThreadExpansion preferences={preferences} />
                <MessageViewWarning preferences={preferences} />

                <Divider />

                <SectionName>{translate('settings.mailbox')}</SectionName>
                <AiFiltersSection preferences={preferences} />

                <Divider />

                <SectionName>{translate('settings.privacy')}</SectionName>
                <Telemetry preferences={preferences} forwarding={telemetryForwarding} />
            </fieldset>
        </>
    );
}

// What reaches somebody and for how long, under the one heading the design gives both rows: whether this machine
// raises a notification of its own, and how long one stands wherever it is drawn. The first is this machine's and is
// drawn only where a head can raise one; the second follows the person, so it sits in a group the deployment keeps.
function Notifications({
    preferences,
    withdrawn,
}: {
    readonly preferences: ClientPreferencesInForce;

    /** Why the rows the deployment keeps are shown rather than offered, or `null` where they are offered. */
    readonly withdrawn: MessageKey | null;
}) {
    const { translate } = useLocalization();
    const reason = useFocusKeptWhenWithdrawn<HTMLParagraphElement>(withdrawn !== null);

    return (
        <>
            <SystemNotifications />

            {withdrawn === null ? null : (
                <p ref={reason} tabIndex={-1} className="text-base text-muted outline-none">
                    {translate(withdrawn)}
                </p>
            )}

            <fieldset disabled={withdrawn !== null} className={followingThePerson}>
                <NotificationDuration preferences={preferences} />
            </fieldset>
        </>
    );
}

// A group of rows the deployment keeps. A fieldset because disabling one is the platform's own way of withdrawing
// every control in it, and one that generates no box of its own, so that grouping the rows moves none of them: they
// stay the panel's own children for layout, which is what the panel's spacing and its scrolling are written against.
// The dimming is on the rows for the same reason — an element without a box has nothing to dim.
const followingThePerson = 'contents [&:disabled>*]:opacity-60';

// Whether this machine raises a notification of its own while nobody is looking at the window.
//
// It is drawn only where a head offered the operation, which is why the row is a component that may draw nothing: a
// switch on a head that cannot raise one would decide nothing, and a setting that says nothing about
// the machine it is read on is worse than an absent one. Nothing here asks which head it is running in — it asks
// whether the operation exists, which `shellOperations/systemNotifier.ts` is what answers.
//
// The choice belongs to this machine rather than to the person, so it is held in the device store and read from there
// rather than passed down — and read through a subscription rather than copied, because the person moving the switch is
// not the only writer: an arrival the operating system refuses turns it off from `useNotificationCentre.ts`, and a copy
// taken at mount would still read *on* underneath somebody with this dialog open.
//
// How long one of the client's own notifications stands, which is also how long the way back out of a permanent delete
// is open — the toast carrying that offer is the whole of the window, so the two are one number rather than two that
// have to agree.
//
// A field rather than a set of segments, because the bound is thirty whole seconds and a handful of segments over it
// would be a choice this client invented on the deployment's behalf. It is corrected in place, like the name on the
// profile tab and for the same reason: somebody who typed a number and moved on has said what they mean as plainly as
// one who pressed a button. The bound is stated to the browser as well as checked, and a value outside it is refused
// rather than brought inside — `chooseNotificationSeconds` is what a typed one reaches, and it answers as the route
// does.
function NotificationDuration({ preferences }: { readonly preferences: ClientPreferencesInForce }) {
    const { locale, translate } = useLocalization();
    const named = useId();
    const [refused, setRefused] = useState(false);
    const bounds = {
        shortest: new Intl.NumberFormat(locale).format(shortestNotificationSeconds),
        longest: new Intl.NumberFormat(locale).format(longestNotificationSeconds),
    };

    // Said here as well as refused by the hook, which refuses without a word: a field still holding the number somebody
    // typed would otherwise read as a number that was taken.
    function choose(seconds: number): void {
        const acceptable = isNotificationTime(seconds);

        setRefused(!acceptable);

        if (acceptable) {
            preferences.chooseNotificationSeconds(seconds);
        }
    }

    return (
        <>
            <div className="flex items-start gap-2.75 rounded-md px-2.5 py-2.25">
                <label htmlFor={named} className="flex min-w-0 flex-1 flex-col gap-0.75">
                    {translate('settings.notificationSeconds')}
                    <span className="text-base text-muted">
                        {translate('settings.notificationSecondsExplanation', bounds)}
                    </span>
                </label>

                {/* Uncontrolled and remounted by the value in force, for the reason the name field is: what somebody is
                    typing is not state this surface has any use for, and keying on the stated value is what redraws the
                    field when an answer arrives without discarding a correction that has not been sent yet. */}
                <input
                    key={preferences.notificationSeconds}
                    id={named}
                    type="number"
                    inputMode="numeric"
                    min={shortestNotificationSeconds}
                    max={longestNotificationSeconds}
                    step={1}
                    defaultValue={preferences.notificationSeconds}
                    className="w-16 shrink-0 rounded-md border border-line-strong bg-sunken px-2.5 py-1.5 text-base text-text outline-none focus:border-accent"
                    onBlur={(event) => {
                        choose(event.target.valueAsNumber);
                    }}
                    onKeyDown={(event) => {
                        if (event.key === 'Enter') {
                            choose(event.currentTarget.valueAsNumber);
                        }
                    }}
                />
            </div>

            {refused ? (
                <p className="text-sm text-warning">{translate('settings.notificationSecondsRefused', bounds)}</p>
            ) : null}
        </>
    );
}

// **Moving the switch on is the gesture the permission is asked from**, which is why this row asks rather than the
// arrival that would use it: a browser refuses the prompt outright unless somebody's own action led to it, and a
// dialog an arrival raised would land over whatever they were reading. Nothing here knows that a browser is what is
// underneath — it asks the operation where it stands and asks it to ask, and a head that grants unconditionally
// answers both without a dialog appearing at all.
//
// The switch says what this machine will actually do, which is the choice *and* the permission: a head nobody has been
// asked in yet draws off however the device store reads, because a control saying *on* over a machine that will raise
// nothing is the one state worse than an absent row. Only a refusal is explained in words, because it is the only one
// of the three that cannot be undone from this row — the browser has to be told first, from its own site settings.
//
// It is explained rather than disabled, and the switch stays live under the explanation, because the row promises in
// words that it will work again once the browser has been told. The gesture is what re-reads the answer: `permit()`
// asks the head where it stands before it asks anybody anything, so moving the switch on a browser still refusing puts
// no dialog in front of anyone and moving it on one told to allow them since is what notices. Disabling it would leave
// the one state the row's own sentence says is recoverable as the one state nothing on the screen can recover from.
function SystemNotifications() {
    const { translate } = useLocalization();
    const notifier = useSystemNotifier();
    const telemetry = useTelemetry();
    const raising = useSystemNotificationsChosen();

    // Read on every render rather than copied, for the same reason the switch above it is: this screen is not the only
    // writer. An arrival the head refuses turns the choice off from `useNotificationCentre.ts`, and a permission taken
    // back from the browser's own address bar is written by nobody at all — a value seeded at mount would go on
    // explaining a machine that will now raise nothing as one the switch can still turn back on.
    const standing = notifier.standing;

    // Nothing announces that answer, so the one moment this screen knows a redraw is owed is the moment it asked: the
    // head resolves its own dialog long after the gesture, and only a state change puts the answer on the screen.
    const [, redrawOnceTheHeadAnswers] = useState(0);

    if (!notifier.offered) {
        return null;
    }

    function choose(on: boolean): void {
        if (!on) {
            chooseSystemNotifications(false);

            return;
        }

        void notifier.permit().then((answered) => {
            redrawOnceTheHeadAnswers((asked) => asked + 1);

            // What the head answered, which is the one part of this a deployment cannot see at all: a refusal here is
            // a person who turned notifications on and got nothing, and every report of that reads as the client being
            // broken. The four standings are a closed set of this client's own.
            telemetry.happened('notifications_asked', { 'mailfathom.client.standing': answered });

            // Only an answer somebody gave is written. `unasked` is a question that reached nobody — a prompt the
            // browser dismissed without deciding, or a shell command that threw — and writing *off* for it would leave
            // a switch to undo on a machine that was never asked anything.
            if (answered !== 'unasked') {
                chooseSystemNotifications(answered === 'permitted');
            }
        });
    }

    // The heading goes with the row, because the row is the only thing on this page that belongs to the machine: a head
    // that offers no notifier draws neither, rather than a device heading over the person's own setting below it.
    return (
        <>
            <SectionName>{translate('settings.thisDevice')}</SectionName>

            <label className="flex cursor-pointer items-start gap-2.75 rounded-md px-2.5 py-2.25 transition hover:bg-hover">
                <span className="flex min-w-0 flex-1 flex-col gap-0.75">
                    {translate('settings.raiseSystemNotifications')}
                    <span className="text-base text-muted">
                        {translate(
                            standing === 'refused'
                                ? 'settings.systemNotificationsRefused'
                                : 'settings.raiseSystemNotificationsExplanation',
                        )}
                    </span>
                </span>

                <Switch on={raising && standing === 'permitted'} onChange={choose} />
            </label>
        </>
    );
}

// Whether a conversation opens with every message drawn. Stated as the choice rather than as its absence, which is the
// way round the design draws it: the switch being on is the conversation opening expanded, and the line under
// it says what the client does without it — which is what it does today and what an unset preference reads as.
function ThreadExpansion({ preferences }: { readonly preferences: ClientPreferencesInForce }) {
    const { translate } = useLocalization();

    return (
        <label className="flex cursor-pointer items-start gap-2.75 rounded-md px-2.5 py-2.25 transition hover:bg-hover">
            <span className="flex min-w-0 flex-1 flex-col gap-0.75">
                {translate('settings.expandWholeThread')}
                <span className="text-base text-muted">{translate('settings.expandWholeThreadExplanation')}</span>
            </span>

            <Switch on={preferences.expandWholeThread} onChange={preferences.chooseThreadExpansion} />
        </label>
    );
}

// Whether the folder tree carries the standing views of what MailFathom read. Turning it off removes the section
// rather than emptying it, which is what the switch says: the views are three sets of filter criteria, and a tree
// carrying a heading over nothing would read as three views that had failed rather than as a section nobody wanted.
function AiFiltersSection({ preferences }: { readonly preferences: ClientPreferencesInForce }) {
    const { translate } = useLocalization();

    return (
        <label className="flex cursor-pointer items-start gap-2.75 rounded-md px-2.5 py-2.25 transition hover:bg-hover">
            <span className="flex min-w-0 flex-1 flex-col gap-0.75">
                {translate('settings.aiFilters')}
                <span className="text-base text-muted">{translate('settings.aiFiltersExplanation')}</span>
            </span>

            <Switch on={preferences.aiFiltersShown} onChange={preferences.chooseAiFilters} />
        </label>
    );
}

// What the deployment records the person as, and the picture they are drawn by. The name is corrected in place rather
// than through a form with a button: there is one field, and a person who has typed their name and moved on has said
// what they mean as plainly as one who pressed Save.
function Profile({
    profile,
    withdrawn,
}: {
    readonly profile: OwnProfileInForce;

    /** Why what the deployment holds about the person is shown rather than offered, or `null` where it is offered. */
    readonly withdrawn: MessageKey | null;
}) {
    const { translate } = useLocalization();
    const reason = useFocusKeptWhenWithdrawn<HTMLParagraphElement>(withdrawn !== null);

    // What a file the person picked was refused for, which belongs to the control they picked it with rather than to
    // the profile: it is decided here and never sent, so nothing outside this surface has anything to do about it.
    const [refused, setRefused] = useState<PortraitChoice | null>(null);
    const named = useId();

    function pick(picture: File | undefined): void {
        if (picture === undefined) {
            return;
        }

        const choice = chosenPortrait(picture.type, picture.size);

        setRefused(choice.outcome === 'admissible' ? null : choice);

        if (choice.outcome === 'admissible') {
            profile.choosePicture(picture, choice.type);
        }
    }

    return (
        <>
            <div className="flex items-center gap-2.75">
                <PersonAvatar displayName={profile.displayName} picture={profile.picture} place="profile" />

                <div className="flex min-w-0 flex-1 flex-col gap-1.25">
                    <label htmlFor={named} className="sr-only">
                        {translate('settings.name')}
                    </label>

                    {/* Uncontrolled, and remounted by the name the deployment holds. What somebody is typing is not
                        state this surface has any use for, and a second copy of it beside the stored name is the pair
                        that comes to disagree; keying on the stored name is what redraws the field when an answer
                        arrives without discarding a correction that has not been sent yet. */}
                    <input
                        key={profile.displayName ?? ''}
                        id={named}
                        type="text"
                        defaultValue={profile.displayName ?? ''}
                        readOnly={!profile.changeable || withdrawn !== null}
                        placeholder={translate('settings.name')}
                        className="w-full max-w-110 rounded-md border border-line-strong bg-sunken px-2.5 py-1.75 text-md text-text outline-none read-only:text-muted focus:border-accent"
                        onBlur={(event) => {
                            correct(profile, event.target.value, withdrawn);
                        }}
                        onKeyDown={(event) => {
                            if (event.key === 'Enter') {
                                correct(profile, event.currentTarget.value, withdrawn);
                            }
                        }}
                    />

                    {/* Drawn whatever the field beside them is doing. `changeable` says whether *the name* would be
                        taken, which a settings policy may hold apart from everything else here; the portrait routes
                        ask only for the grant to change a profile, so that grant — and a server there to take the
                        change — alone decides whether these are offered. */}
                    {withdrawn !== null ? null : (
                        <div className="flex flex-wrap items-center gap-1.25">
                            <label className="flex cursor-pointer items-center gap-1.25 rounded-md border border-line-strong px-2.25 py-1 text-base text-text-soft transition hover:bg-hover has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-accent">
                                <Icon name="add_a_photo" className="size-icon-sm" />
                                {translate('settings.choosePicture')}

                                {/* The file input carries the label rather than being clicked by a handler: a hidden input
                                a control activates imperatively is one a keyboard reaches only by accident, and a
                                label already names it and opens it. */}
                                <input
                                    type="file"
                                    accept="image/jpeg,image/png"
                                    className="sr-only"
                                    onChange={(event) => {
                                        pick(event.target.files?.[0]);

                                        // Cleared so that picking the same file twice is a second choice rather than
                                        // nothing at all, which is what a person correcting a refusal does.
                                        event.target.value = '';
                                    }}
                                />
                            </label>

                            {profile.picture === null ? null : (
                                <button
                                    type="button"
                                    className="flex items-center gap-1 rounded-md px-2 py-1 text-base text-muted transition hover:bg-hover hover:text-text"
                                    onClick={() => {
                                        setRefused(null);
                                        profile.removePicture();
                                    }}
                                >
                                    <Icon name="delete" className="size-icon-sm" />
                                    {translate('settings.removePicture')}
                                </button>
                            )}

                            <span className="text-sm text-faint">{translate('settings.pictureBounds')}</span>
                        </div>
                    )}

                    <PictureNotice profile={profile} refused={refused} />
                </div>
            </div>

            <TimeZone profile={profile} writable={withdrawn === null} />

            <p className="text-base text-muted">{translate('settings.profileHeld')}</p>

            {/* Two reasons a name is shown rather than offered, and each has its own sentence: the credential may
                change nothing here, or it may and somebody else keeps the name. */}
            {withdrawn !== null ? (
                <p ref={reason} tabIndex={-1} className="text-base text-muted outline-none">
                    {translate(withdrawn)}
                </p>
            ) : profile.changeable ? null : (
                <p className="text-base text-muted">{translate('settings.nameNotYours')}</p>
            )}

            {profile.nameNotAcceptable ? (
                <p className="text-sm text-warning">{translate('settings.nameNotAcceptable')}</p>
            ) : null}

            {profile.nameNotStated || profile.nameStateUnknown ? (
                <p className="text-sm text-warning">
                    {translate(profile.nameStateUnknown ? 'settings.nameStateUnknown' : 'settings.nameNotStored')}
                </p>
            ) : null}
        </>
    );
}

// Which zone this person's own days are read in, which is two things rather than one: every date the client draws is
// written in it, and the deployment resolves "this week" and "since Tuesday" against it when they ask about their mail.
// That is why it is held by the deployment and offered here rather than taken from what the runtime reports — the two
// answers disagree for anybody travelling or reading their mail from a second machine, and the disagreement shows up
// as a message on the wrong day rather than as a failure.
//
// The design draws no such row, which is stated here rather than worked around: it settles what the profile
// tab looks like and not which values a deployment holds about somebody, so this takes the shape of the rows around it
// exactly as the notification rows on the other tab do.
//
// A list rather than a field, because an IANA identifier is not a thing anybody types correctly, and the list is the
// runtime's own — `Intl.supportedValuesOf` is every zone this engine can place a date in, which is the same question
// the deployment answers for itself. The zone in force is put into the list whether or not the runtime carries it, so a
// deployment holding one this engine has never heard of draws what it holds rather than silently drawing another zone.
function TimeZone({ profile, writable }: { readonly profile: OwnProfileInForce; readonly writable: boolean }) {
    const { translate } = useLocalization();
    const named = useId();

    if (profile.timeZone === null) {
        return null;
    }

    const inForce = profile.timeZone;
    const zones = offeredTimeZones(inForce);

    return (
        <>
            <div className="flex items-start gap-2.75 rounded-md px-2.5 py-2.25">
                <label htmlFor={named} className="flex min-w-0 flex-1 flex-col gap-0.75">
                    {translate('settings.timeZone')}
                    <span className="text-base text-muted">{translate('settings.timeZoneExplanation')}</span>
                </label>

                {/* Uncontrolled, and remounted by the zone the deployment holds, for the reason the name field
                    above is: a re-render while the choice is in flight would otherwise redraw the control on the
                    old zone, which reads as the choice having been discarded. */}
                <select
                    key={inForce}
                    id={named}
                    defaultValue={inForce}
                    disabled={!writable}
                    className="w-44 shrink-0 rounded-md border border-line-strong bg-sunken px-2.5 py-1.5 text-base text-text outline-none focus:border-accent"
                    onChange={(event) => {
                        profile.chooseTimeZone(event.target.value);
                    }}
                >
                    {zones.map((zone) => (
                        <option key={zone} value={zone}>
                            {zone}
                        </option>
                    ))}
                </select>
            </div>

            {profile.timeZoneNotAcceptable ? (
                <p className="text-sm text-warning">{translate('settings.timeZoneNotAcceptable')}</p>
            ) : null}

            {profile.timeZoneNotStated || profile.timeZoneStateUnknown ? (
                <p className="text-sm text-warning">
                    {translate(
                        profile.timeZoneStateUnknown ? 'settings.timeZoneStateUnknown' : 'settings.timeZoneNotStored',
                    )}
                </p>
            ) : null}
        </>
    );
}

// Every zone this runtime can place a date in, with the one in force among them. An engine without the enumeration
// answers with the zone in force alone, which is a list of one rather than a control that offers nothing.
function offeredTimeZones(inForce: string): readonly string[] {
    const carried = typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone') : [];

    return carried.includes(inForce) ? carried : [inForce, ...carried];
}

// What went wrong with the picture, which is one line whether this client refused the file or the deployment refused
// the request. The client's own refusal is said first: it is about the file in front of the person.
function PictureNotice({
    profile,
    refused,
}: {
    readonly profile: OwnProfileInForce;
    readonly refused: PortraitChoice | null;
}) {
    const { translate } = useLocalization();

    if (refused !== null && refused.outcome !== 'admissible') {
        return (
            <p className="text-sm text-warning">
                {translate(
                    refused.outcome === 'notAnImageKind'
                        ? 'settings.pictureNotAnImageKind'
                        : 'settings.pictureTooLarge',
                )}
            </p>
        );
    }

    return profile.pictureNotStated || profile.pictureStateUnknown ? (
        <p className="text-sm text-warning">
            {translate(profile.pictureStateUnknown ? 'settings.pictureStateUnknown' : 'settings.pictureNotStored')}
        </p>
    ) : null;
}

// Whether this deployment may be told what the client is doing, drawn the way the design states it: as the
// decision to withhold rather than the decision to permit, so that the switch being on is the private answer.
//
// Three things stand beside the switch that the design does not draw, all of them the acceptance of #1232 and
// all about the same thing — that a decision is only a decision if what is being decided is stated. Where the records
// go is named, because "telemetry" says nothing about who ends up holding it, and this client sends to the deployment
// somebody signed in to rather than anywhere else. A deployment that takes none from this client is said out loud
// instead of being drawn as a switch: there is nothing behind it there, so moving it would decide nothing. The
// sentence names no cause, because the session answers `off` both where the deployment forwards nothing at all and
// where a settings policy holds this person's switch off, and it does not say which. And a deployment that has
// not answered yet says that instead of either, because the frame records under the person's own answer while it
// waits — so drawing "nothing to turn off" over that state would be telling somebody nothing is being sent at exactly
// the moment something is.
function Telemetry({
    preferences,
    forwarding,
}: {
    readonly preferences: ClientPreferencesInForce;
    readonly forwarding: TelemetryForwarding;
}) {
    const { translate } = useLocalization();
    const withheld = !preferences.telemetryEnabled;

    if (!forwarding.answered) {
        return (
            <p className="rounded-md px-2.5 py-2.25 text-base text-muted">
                {translate('settings.telemetryUnanswered')}
            </p>
        );
    }

    const { destination } = forwarding;

    if (destination === null) {
        return (
            <p className="rounded-md px-2.5 py-2.25 text-base text-muted">
                {translate('settings.telemetryNotForwarded')}
            </p>
        );
    }

    return (
        <>
            <label className="flex cursor-pointer items-start gap-2.75 rounded-md px-2.5 py-2.25 transition hover:bg-hover">
                <span className="flex min-w-0 flex-1 flex-col gap-0.75">
                    {translate('settings.telemetryWithheld')}
                    <span className="text-base text-muted">{translate('settings.telemetryExplanation')}</span>
                </span>

                <Switch
                    on={withheld}
                    onChange={(chosen) => {
                        preferences.chooseTelemetry(!chosen);
                    }}
                />
            </label>

            <p className="text-sm text-faint">{translate('settings.telemetryDestination', { address: destination })}</p>

            {withheld ? (
                <p className="flex items-start gap-2 rounded-md border border-warning bg-warning-soft px-2.5 py-2.25 text-base text-warning-text">
                    <Icon name="info" className="size-icon-sm" />
                    {translate('settings.telemetryWithheldWarning')}
                </p>
            ) : null}
        </>
    );
}

// A correction is sent where it says something different from what the deployment holds. Anything else would be a
// write per field somebody tabbed through, and the deployment would answer the name it already had. Nothing is sent
// while the profile is withdrawn — a name typed before the server went quiet and left by a blur or an Enter after is a
// write the screen already says it refuses, and the field stands read-only with the reason beside it.
function correct(profile: OwnProfileInForce, typed: string, withdrawn: MessageKey | null): void {
    if (withdrawn === null && profile.changeable && typed.trim() !== (profile.displayName ?? '')) {
        profile.correctName(typed);
    }
}
