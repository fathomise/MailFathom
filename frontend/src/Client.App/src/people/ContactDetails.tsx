// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useState, type MouseEvent } from 'react';
import type { Contact } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { useLinkOpener } from '../shellOperations/linkOpener';
import { phoneLabelSaid, telephoneLink } from './phoneNumbers';

// How to reach one person, as the design's *Contact details* card draws it: every address they use, the preferred one
// marked where there is a choice, and every phone number as a link a phone can call.
//
// A number is opened through the application's one operation for leaving itself rather than by the anchor navigating,
// because a WebView that navigates has replaced the application: the head decides what a telephone address opens, and a
// head that cannot open one says so under the number.
//
// A collected contact holds no numbers and never could — nothing collected from mail headers carries one — so its card
// says nothing about phones at all rather than reporting an absence that is not a gap.

export function ContactDetails({ contact }: { readonly contact: Contact }) {
    const { translate } = useLocalization();
    const openLink = useLinkOpener();
    const [unopened, setUnopened] = useState<number | null>(null);
    const asserted = contact.origin === 'Asserted';

    function call(pressed: MouseEvent<HTMLAnchorElement>, number: string, position: number): void {
        pressed.preventDefault();
        setUnopened(null);

        void openLink({ target: telephoneLink(number), picture: false, words: number, warnings: [] }).catch(() => {
            setUnopened(position);
        });
    }

    return (
        <section className="flex min-w-0 flex-col gap-2.5 rounded-lg border border-line bg-panel px-4.25 py-3.75">
            <h3 className="text-xs leading-natural font-semibold text-muted">{translate('people.contactDetails')}</h3>

            <ul className="flex flex-col gap-2.5">
                {contact.addresses.map((address) => (
                    <li key={address} className="flex min-w-0 items-center gap-2.5 border-t border-line-soft pt-2.25">
                        <Icon name="mail" className="size-icon-md shrink-0 text-muted" />
                        <span className="min-w-0 flex-1 text-md leading-natural wrap-anywhere">{address}</span>

                        {address === contact.preferredAddress && contact.addresses.length > 1 ? (
                            <span className="shrink-0 rounded-md bg-accent-soft px-1.75 py-0.5 text-xs font-semibold text-accent-deep">
                                {translate('people.preferredAddress')}
                            </span>
                        ) : null}
                    </li>
                ))}

                {asserted
                    ? contact.phoneNumbers.map((phoneNumber, position) => (
                          // Numbers are held in the person's order and may repeat, so the place is the identity.
                          <li
                              key={position}
                              className="flex min-w-0 flex-wrap items-center gap-x-2.5 gap-y-1 border-t border-line-soft pt-2.25"
                          >
                              <Icon name="call" className="size-icon-md shrink-0 text-muted" />
                              <a
                                  href={telephoneLink(phoneNumber.number)}
                                  className="min-w-0 flex-1 text-md leading-natural text-accent-deep tabular-nums underline underline-offset-2"
                                  onClick={(pressed) => {
                                      call(pressed, phoneNumber.number, position);
                                  }}
                              >
                                  {phoneNumber.number}
                              </a>
                              <span className="shrink-0 text-sm leading-natural text-muted">
                                  {translate(phoneLabelSaid[phoneNumber.label])}
                              </span>
                              {unopened === position ? (
                                  <span role="status" className="basis-full text-sm text-warning">
                                      {translate('link.couldNotOpen')}
                                  </span>
                              ) : null}
                          </li>
                      ))
                    : null}

                {asserted && contact.phoneNumbers.length === 0 ? (
                    <li className="border-t border-line-soft pt-2.25 text-base leading-natural text-faint">
                        {translate('people.noPhoneNumbers')}
                    </li>
                ) : null}
            </ul>
        </section>
    );
}
