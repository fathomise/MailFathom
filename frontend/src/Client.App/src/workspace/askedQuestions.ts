// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// What Discover was asked before, offered back so that asking again is one press rather than retyping the sentence.
// A question carries its words alone: Discover always asks about all of the reader's mail, so there is no scope to
// remember beside it and nothing the Mail space had open, picked out, or highlighted travels with it.

/** A question Discover was asked. */
export interface AskedQuestion {
    readonly question: string;
}

/**
 * How many questions are offered back before the oldest is dropped.
 *
 * Read where a store is read back and written by the field that keeps them, so the bound one tab writes under and the
 * bound a stored list is held to are one number rather than two that drift.
 */
export const mostAskedQuestions = 8;

/**
 * The questions asked before with this one at the front, held to what one tab may accumulate.
 *
 * A question asked again moves rather than repeating, which is what keeps the list short without anything having to
 * look for a duplicate before writing one — `search/MailSearch.tsx` keeps what was searched for the same way. The entry
 * is a new object on every press, which is what `discover/useStartedRun.ts` starts a run for.
 */
export function withAsked(asked: readonly AskedQuestion[], question: string): readonly AskedQuestion[] {
    return [{ question }, ...asked.filter((before) => before.question !== question)].slice(0, mostAskedQuestions);
}
