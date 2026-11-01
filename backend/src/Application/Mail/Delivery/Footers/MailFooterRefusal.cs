// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Delivery.Footers;

/// <summary>Why a footer or a template was not saved.</summary>
/// <param name="Reason">What was wrong with it.</param>
/// <param name="Subject">The image or variable name the reason is about, or <see langword="null" /> where it is about neither.</param>
/// <remarks>The subject is a name the writer chose, never a reference the markup carried, so a refusal never repeats a remote address back.</remarks>
public sealed record MailFooterRefusal(MailFooterRefusalReason Reason, string? Subject = null);
