// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// What sort of mail each message is: a model's reading of it, never a change to it. `names` is what a category draws,
// the seven defaults first and then this mailbox's own, in the order they are drawn; a default whose name is null is
// one that draws no label. `messages` is keyed by thread, one entry per message, newest first and the user's own included: a message holds
// exactly one default and any of the mailbox's own. A thread or an entry missing here is a message nothing has
// categorized yet, or one the model gave no answer about, and both draw nothing.
MailFathomDesign.data.categories = {
  names: {
    correspondence: null,
    subscriptions: "Subscriptions",
    finance: "Finance",
    shopping: "Shopping",
    travel: "Travel",
    notifications: "Notifications",
    other: null,
    contracts: "Contract renewals, addenda and indexation clauses for the master agreements",
    legal: "Legal",
    budget: "Budget 2027",
    suppliers: "Suppliers",
  },
  messages: {
    contoso: [["correspondence", "contracts", "legal", "budget"], ["correspondence", "contracts"], null, ["finance", "contracts"]],
    finanse: [["finance"]],
    piotr: [["correspondence"]],
    jacek: [["correspondence", "legal"]],
    anna2: [["correspondence", "contracts"]],
    hr1: [["other"]],
    north1: [["correspondence", "budget"]],
    bank1: [["finance"]],
    fabrikam1: [["shopping", "suppliers"], null, ["correspondence", "suppliers"]],
    travel1: [["travel"]],
    marketing1: [["subscriptions"]],
    devops1: [["notifications"]],
    handover1: [["correspondence", "suppliers"]],
  },
};
