// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { MailAccount } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import { MailboxMark } from '../controls/MailboxMark';
import { useTip } from '../controls/useTip';
import { useLocalization } from '../localization/useLocalization';
import { useMailboxesForm } from '../workspace/mailboxesForm';
import { useWorkspace } from '../workspace/useWorkspace';
import { threadListAccountIn } from './threadRows';

// What the mailbox column offers while the list is read by conversation: the accounts and no folder. A conversation
// gathers its messages from every folder, junk and Trash included, so a folder would narrow nothing the list draws —
// what is left to pick is whose conversations they are. Every account at once heads the column where there is more than
// one to gather, exactly as the folder tree heads it with the row that is every inbox; with one account that row would
// be a second name for the same list.
//
// No row carries a menu, a count, or a drop target, because none of them has a folder to act on. Folded to its strip,
// each row is its glyph under the account's own mark, so a column of identical glyphs still says which is which.

export function ThreadAccounts({ accounts }: { readonly accounts: readonly MailAccount[] }) {
    const { translate } = useLocalization();
    const { workspace, revise } = useWorkspace();
    const folded = useMailboxesForm() === 'strip';
    const several = accounts.length > 1;

    const picked = threadListAccountIn(accounts, workspace.threadListAccount);

    const rows = [
        ...(several ? [{ id: null, name: translate('threadList.allAccounts'), icon: 'all_inbox' as const }] : []),
        ...accounts.map((account) => ({
            id: account.id,
            name: account.displayName,
            icon: account.shared ? ('group' as const) : ('account_circle' as const),
        })),
    ];

    return (
        <nav aria-label={translate('threadList.accounts')}>
            <ul className={`flex flex-col ${folded ? 'gap-0.75' : 'gap-0.5'}`}>
                {rows.map((row, ordinal) => (
                    <li key={row.id ?? ''}>
                        <AccountPick
                            name={row.name}
                            icon={row.icon}
                            ordinal={ordinal}
                            folded={folded}
                            picked={row.id === (several ? picked : (accounts[0]?.id ?? null))}
                            onPick={() => {
                                revise({ threadListAccount: row.id, selected: [] });
                            }}
                        />
                    </li>
                ))}
            </ul>
        </nav>
    );
}

function AccountPick({
    name,
    icon,
    ordinal,
    folded,
    picked,
    onPick,
}: {
    readonly name: string;
    readonly icon: IconName;

    /** Which mark the row carries, counted from the column's first row as the folder tree counts its groups. */
    readonly ordinal: number;

    readonly folded: boolean;
    readonly picked: boolean;
    readonly onPick: () => void;
}) {
    const { anchor, tip } = useTip({ says: name, onPress: onPick });
    const tone = picked ? 'bg-accent-soft font-semibold text-accent-deep' : 'text-text hover:bg-hover';

    return (
        <>
            <button
                {...(folded ? anchor : { onClick: onPick })}
                type="button"
                aria-current={picked ? 'true' : undefined}
                className={`flex w-full cursor-pointer items-center rounded-md transition ${
                    folded ? 'flex-col gap-0.75 py-1' : `gap-2.5 px-2.75 py-1.75 text-md ${tone}`
                }`}
            >
                {folded ? <MailboxMark ordinal={ordinal} /> : null}

                <span
                    className={
                        folded
                            ? `flex h-8.5 w-full items-center justify-center rounded-md ${tone}`
                            : 'flex shrink-0 items-center'
                    }
                >
                    <Icon
                        name={icon}
                        className={`shrink-0 ${folded ? 'size-icon-2xl' : 'size-icon-md'} ${picked ? '' : 'text-muted'}`}
                    />
                </span>

                <span className={folded ? 'sr-only' : 'min-w-0 flex-1 truncate text-start'}>{name}</span>
            </button>
            {folded ? tip : null}
        </>
    );
}
