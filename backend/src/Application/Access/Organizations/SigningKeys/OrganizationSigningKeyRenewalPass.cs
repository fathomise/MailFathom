// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Access.Organizations.SigningKeys;

/// <summary>What one pass over the organizations' signing keys did.</summary>
/// <param name="Renewed">How many organizations got a fresh active key.</param>
/// <param name="Unminted">How many were due one and got none, because the deployment configures no key ring.</param>
public sealed record OrganizationSigningKeyRenewalPass(int Renewed, int Unminted);
