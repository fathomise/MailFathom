# Mail footers

<!-- describes: backend/src/Application/Mail/Delivery/Footers/**, backend/src/Infrastructure/Mail/Mime/MailFooterMarkup.cs, backend/src/Infrastructure/Mail/Mime/Composition/MimeKitMailFooterComposer.cs, backend/src/Infrastructure/Persistence/Footers/**, backend/src/Application/Mail/Delivery/MailOutbox.cs -->

A footer is the block under a person's words in every message they send: their name, their role, a number to call, a
logo. Each person writes their own for each mail account they send from, and an organization may set one template that
every member's message from its accounts carries instead. Nothing an operator configures turns footers on: a person
with no footer sends without one. A person writes theirs over [the footer
routes](../operations/client-endpoint.md#the-footer-routes) of the client endpoint, and an administrator sets a template
over [the organization footer template
routes](../operations/admin-endpoint.md#the-organization-footer-template-routes) or with `mfctl`. No screen of the
client offers either yet.

## Whose footer a message carries

**A footer belongs to one person on one account.** It hangs off the assignment that gives the person the account, so on
a shared mailbox each person assigned it writes their own and a message carries the footer of whoever sent it. The
footer as written is deleted with that assignment and with the person. A message already sent keeps the footer it went
out with: its outbox record and its filed sent copy hold the message as sent, footer included, and leave with that mail
rather than with the assignment.

**While the organization holding the account sets a template, the template is the footer.** It is filled for the
person sending — `{{displayName}}`, `{{jobTitle}}`, and `{{phoneNumber}}`, from the organization's member directory,
each encoded for markup and empty where the member has none — and the person's own footer for that account is neither
sent nor writable until the template is cleared. An account in no organization, or in one with no template, sends the
person's own.

**What goes out is what is in force when the message is sent**, not when it was written. A draft sent days after it
was saved and each occasion of a recurring send carry the footer of that moment. A send held for later is different:
it is footed, screened, and signed when it is queued, so it carries the footer in force then, and a footer changed while
it waits does not reach it.

## Where it goes in a message

**Under the author's words and above anything they quote, once, in both parts.** In the plain text, the footer opens
with the RFC 3676 signature delimiter, a line of `-- ` alone, so every client that recognises a signature recognises
this one. In the markup it is wrapped in a `<footer>` element, which the allow-list every received message is read
through keeps — so the sent copy, read back out of the Sent folder, still marks where the person's words end. In an
answer, the footer sits between what the person wrote and the attribution line that opens the quotation.

**Every outgoing path carries it, and none carries it twice.** The footer goes in at the one place every send passes
through, the moment the outbox writes the message down: a new message, a reply, a forward, a draft sent from the
client or a tool, a held send, each occasion of a recurring one, a message an agent sends for the person, and an
automatic reply. A read receipt carries none, because a receipt is a report rather than a message somebody wrote, and
neither does a message sent by a caller acting for no person, such as the deployment administrator, because there is
nobody whose footer it could be.

**A draft never contains it.** It is put in on sending rather than on saving, so reading a draft back, editing it, and
sending it later never returns the footer as words the person wrote, and a footer changed in the meantime is the one
that goes out.

**It goes in before the organization's signature.** An organization that [signs its
mail](../operations/organization-signing-keys.md) signs the message with the footer already in it, so a footed message
verifies like any other. A footer that would carry a message past the largest one the deployment sends is left off
rather than refusing the message, the same answer the signature gives.

**It is screened with the message.** A deployment that [screens outgoing mail](mail-delivery.md) for sensitive content
reads the message with the footer in it, because the footer leaves with it. So a screen that refuses phone numbers
refuses every message whose footer carries one, and the remedy is the footer or the screen, not the message.

## Images

**An image is uploaded with the footer and travels inside every message.** The markup refers to it as `cid:` and the
name it was uploaded under, and each message carries the image as an inline part beside the markup, in the
`multipart/related` the markup is the root of, under a content identifier minted for that message. Nothing is fetched
from anywhere when the message is read, which is what keeps a footer from becoming a tracking pixel. A reference to a
remote image, a `data:` image, or any scheme but `cid:` is refused when the footer is saved, saying the image has to be
uploaded.

**A footer carries at most five images, each a PNG, a JPEG, or a GIF of at most 256 KiB, in markup of at most 16384
characters.** Every image goes out on every message, so the bound is about everybody's mail rather than one upload.
An image's type is held to its octets: a file whose first octets are not the signature of the type it states is
refused, so nothing but an image goes out under an image's name.

## What a client is served

A client reads, per account, the footer a message sent now would carry, so it can show the person what their message
will end with. It arrives as the same closed document tree a message body is served as, with its images inlined the way
a message's own inline images are, says whether it is the person's own or the organization's, and is absent for an
account that sends without one. The person's own footer is read and written separately, as the markup and images they
would edit.

## Personal data

A footer usually names its writer and carries their phone number, so it is personal data about them. It is never
logged, the footer as written is stored only beside the assignment or the organization it belongs to, and it is deleted
with either. Every message it went out on carries a copy, as any sent message carries what its author wrote: the
outbox record and the filed sent copy keep it under the retention that mail has, not the footer's. No export of a
person's data exists in this release for it to be added to.
