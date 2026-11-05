// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useState, type ReactNode } from 'react';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { mostTableColumns, mostTableRows } from './writtenTable';

// The two surfaces the design draws for a table: what *Insert table* opens, and the strip of controls that stands
// under the formatting bar while the caret is in a cell. Neither touches the region — each says what was asked for,
// and the composer's words make the edit.

// What the design opens the menu on every time, and how much of the grid its preview draws.
const openingRows = 3;
const openingColumns = 3;
const previewRows = 6;
const previewColumns = 10;

/** The body of the *Insert a table* menu: a count of rows and of columns, a preview, the header switch, and the act. */
export function TableInsertion({
    onInsert,
}: {
    readonly onInsert: (rows: number, columns: number, headed: boolean) => void;
}) {
    const { translate, locale } = useLocalization();
    const counted = (count: number) => new Intl.NumberFormat(locale).format(count);
    const [rows, setRows] = useState(openingRows);
    const [columns, setColumns] = useState(openingColumns);
    const [headed, setHeaded] = useState(true);
    const shownColumns = Math.min(columns, previewColumns);
    const shownCells = Math.min(rows, previewRows) * shownColumns;

    return (
        <>
            <TableCount
                named={translate('compose.tableRows')}
                value={rows}
                counted={counted(rows)}
                most={mostTableRows}
                fewer={translate(rows <= 1 ? 'compose.tableRowsAtLeast' : 'compose.tableRowFewer', {
                    count: counted(1),
                })}
                more={translate(rows >= mostTableRows ? 'compose.tableRowsAtMost' : 'compose.tableRowMore', {
                    count: counted(mostTableRows),
                })}
                onCount={setRows}
            />
            <TableCount
                named={translate('compose.tableColumns')}
                value={columns}
                counted={counted(columns)}
                most={mostTableColumns}
                fewer={translate(columns <= 1 ? 'compose.tableColumnsAtLeast' : 'compose.tableColumnFewer', {
                    count: counted(1),
                })}
                more={translate(
                    columns >= mostTableColumns ? 'compose.tableColumnsAtMost' : 'compose.tableColumnMore',
                    { count: counted(mostTableColumns) },
                )}
                onCount={setColumns}
            />

            <div
                aria-hidden="true"
                className="mx-2.5 my-1.5 grid gap-0.5 rounded-md border border-line-soft bg-sunken p-1.5"
                style={{ gridTemplateColumns: `repeat(${String(shownColumns)}, minmax(0, 1fr))` }}
            >
                {Array.from({ length: shownCells }, (_, index) => (
                    <span
                        key={index}
                        className={`h-2.5 rounded-xs border border-line-strong pointer-coarse:h-3 ${
                            headed && index < shownColumns ? 'bg-accent-line' : 'bg-panel'
                        }`}
                    />
                ))}
            </div>

            <button
                type="button"
                role="menuitemcheckbox"
                aria-checked={headed}
                tabIndex={-1}
                className="flex min-h-8 items-center gap-2.5 rounded-md px-2.5 text-start text-base text-text transition hover:bg-hover pointer-coarse:min-h-11"
                onMouseDown={keepTheSelection}
                onClick={() => {
                    setHeaded(!headed);
                }}
            >
                <Icon
                    name={headed ? 'check_box' : 'check_box_outline_blank'}
                    className="size-icon-lg shrink-0 text-accent-deep"
                />
                <span>{translate('compose.tableHeaderRow')}</span>
            </button>

            <p className="px-2.5 py-0.5 text-sm text-muted">
                {rows > previewRows || columns > previewColumns ? `${translate('compose.tablePreviewCut')} ` : ''}
                {translate('compose.tableBounds', { rows: counted(mostTableRows), columns: counted(mostTableColumns) })}
            </p>

            <button
                type="button"
                role="menuitem"
                tabIndex={-1}
                className="mx-2.5 mt-1.5 mb-0.5 flex min-h-8 items-center justify-center rounded-md bg-accent text-base font-semibold text-on-accent transition hover:bg-accent-strong pointer-coarse:min-h-11"
                onMouseDown={keepTheSelection}
                onClick={() => {
                    onInsert(rows, columns, headed);
                }}
            >
                {translate('compose.tableInsert', { rows: counted(rows), columns: counted(columns) })}
            </button>
        </>
    );
}

