// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useState } from 'react';
import { act, fireEvent, render, renderHook, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import {
    ScreenLayersContext,
    SpaceInFrontContext,
    useScreenLayer,
    useScreenLayerStack,
    useScreenStep,
    useTopLayerSurface,
    type ScreenLayers,
} from './screenLayers';

// One surface standing over the screen, drawn as what a reader would see of it and left by a control of its own. It is
// a paragraph and a button rather than a dialog because what is proven here is the order the shell closes them in, and
// jsdom implements none of the platform's own top layer — so the element is whatever the surface would have been.
function Standing({
    name,
    leaving,
    open,
    onClose,
}: {
    readonly name: string;
    readonly leaving: string;
    readonly open: boolean;
    readonly onClose: () => void;
}) {
    useScreenLayer(open, onClose);

    return open ? (
        <div>
            <p>{name}</p>
            <button type="button" onClick={onClose}>
                {leaving}
            </button>
        </div>
    ) : null;
}

// Not catalogue entries: each stands for a surface the shell would have drawn, and what they say is what a test reads.
const drawer = 'The mailboxes in a drawer';
const leaveTheDrawer = 'Close the drawer';
const question = 'A question asked over the drawer';
const leaveTheQuestion = 'Close the question';

// The composer's own shape: closing it with something written in it asks a question rather than taking it away, so the
// surface is still on the screen after the press that reached it and has to be recorded again.
function Asking() {
    const [timesAsked, setTimesAsked] = useState(0);

    useScreenLayer(
        true,
        () => {
            setTimesAsked((times) => times + 1);
        },
        timesAsked,
    );

    // How many times it has been asked, drawn on its own: this surface is the whole of what a test renders here, so
    // the count is unambiguous without a sentence around it.
    return <p>{String(timesAsked)}</p>;
}

/** A drawer with a question standing over it, which is the arrangement every ordering behaviour is asked about. */
function Surfaces({
    layers,
    drawerOpen = true,
    questionOpen = true,
}: {
    readonly layers: ScreenLayers;
    readonly drawerOpen?: boolean;
    readonly questionOpen?: boolean;
}) {
    const [drawerStanding, setDrawerStanding] = useState(drawerOpen);
    const [questionStanding, setQuestionStanding] = useState(questionOpen);

    return (
        <ScreenLayersContext value={layers}>
            <Standing
                name={drawer}
                leaving={leaveTheDrawer}
                open={drawerStanding}
                onClose={() => {
                    setDrawerStanding(false);
                }}
            />
            <Standing
                name={question}
                leaving={leaveTheQuestion}
                open={questionStanding}
                onClose={() => {
                    setQuestionStanding(false);
                }}
            />
        </ScreenLayersContext>
    );
}

// The stack is built through its own hook and the surfaces are drawn under it, so that a test can ask the shell to
// close one the way the back gesture does rather than the way a control does. Its three functions are the same ones for
// the life of the stack, so the value handed to the provider stays current however the count moves.
function shellWith(standing: { readonly drawerOpen?: boolean; readonly questionOpen?: boolean } = {}): ScreenLayers {
    const { result } = renderHook(() => useScreenLayerStack());

    render(<Surfaces layers={result.current} {...standing} />);

    return result.current;
}

describe('useScreenLayerStack', () => {
    it('counts nothing standing over a screen nobody has opened anything on', () => {
        const { result } = renderHook(() => useScreenLayerStack());

        expect(result.current.depth).toBe(0);
    });

    it('answers that there was nothing to close where nothing stands over the screen', () => {
        const { result } = renderHook(() => useScreenLayerStack());
        let closed = true;

        act(() => {
            closed = result.current.closeTop();
        });

        expect(closed).toBe(false);
    });
});

describe('useScreenLayer', () => {
    it('closes the surface opened last, which is the one standing on top', () => {
        const layers = shellWith();

        act(() => {
            layers.closeTop();
        });

        expect(screen.queryByText(question)).toBeNull();
        expect(screen.getByText(drawer)).toBeTruthy();
    });

    it('closes one surface per step, so the one underneath is reached by the step after it', () => {
        const layers = shellWith();

        act(() => {
            layers.closeTop();
        });
        act(() => {
            layers.closeTop();
        });

        expect(screen.queryByText(drawer)).toBeNull();
    });

    it('closes every surface at once, which is what going to another destination does to them', () => {
        const layers = shellWith();

        act(() => {
            layers.closeEvery();
        });

        expect(screen.queryByText(drawer)).toBeNull();
        expect(screen.queryByText(question)).toBeNull();
    });

    it('stops counting a surface that was closed by its own control', () => {
        const { result } = renderHook(() => useScreenLayerStack());
        render(<Surfaces layers={result.current} />);

        fireEvent.click(screen.getByRole('button', { name: leaveTheQuestion }));

        expect(result.current.depth).toBe(1);
    });

    it('registers nothing for a surface that is mounted while it is closed', () => {
        const { result } = renderHook(() => useScreenLayerStack());
        render(<Surfaces layers={result.current} drawerOpen={false} questionOpen={false} />);

        expect(result.current.depth).toBe(0);
    });

    // The press is spent on the surface it reached, so a surface that answered with a question rather than by going
    // away has to be recorded afresh — otherwise the press after it goes straight past a composer still on the screen.
    it('records a surface again where closing it asked a question instead of taking it away', () => {
        const { result } = renderHook(() => useScreenLayerStack());
        render(
            <ScreenLayersContext value={result.current}>
                <Asking />
            </ScreenLayersContext>,
        );

        act(() => {
            result.current.closeTop();
        });
        act(() => {
            result.current.closeTop();
        });

        expect(screen.getByText('2')).toBeTruthy();
    });

    it('leaves a surface drawn with no shell around it unchanged, rather than refusing to draw it', () => {
        render(<Standing name={question} leaving={leaveTheQuestion} open onClose={() => undefined} />);

        expect(screen.getByText(question)).toBeTruthy();
    });
});

// A view drawn in place of another inside a space, standing for as long as nobody went back from it. Not a catalogue
// entry for the reason the surfaces above are not.
const person = 'A person in front of the contact book';

function Replacing({
    inFront = true,
    withAQuestion = false,
}: {
    readonly inFront?: boolean;
    readonly withAQuestion?: boolean;
}) {
    const [standing, setStanding] = useState(true);
    const [asked, setAsked] = useState(withAQuestion);

    return (
        <SpaceInFrontContext value={inFront}>
            <Step
                standing={standing}
                onBack={() => {
                    setStanding(false);
                }}
            />
            <Standing
                name={question}
                leaving={leaveTheQuestion}
                open={asked}
                onClose={() => {
                    setAsked(false);
                }}
            />
        </SpaceInFrontContext>
    );
}

function Step({ standing, onBack }: { readonly standing: boolean; readonly onBack: () => void }) {
    useScreenStep(standing, onBack);

    return standing ? <p>{person}</p> : null;
}

function stepsWith(standing: { readonly inFront?: boolean; readonly withAQuestion?: boolean } = {}) {
    const stack = renderHook(() => useScreenLayerStack());
    const view = render(
        <ScreenLayersContext value={stack.result.current}>
            <Replacing {...standing} />
        </ScreenLayersContext>,
    );

    return { stack: stack.result, view };
}

describe('useScreenStep', () => {
    it('goes back from the view by its own way back when the gesture reaches it', () => {
        const { stack } = stepsWith();

        act(() => {
            stack.current.closeTop();
        });

        expect(screen.queryByText(person)).toBeNull();
    });

    it('stands under what was opened over it, so the gesture reaches that first', () => {
        const { stack } = stepsWith({ withAQuestion: true });

        act(() => {
            stack.current.closeTop();
        });

        expect(screen.queryByText(question)).toBeNull();
        expect(screen.getByText(person)).toBeTruthy();
    });

    it('stays where it is when the reader goes to another destination, where everything over it goes', () => {
        const { stack } = stepsWith({ withAQuestion: true });

        act(() => {
            stack.current.closeEvery();
        });

        expect(screen.queryByText(question)).toBeNull();
        expect(screen.getByText(person)).toBeTruthy();
        expect(stack.current.depth).toBe(1);
    });

    it('counts nothing while its space stands aside, and counts again once it is back in front', () => {
        const { stack, view } = stepsWith({ inFront: false });

        expect(stack.current.depth).toBe(0);

        view.rerender(
            <ScreenLayersContext value={stack.current}>
                <Replacing inFront />
            </ScreenLayersContext>,
        );

        expect(stack.current.depth).toBe(1);
    });
});

const surfaceName = 'A dialog the platform stands in its top layer';

function TopLayerDialog() {
    const closesOnBack = useTopLayerSurface();

    return <dialog aria-label={surfaceName} onToggle={closesOnBack} />;
}

/** The platform's own report that the dialog opened, which jsdom never sends and declares no `ToggleEvent` for. */
function reportOpened(dialog: HTMLElement): void {
    const opening = new Event('toggle');

    Object.defineProperty(opening, 'newState', { value: 'open' });

    act(() => {
        dialog.dispatchEvent(opening);
    });
}

describe('useTopLayerSurface', () => {
    it('stands over the screen from the moment the platform says the surface opened', () => {
        const { result } = renderHook(() => useScreenLayerStack());
        render(
            <ScreenLayersContext value={result.current}>
                <TopLayerDialog />
            </ScreenLayersContext>,
        );

        expect(result.current.depth).toBe(0);

        reportOpened(screen.getByRole('dialog', { hidden: true }));

        expect(result.current.depth).toBe(1);
    });

    it('closes the dialog the way Escape does when the back gesture reaches it', () => {
        const { result } = renderHook(() => useScreenLayerStack());
        render(
            <ScreenLayersContext value={result.current}>
                <TopLayerDialog />
            </ScreenLayersContext>,
        );
        const dialog = screen.getByRole<HTMLDialogElement>('dialog', { hidden: true });

        dialog.showModal();
        reportOpened(dialog);

        act(() => {
            result.current.closeTop();
        });

        expect(dialog.open).toBe(false);
    });
});
