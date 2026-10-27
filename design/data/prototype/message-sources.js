// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// What *Show source* draws: every message's raw source is composed from its thread (`sourceOf` in the component), and
// this holds the parts of it no thread carries — the relay it came through, its signature, and the five messages
// whose source does not arrive whole. Every host, address and digest here is made up; the addresses are in the
// documentation ranges.
MailFathomDesign.data.messageSources = {
  // The relay every message is drawn as having come through, and the deployment's own receiving host.
  relay: { host: "mx.nordwind.example", via: "192.0.2.41", id: "4Xq7Lm2Kd9zT" },

  // The two signature values a DKIM header carries, as base64 that verifies nothing.
  bodyHash: "q3Zt0b7Lr5mJcXn2ZQ1vH8Kp4sWdY6uGeFaRiNo9TlE=",
  signature: [
    "Gx2fV7nqLbP3sR0uKc8YtW1mHdZ6oJeA9iQ4lNrT5vXyBk2gMpSaFwD7hCzUjE0q",
    "Rb4tYn8LmKs1PvQx6WcZ3oHdJ9eAfGi2uNlT0rV5yXk7BgMpSaFwD3hCzUjE1qLs",
    "Pn6vQx0WcZ5oHdJ8eAfGi4uNlT2rV7yXk9BgMpSa==",
  ],

  // The one message whose source the deployment could not serve: its local copy is missing, so the surface draws
  // the error with a way to try again, and the second try arrives.
  failsFor: ["devops1"],

  // The one whose source is larger than the client is sent whole: the first part stands with a line saying where it
  // stops and how large the whole is.
  cutFor: { marketing1: "8.4 MB" },

  // The one on an account whose deployment screens what leaves it: a raw source cannot be screened the way a read is,
  // so it is not served at all, and the surface says so without a way to try again.
  screenedFor: ["bank1"],

  // The one asked for while the machine is offline: nothing goes out, and the surface says the source is read once the
  // network comes back, with no way to try again before then.
  offlineFor: ["travel1"],

  // The one whose source is asked for and never answered, so the skeleton stands on it for as long as the surface is
  // open.
  waitsFor: ["finanse"],
};
