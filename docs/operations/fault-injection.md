# Injecting network faults

<!-- describes: deploy/compose/compose.faults.yaml, scripts/inject-fault.sh -->

`deploy/compose/compose.faults.yaml` puts one [Toxiproxy](https://github.com/Shopify/toxiproxy) instance on three
connections of a [Compose deployment](deployment-compose.md), and `scripts/inject-fault.sh` breaks one of them on
purpose and mends it again: the IMAP server taken away, a mailbox that stops answering, a connection cut halfway
through a transfer, a client that can no longer reach the service. It exists so the behaviour of a running deployment
and of every client head during an outage is watched against the real service and a real mail server, rather than
against the fake deployment the client's browser suite answers from.

**It is a testing tool.** Nothing of it exists in a deployment that does not name the overlay: it adds no production
code, no configuration key, and no endpoint, and the service never knows the proxy is there. Run it against a test
mailbox rather than one somebody depends on. A connection cut in the middle of a move or a flag change leaves the
mailbox in whatever state the server reached, and a provider that sees the same account reconnect over and over may
throttle or lock it.

## Starting it

The overlay is applied on top of `compose.yaml`, from `deploy/compose/`, and needs the two mail server names the
account under test connects to:

```bash
MAILFATHOM_FAULTS_IMAP_HOST=imap.example.com \
MAILFATHOM_FAULTS_SMTP_HOST=smtp.example.com \
docker compose -f compose.yaml -f compose.faults.yaml up -d
```

Leaving either name out fails at once with the reason, and it does for every command that names the overlay — `ps`,
`logs`, and `down` read the file as `up` does — so export both names, or put them in `.env`, for as long as the
overlay is in use. Naming files with `-f` stops Compose from reading `compose.override.yaml` on its own, so where
[the quick start](deployment-compose.md#trying-it-first-with-one-command) wrote one, name it as well, before the
overlay. To go back to an ordinary deployment, bring the stack down with the same files and the same two names, then
start it without the overlay.

| Variable | Default | What it says |
|---|---|---|
| `MAILFATHOM_FAULTS_IMAP_HOST` | required | The IMAP server name, exactly as the account names it |
| `MAILFATHOM_FAULTS_IMAP_PORT` | `993` | The port the account connects to it on |
| `MAILFATHOM_FAULTS_SMTP_HOST` | required | The SMTP server name, exactly as the account names it |
| `MAILFATHOM_FAULTS_SMTP_PORT` | `587` | The port the account sends on |
| `MAILFATHOM_FAULTS_CONTROL_PORT` | `8474` | The loopback port the proxy's control API is published on, which `scripts/inject-fault.sh` reads too |
| `MAILFATHOM_FAULTS_SUBNET` | `172.30.199.0/24` | The subnet of the overlay's own network |
| `MAILFATHOM_FAULTS_PROXY_ADDRESS` | `172.30.199.10` | The proxy's address on it, which has to lie inside the subnet |
| `MAILFATHOM_TOXIPROXY_IMAGE` | the pinned image | The proxy's image |

The overlay withdraws the HTTP port `compose.yaml` publishes with Compose's `!override` merge tag and hands the proxy
its definition as an inline `configs` entry, so it needs a Compose that reads both.

## What it changes

**The mail connections.** Inside the `mailfathom` container alone, the two server names resolve to the proxy, which
forwards to the real servers under the same names. The TLS session is still the one MailFathom opens with the real
server, and the certificate it validates is the real server's, so a mail account is configured exactly as it is
without the overlay. An account that names its server differently — another name, an address — bypasses the proxy.
The names are written into the container's `/etc/hosts`, which the C library reads before DNS even in the chiseled
image, and they point at an address fixed on a network of the overlay's own, because an address can be fixed only on a
network whose subnet is stated. Change `MAILFATHOM_FAULTS_SUBNET` and `MAILFATHOM_FAULTS_PROXY_ADDRESS` together when
that subnet is already taken on the host.

**The client's connection.** The HTTP port is published by the proxy instead of by the `mailfathom` container, on the
same address and port `MAILFATHOM_HTTP_BIND` and `MAILFATHOM_HTTP_PORT` name, so a client reaches the deployment only
through it. In this deployment that port also serves the MCP endpoint, so a fault on it reaches an agent's connection
as well. The administrative and health ports stay published directly, so `mfctl` and the probes keep answering while
a fault is active.

**The proxy itself.** It runs as `nobody` with every capability dropped, and opens the mail ports below 1024 because the
overlay lowers the unprivileged port floor of its own network namespace, which reaches nothing outside the container.
It is not read-only like the services of `compose.yaml`, because Compose refuses to copy the proxies' definition into a
read-only container. Its control API answers anybody who reaches it, with no credential, so it is published on
`127.0.0.1` whatever the other ports are bound to.

## Breaking a connection

```bash
scripts/inject-fault.sh list
scripts/inject-fault.sh start imap-down --for 5m
scripts/inject-fault.sh stop
```

The script needs `curl` and `jq` on the path. `start` holds the fault for the duration — seconds, or a number followed
by `s`, `m`, or `h`, and `60s` when none is given — then removes it. Interrupting the script removes it too, and so
does closing the terminal it runs in, so a fault does not outlive the run that started it unless that run is killed
outright; `stop <preset>` removes one preset's fault, and `stop` alone every fault the proxy
holds. A preset already active is refused rather than started twice, and a refusal from the proxy is reported with
the proxy's own reason. `list` names the presets and what is active now.

| Preset | Connection | What happens |
|---|---|---|
| `imap-down` | IMAP | A new connection is refused and every open one is closed |
| `imap-stalled` | IMAP | The server stops answering; the connection stays open and nothing arrives on it |
| `imap-slow` | IMAP | Every answer from the server arrives two to four seconds late |
| `imap-cut-midway` | IMAP | A connection is closed once the server has sent 64 KiB on it, which a login survives and a large fetch does not |
| `smtp-down` | SMTP | A new connection is refused and every open one is closed |
| `server-down` | Client | The service cannot be reached, and every open connection to it is closed |
| `server-slow` | Client | Every answer from the service reaches the client two to four seconds late |

## What it cannot inject

The proxy sees TCP connections and the bytes on them, and none of what those bytes mean, because the mail
connections are encrypted end to end. So it cannot:

- refuse a mailbox credential, or answer a command with an error of the server's own;
- answer a message with an SMTP reply code;
- answer chosen client routes with an HTTP status, or slow one route and not another;
- lose the answer to a write while the write itself is applied — cutting a connection loses both or neither, and
  which one is not chosen;
- break one folder, one command, or one connection while others on the same proxy pass.

Those need injection inside the service, and nothing here provides it. The overlay also covers one IMAP host and one
SMTP host, and neither an OAuth provider nor an AI provider; and it exists for Compose alone, with nothing equivalent in
the Helm chart or the Quadlet units.

## Related

- [Docker Compose](deployment-compose.md) — the deployment the overlay is applied to
- [The end-to-end client run](end-to-end-client-run.md) — the pipeline that drives the real client against a throwaway
  deployment
