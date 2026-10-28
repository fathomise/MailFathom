// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useEffectEvent, useRef, useState, useSyncExternalStore } from 'react';
import {
    mostReconnectionAttempts,
    readDeploymentSession,
    readMailAccounts,
    reconnectionDelay,
    wentUnanswered,
    type ClientFailure,
    type ClientResult,
    type ClientSession,
    type DeploymentSession,
    type MailAccountDirectory,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { requestsInFlight, type RequestsInFlight } from '../deployment/requestsInFlight';
import type { DeploymentTransport } from '../deployment/sendToDeployment';
import { offers } from './capabilities';
import { probeLiveness, type LivenessProbe, type ProbeOutcome } from './livenessProbe';

// What the client knows about the deployment it is signed in to: what the credential may do there, how current each of
// the user's accounts is, and whether the deployment is answering at all. It is one thing rather than three because it
// arrives as one exchange — the grant decides whether the accounts are asked for at all, and a deployment that stopped
// answering stops both.
//
// The session is re-read on every attempt rather than kept from the sign-in that produced it. A grant is the
// deployment's to narrow while the client is open, and a client acting on the grant it was handed at sign-in would keep
// offering what the service has since begun refusing.

// The schedule is the package's rather than this hook's: the signal channel reopens on the same one, and two
// definitions of how long to wait would be two answers to one question.
export { mostReconnectionAttempts, reconnectionDelay };

/** What the deployment last said, and how current what is on the screen therefore is. */
export interface Connection {
    /** What the deployment says the credential may do, or `null` while that is being read for the first time on this attempt. */
    readonly session: ClientResult<DeploymentSession> | null;

    /** The user's accounts, or `null` where they have not been read — which includes a credential not allowed to. */
    readonly accounts: ClientResult<MailAccountDirectory> | null;

    /** Whether this machine has a network at all, which is a different sentence from a deployment that is not answering. */
    readonly online: boolean;

    /** How many automatic attempts have been made since the deployment last answered. */
    readonly attempts: number;

    /**
     * Since when the deployment has not answered a read, whether or not that read left the frame standing, or `null`
     * while it answers.
     *
     * It is said apart from `session` because a refresh nobody answered keeps the answer before it on the screen, so
     * the frame alone would go on claiming a deployment that has gone quiet. The connection status reads it.
     */
    readonly unansweredSince: number | null;

    /** When the accounts last answered, which is when the connection status first saw whatever they said. */
    readonly accountsReadAt: number | null;

    /** Reads everything again, from a person asking rather than from the client trying on its own. */
    readonly reread: () => void;

    /**
     * The signal channel dropped, which is SignalR saying the link carried nothing for thirty seconds — so the server
     * is asked at once whether it is still there, through the liveness probe this hook holds.
     */
    readonly channelDropped: () => void;

    /**
     * Reads everything again on the client's own account — a refresh — leaving what stands on the screen where the
     * deployment does not answer: a refresh that failed is one the next repeats, not a frame replaced by a deployment
     * that stopped answering. It is also a fresh start for the automatic attempts, which is how a deployment that has
     * gone quiet past them is reached again at the next periodic refresh, on *Retry*, and on *Refresh*.
     */
    readonly refresh: () => void;
}

/**
 * Who this client is signed in as, and what it presents for them right now.
 *
 * The two are separate because only one of them lasts. A session token is replaced while somebody is reading — the
 * client renews before it expires — so what names a sign-in is the person and the deployment rather than the value
 * being presented, and everything this hook keys on is that name.
 */
export interface SignedInCaller {
    /** What this sign-in is, which a renewal does not change and signing in as somebody else does. */
    readonly identity: string;

    /** The finished header value to present on the next request, which is whatever the client holds at that moment. */
    readonly authorization: string;
}

/**
 * What one attempt answered, tagged with the address and the identity it answered for.
 *
 * **The attempt is deliberately not part of that tag.** A re-read is asked for while somebody is reading — the account
 * signals one every time a synchronization run finishes — and an answer discarded for belonging to the previous
 * attempt would empty the whole frame for as long as the new read takes: every screen below unmounts, every read they
 * have in flight is abandoned and reported as a deployment that is not answering, the list returns to the top of the
 * folder, and the signal channel is closed and opened again. What is on the screen is still the truest thing anybody
 * has until the next answer replaces it, which is what this hook already says about a machine that lost its network.
 *
 * The identity is what a stale answer is actually told apart by, and it is what matters: signing out and back in as
 * somebody else changes who is signed in, so an answer left standing across that would put the previous user's
 * accounts and the previous user's grants in front of the next person. What it holds is the identity rather than the
 * credential — the credential is deliberately the thing not compared, a renewal replacing it while nothing about the
 * deployment changed — and it is compared, never read, and never rendered.
 */
interface Answered {
    readonly session: ClientResult<DeploymentSession> | null;
    readonly accounts: ClientResult<MailAccountDirectory> | null;
    readonly presentedAt: string | null;
    readonly presenting: string | null;
}

// Before the first attempt there is nothing, tagged with an address and an identity no read will ever carry.
const nothingRead: Answered = {
    session: null,
    accounts: null,
    presentedAt: null,
    presenting: null,
};

/**
 * The accounts a previous attempt answered with, where they were answered for this same address and identity.
 *
 * They stand while the accounts of the attempt now in flight are still being read, for the reason {@link Answered}
 * gives: a folder tree emptied on every re-read is a folder tree emptied every time an account finishes a run. An
 * answer for another address or another person is not kept, which is the same comparison that decides what is drawn.
 *
 * A grant that no longer reads mail keeps nothing either, and it is the one case that is not about who is reading. The
 * attempt holding it asks for no accounts at all, so nothing later in it replaces what stands — a tree left up here
 * would go on offering mail the deployment has begun refusing, for as long as that person stayed signed in.
 */
function accountsStillStanding(
    previous: Answered,
    baseAddress: string,
    presenting: string,
    readsMail: boolean,
): ClientResult<MailAccountDirectory> | null {
    return readsMail && previous.presentedAt === baseAddress && previous.presenting === presenting
        ? previous.accounts
        : null;
}

/** Whether what stands was read, and for this same address and identity, which is what a refresh that failed leaves up. */
function readFor(previous: Answered, baseAddress: string, presenting: string): boolean {
    return (
        previous.presentedAt === baseAddress &&
        previous.presenting === presenting &&
        previous.session?.outcome === 'read'
    );
}

/**
 * How many automatic attempts have been made, and against which identity.
 *
 * The budget belongs to the identity that spent it for the same reason an answer does. Counting it on its own would
 * carry a spent budget across a sign-out: the next person's first read would be announced as somebody else's fifth,
 * and once the count had reached its ceiling they would be told the deployment has stopped answering before their own
 * read had failed even once — with no automatic attempt left to correct it.
 */
interface Reaching {
    readonly made: number;
    readonly presentedAt: string | null;
    readonly presenting: string | null;
}

// Nothing spent, against an identity no credential will ever match, which is also what a person asking again resets to.
const noneMade: Reaching = { made: 0, presentedAt: null, presenting: null };

/**
 * Which sign-in a read went unanswered for, and since when.
 *
 * Tagged with the address and the identity for the reason an answer is: a deployment that went quiet for one person is
 * nothing to say to the next, who has not asked it anything yet.
 */
interface Unanswered {
    readonly presentedAt: string;
    readonly presenting: string;
    readonly since: number;
}

function subscribeToConnectivity(changed: () => void): () => void {
    window.addEventListener('online', changed);
    window.addEventListener('offline', changed);

    return () => {
        window.removeEventListener('online', changed);
        window.removeEventListener('offline', changed);
    };
}

function isOnline(): boolean {
    return window.navigator.onLine;
}

/**
 * Holds what the deployment says, and reaches for it again on its own while it says nothing.
 *
 * @param baseAddress Where the deployment is, or `null` where none has been adopted.
 * @param signedIn Who is signed in and what to present for them, or `null` where nobody is.
 * @param send How a request reaches the deployment.
 * @param onCredentialRefused What to do about a credential the deployment has stopped accepting, which is the one
 * answer this hook reports rather than renders: it is acted on once, by whatever owns signing in, instead of producing
 * the same refusal on every later read.
 * @param inFlight The requests the liveness probe asks about, and abandons once the server is lost.
 * @returns Everything a screen needs to say what it is looking at and how current it is.
 */
export function useConnection(
    baseAddress: string | null,
    signedIn: SignedInCaller | null,
    send: DeploymentTransport,
    onCredentialRefused: () => void,
    inFlight: RequestsInFlight = requestsInFlight,
): Connection {
    const online = useSyncExternalStore(subscribeToConnectivity, isOnline);

    // What is read again is decided by who is signed in, and what is presented is decided when a request goes out.
    // They are two different things because a session is renewed while somebody is reading: the header value changes
    // every eleven hours and the person behind it does not, so keying anything on the value would empty the screen,
    // move focus, and re-read everything at an instant nothing happened at.
    const presenting = signedIn?.identity ?? null;
    const authorization = signedIn?.authorization ?? null;
    const carried = useRef(authorization);

    // Whether the next read is a refresh, handed from the call asking for one to the read it starts. A ref because
    // nothing is drawn from it and setting it must not start a read of its own.
    const quietly = useRef(false);

    // Whether the next read is one of the automatic attempts below, handed over the same way and for the same reason.
    const automatically = useRef(false);
    const [read, setRead] = useState(0);
    const [reaching, setReaching] = useState<Reaching>(noneMade);
    const [answered, setAnswered] = useState<Answered>(nothingRead);
    const [standingUnanswered, setStandingUnanswered] = useState<Unanswered | null>(null);
    const [accountsReadAt, setAccountsReadAt] = useState<number | null>(null);

    // Worked out during a render for the same reason the answer is: a budget spent against one identity is nothing to
    // the next one, so signing in as somebody else starts at nothing without an effect having to clear it.
    const attempts = reaching.presentedAt === baseAddress && reaching.presenting === presenting ? reaching.made : 0;

    // Declared above the read below, so a commit carrying both a fresh identity and a fresh header value has the
    // value in hand before the read that will present it runs: effects run in the order they are written.
    useEffect(() => {
        carried.current = authorization;
    }, [authorization]);

    useEffect(() => {
        // Nothing is read without a network, and what was read before it went is left on the screen rather than
        // cleared: the last answer is still the truest thing anybody has, and saying so beside it is what the offline
        // state is for. Coming back re-runs this, which is the whole of the automatic recovery from that direction.
        if (baseAddress === null || presenting === null || !online) {
            return;
        }

        // Taken by the read it was set for, so every read after it — a person's — is not a refresh unless it was asked
        // for as one. An automatic attempt keeps what stands too: it is made because a read went unanswered, and where
        // that read had left the frame standing, the attempt after it is no reason to take the frame down.
        const retriedByCaller = automatically.current;
        automatically.current = false;
        const keepsWhatStands = quietly.current || retriedByCaller;
        quietly.current = false;

        // A read nobody answered is said here rather than by the frame, which may still show the answer before it.
        // Any answer ends it, and only the last read of an attempt says so — a session read answering while the
        // accounts beside it go unanswered is the same silence, not a moment of the server being back.
        const heard = (failure: ClientFailure | null): void => {
            if (failure === null || !wentUnanswered(failure)) {
                setStandingUnanswered(null);

                return;
            }

            // The first read nobody answered is when it began; the attempts after it are the same silence.
            setStandingUnanswered((previous) =>
                previous?.presentedAt === baseAddress && previous.presenting === presenting
                    ? previous
                    : { presentedAt: baseAddress, presenting, since: Date.now() },
            );
        };

        // The budget is spent until the whole attempt has answered: a session read answering while the accounts beside
        // it go unanswered is the same silence, and starting the count over there would reach for it without end.
        const answeredWhole = (): void => {
            setReaching({ made: 0, presentedAt: baseAddress, presenting });
        };

        // Abandoning is what says an answer is nobody's to render any more, and it is one mechanism rather than two:
        // the signal already has to travel to the transport, so a second flag beside it would be a second thing to
        // keep true.
        const attempted = new AbortController();
        const onTheWire = send(attempted.signal);

        // The first read after an answer is retried by the transport like every other, so a deployment that blinks is
        // never reported lost. Once it has been, the schedule below is what reaches for it, and each of its attempts is
        // a liveness probe: on the wire once rather than retried a second time underneath, ahead of the requests a dead
        // link left hanging, and given up after its bound rather than waiting on a dead connection for minutes.
        const transport: MailFathomTransport = retriedByCaller
            ? (request) => onTheWire({ ...request, retriedByCaller: true, probes: true })
            : onTheWire;

        // Read per request rather than once per effect, so a renewal landing between the two reads below presents the
        // token the deployment now holds. The one it replaced stopped working the moment the renewal answered.
        const credential = (): ClientSession => ({ baseAddress, authorization: carried.current ?? '' });

        // Asked through a function rather than read off the controller, so nothing decides at the first check that it
        // can never be true at the second: what changes it is a cleanup running while a read is in flight.
        const abandoned = (): boolean => attempted.signal.aborted;

        // A token this client replaced while the request was on the wire was refused for having been replaced rather
        // than for the person behind it: the renewal that destroyed it minted its successor in the same answer. So the
        // read is made again with what is held now, and the session somebody still has is not discarded under them.
        const refused = (presented: ClientSession): void => {
            if (presented.authorization === carried.current) {
                onCredentialRefused();

                return;
            }

            setRead((token) => token + 1);
        };

        void (async () => {
            const presentedToRead = credential();
            const session = await readDeploymentSession(presentedToRead, transport);

            if (abandoned()) {
                return;
            }

            if (session.outcome === 'failed' && session.failure.reason === 'unauthenticated') {
                refused(presentedToRead);

                return;
            }

            if (session.outcome === 'failed') {
                heard(session.failure);

                const unanswered: Answered = {
                    session,
                    accounts: null,
                    presentedAt: baseAddress,
                    presenting,
                };

                setAnswered((previous) =>
                    keepsWhatStands && readFor(previous, baseAddress, presenting) ? previous : unanswered,
                );

                return;
            }

            const readsMail = offers(session.value, 'readMail');

            // On the screen as soon as it is known rather than once the accounts beside it are: what it decides — the
            // spaces, the controls, the deployment's version — is answerable now, and holding it back would leave the
            // frame saying it is still reaching a deployment that has already answered.
            //
            // The accounts a previous attempt answered with stand until this attempt's replace them, which is what
            // makes a re-read invisible to whoever is reading: the folder tree keeps its folders and their counts, and
            // the freshness line keeps what those said rather than blanking twice per run.
            setAnswered((previous) => ({
                session,
                accounts: accountsStillStanding(previous, baseAddress, presenting, readsMail),
                presentedAt: baseAddress,
                presenting,
            }));

            // A credential that may not read mail is never asked for it. The refusal would be the service's to give
            // and it would arrive as a failure on a screen, where what is true is that the client is not offering
            // something rather than that something went wrong.
            if (!readsMail) {
                answeredWhole();
                heard(null);

                return;
            }

            const presentedToList = credential();
            const accounts = await readMailAccounts(presentedToList, transport);

            if (abandoned()) {
                return;
            }

            if (accounts.outcome === 'failed' && accounts.failure.reason === 'unauthenticated') {
                refused(presentedToList);

                return;
            }

            heard(accounts.outcome === 'failed' ? accounts.failure : null);

            if (accounts.outcome === 'read' || !wentUnanswered(accounts.failure)) {
                answeredWhole();
            }

            // The accounts the previous attempt answered with already stand, above, so a refresh that could not read
            // them again leaves them there. Where none had been read, the failure is what there is to say.
            setAnswered((previous) =>
                keepsWhatStands && accounts.outcome === 'failed' && previous.accounts?.outcome === 'read'
                    ? previous
                    : { session, accounts, presentedAt: baseAddress, presenting },
            );

            if (accounts.outcome === 'read') {
                setAccountsReadAt(Date.now());
            }
        })();

        return () => {
            attempted.abort();
        };
    }, [baseAddress, presenting, online, read, send, onCredentialRefused]);

    // An answer given for another address, or for a credential this client is no longer signed in with, is nobody's to
    // draw: what a person is looking at then is a read in flight, which is what the frame says while it waits. An
    // answer for this same address and this same person stands whichever attempt produced it, for the reason
    // `Answered` gives — a re-read is not a sign-out, and emptying the frame for one is what made a finished
    // synchronization run look like the page reloading.
    const current = answered.presentedAt === baseAddress && answered.presenting === presenting;
    const connection = current ? answered : nothingRead;

    const unanswered =
        online &&
        standingUnanswered !== null &&
        standingUnanswered.presentedAt === baseAddress &&
        standingUnanswered.presenting === presenting;
    const unansweredSince = unanswered ? standingUnanswered.since : null;

    // Only a deployment that did not answer is reached for again. A credential it refused, a grant it does not hold,
    // and an answer this client could not read each repeat identically however many times they are asked for, so the
    // budget is spent on the one failure that passes on its own. A refresh nobody answered is reached for the same
    // way, under the frame it left standing.
    const lost =
        (connection.session?.outcome === 'failed' && connection.session.failure.reason === 'unavailable' && online) ||
        unanswered;

    useEffect(() => {
        if (!lost || attempts >= mostReconnectionAttempts) {
            return;
        }

        const waiting = setTimeout(
            () => {
                automatically.current = true;
                setReaching({ made: attempts + 1, presentedAt: baseAddress, presenting });
                setRead((token) => token + 1);
            },
            reconnectionDelay(attempts, Math.random()),
        );

        return () => {
            clearTimeout(waiting);
        };
    }, [lost, attempts, baseAddress, presenting]);

    // The abandoning itself waits for the status to have said the server is lost, so what each abandoned request fails
    // into is a client whose status already says it — which is what lets the failure leave the saying to the status.
    const abandoning = useRef(false);

    // The liveness probe is this same session read, asked quietly and as a probe: one attempt, ahead of any queue, and
    // bounded. Any answer at all says the server is there — a refusal from its limiter or a gateway's `503` included —
    // so a server refusing the probe under load is never called lost. Only silence is a probe nobody answered.
    const askWhetherThere = useEffectEvent(async (): Promise<ProbeOutcome> => {
        if (baseAddress === null || presenting === null) {
            return 'answered';
        }

        const onTheWire = send(new AbortController().signal);
        const session = await readDeploymentSession({ baseAddress, authorization: carried.current ?? '' }, (request) =>
            onTheWire({ ...request, probes: true }),
        );

        return session.outcome === 'read' || session.failure.status !== null ? 'answered' : 'missed';
    });

    // Two probes in a row nobody answered: the server is lost from the first of them, and every request still waiting
    // on it is abandoned — a read ends as the failure it is, and a write as one whose outcome is not known — except one
    // whose body is still visibly moving, which ends on its own terms.
    const gaveUpOnServer = useEffectEvent((since: number) => {
        if (baseAddress === null || presenting === null) {
            return;
        }

        setStandingUnanswered((previous) =>
            previous?.presentedAt === baseAddress && previous.presenting === presenting
                ? previous
                : { presentedAt: baseAddress, presenting, since },
        );
        abandoning.current = true;
    });

    const liveness = useRef<LivenessProbe | null>(null);

    useEffect(() => {
        if (baseAddress === null || presenting === null || !online) {
            return;
        }

        const probe = probeLiveness(
            inFlight,
            () => askWhetherThere(),
            (since) => {
                gaveUpOnServer(since);
            },
        );

        liveness.current = probe;

        return () => {
            probe.stop();
            liveness.current = null;

            // A server given up on for one sign-in is nothing to abandon the next one's requests over.
            abandoning.current = false;
        };
    }, [baseAddress, presenting, online, inFlight]);

    // While the server is lost the probe asks nothing: reaching for it again is the schedule above, whose reads are
    // probes already, and a second schedule for the same question is what `reconnection.ts` exists to rule out.
    useEffect(() => {
        liveness.current?.lost(lost);

        if (lost && abandoning.current) {
            abandoning.current = false;
            inFlight.abandonStill();
        }
    }, [lost, baseAddress, presenting, online, inFlight]);

    return {
        session: connection.session,
        accounts: connection.accounts,
        online,
        attempts,
        unansweredSince,
        accountsReadAt: current ? accountsReadAt : null,
        channelDropped: () => {
            liveness.current?.channelDropped();
        },
        reread: () => {
            quietly.current = false;
            setReaching(noneMade);
            setRead((token) => token + 1);
        },
        refresh: () => {
            quietly.current = true;
            setReaching(noneMade);
            setRead((token) => token + 1);
        },
    };
}