/** One count the menu asks for, between a box taking one away and one adding one, each saying why where it cannot. */
function TableCount({
    named,
    value,
    counted,
    most,
    fewer,
    more,
    onCount,
}: {
    readonly named: string;
    readonly value: number;
    readonly counted: string;
    readonly most: number;
    readonly fewer: string;
    readonly more: string;
    readonly onCount: (value: number) => void;
}) {
    return (
        <div role="group" aria-label={named} className="flex items-center gap-2.5 px-2.5 py-1 text-base text-text">
            <span className="flex-1">{named}</span>
            <CountStep
                icon="remove"
                named={fewer}
                available={value > 1}
                onStep={() => {
                    onCount(value - 1);
                }}
            />
            <span aria-live="polite" className="min-w-7 text-center text-lg font-semibold tabular-nums">
                {counted}
            </span>
            <CountStep
                icon="add"
                named={more}
                available={value < most}
                onStep={() => {
                    onCount(value + 1);
                }}
            />
        </div>
    );
}

function CountStep({
    icon,
    named,
    available,
    onStep,
}: {
    readonly icon: IconName;
    readonly named: string;
    readonly available: boolean;
    readonly onStep: () => void;
}) {
    return (
        <button
            type="button"
            role="menuitem"
            tabIndex={-1}
            aria-label={named}
            aria-disabled={available ? undefined : true}
            className={`flex size-7.5 shrink-0 items-center justify-center rounded-md border border-line transition pointer-coarse:size-11 ${
                available ? 'text-text-soft hover:bg-hover' : 'cursor-not-allowed opacity-45'
            }`}
            onMouseDown={keepTheSelection}
            onClick={() => {
                if (available) {
                    onStep();
                }
            }}
        >
            <Icon name={icon} className="size-icon-lg" />
        </button>
    );
}

/** What the table's strip can ask for. */
export type TableAct =
    'rowAbove' | 'rowBelow' | 'columnBefore' | 'columnAfter' | 'removeRow' | 'removeColumn' | 'delete';

interface StripControl {
    readonly act: TableAct;
    readonly named: MessageKey;
}

// The design's own seven, in its order, with the divider after the four that add something.
const adding: readonly StripControl[] = [
    { act: 'rowAbove', named: 'compose.tableRowAbove' },
    { act: 'rowBelow', named: 'compose.tableRowBelow' },
    { act: 'columnBefore', named: 'compose.tableColumnBefore' },
    { act: 'columnAfter', named: 'compose.tableColumnAfter' },
];

const removing: readonly StripControl[] = [
    { act: 'removeRow', named: 'compose.tableRemoveRow' },
    { act: 'removeColumn', named: 'compose.tableRemoveColumn' },
];

/** The table's controls, which stand while the caret is in a cell. Words rather than symbols, since none names them. */
export function TableStrip({ onAct }: { readonly onAct: (act: TableAct) => void }) {
    const { translate } = useLocalization();

    return (
        <div
            role="toolbar"
            aria-label={translate('compose.tableStrip')}
            className="flex shrink-0 touch-strip flex-wrap items-center gap-0.5 border-b border-line-soft bg-sunken px-3 py-1.25 pointer-coarse:gap-1.5 pointer-coarse:py-2"
        >
            <span className="shrink-0 ps-0.5 pe-2 text-sm font-semibold text-muted">
                {translate('compose.tableStrip')}
            </span>
            {adding.map((control) => (
                <StripButton
                    key={control.act}
                    onPress={() => {
                        onAct(control.act);
                    }}
                >
                    {translate(control.named)}
                </StripButton>
            ))}
            <span
                aria-hidden="true"
                className="mx-1.25 h-4.5 w-px shrink-0 bg-line pointer-coarse:mx-1 pointer-coarse:h-7"
            />
            {removing.map((control) => (
                <StripButton
                    key={control.act}
                    onPress={() => {
                        onAct(control.act);
                    }}
                >
                    {translate(control.named)}
                </StripButton>
            ))}
            <StripButton
                danger
                onPress={() => {
                    onAct('delete');
                }}
            >
                <Icon name="delete" className="size-icon-md pointer-coarse:size-icon-lg" />
                {translate('compose.tableDelete')}
            </StripButton>
        </div>
    );
}

function StripButton({
    danger,
    onPress,
    children,
}: {
    readonly danger?: true;
    readonly onPress: () => void;
    readonly children: ReactNode;
}) {
    return (
        <button
            type="button"
            className={`flex min-h-7 shrink-0 items-center gap-1.5 rounded-md px-2.25 text-sm whitespace-nowrap transition hover:bg-hover pointer-coarse:min-h-11 pointer-coarse:border pointer-coarse:border-line pointer-coarse:px-3 pointer-coarse:text-base ${
                danger === true ? 'text-error-text' : 'text-text-soft'
            }`}
            onMouseDown={keepTheSelection}
            onClick={onPress}
        >
            {children}
        </button>
    );
}

// Every control here acts on the caret's place in the region, and a press that took the focus there would move it.
function keepTheSelection(event: { preventDefault: () => void }): void {
    event.preventDefault();
}
