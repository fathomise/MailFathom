// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ContactPhoneLabel } from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';

// What the People screen does with a phone number beside showing it: names its label, links it, and asks whether
// what somebody typed is shaped like one before the form offers to save it.

/** What each label the service publishes is called on a screen. */
export const phoneLabelSaid: Readonly<Record<ContactPhoneLabel, MessageKey>> = {
    Mobile: 'people.phoneLabel.Mobile',
    Work: 'people.phoneLabel.Work',
    Home: 'people.phoneLabel.Home',
    Other: 'people.phoneLabel.Other',
};

/** The `tel:` link a number is called through, which keeps the digits and a leading plus and drops the grouping. */
export function telephoneLink(number: string): string {
    return `tel:${number.replace(/[^+\d]/gu, '')}`;
}

/**
 * Whether typed text is shaped like a number the service keeps: digits, with the grouping people write them in, at most
 * as long as the service allows. The service judges it again, and this only spares a request it would refuse.
 */
export function looksLikeAPhoneNumber(text: string): boolean {
    return text.length <= 32 && /^[\d+ ()\-.]+$/u.test(text) && /\d/u.test(text);
}
