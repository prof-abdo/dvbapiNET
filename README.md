# dvbapiNET

[![Build](https://github.com/prof-abdo/dvbapiNET/actions/workflows/build.yml/badge.svg)](https://github.com/prof-abdo/dvbapiNET/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/prof-abdo/dvbapiNET)](https://github.com/prof-abdo/dvbapiNET/releases)
[![License](https://img.shields.io/badge/License-GPL%203.0-blue.svg)](LICENSE)

.NET plugin for **DVBViewer** and **MDAPI** that decrypts DVB streams via an **Oscam** server (dvbapi protocol).

## Features

### Core

* Real-time DVB decryption via Oscam (TCP dvbapi)
* Compatible with **DVBViewer** (native plugin) and **MDAPI** (ProgDVB, etc.)
* Automatic reconnection with exponential backoff (1s → 32s)
* 30s heartbeat for connection-loss detection
* Pending command queue replayed after reconnect

### v2.0 — UI & monitoring foundation

* **Integrated menu** `Plugins → dvbapiNet` in DVBViewer
* **Tabbed Windows dialog**: Configuration / Status & Actions / Debug / About
* **Embedded web interface** on port 8080 (TcpListener, no URL ACL required)
  * Auto-refreshing status page
  * Decryption stats (total/even/odd CWs, total ECMs, latency)
  * History of the last 100 ECMs (CAID, PID, latency, reader, protocol)
  * JSON endpoints
* **Auto-discovery** of Oscam servers on the local subnet
* **Multi-server failover** for Oscam (priority list, auto-switch)
* **Windows tray icon** with quick menu + native toast notifications
  * Green = channel tuned · Orange = connected & idle · Red = disconnected
  * Events: Oscam down/up, ECM timeout > 15s
* Optional **HTTP Basic Auth** on the web interface
* **Outgoing webhooks** (POST JSON) on events
* **1-click diagnostics**: generates a ZIP archive (logs + masked config + snapshot)
* **GitHub auto-updater**: checks for releases, notifies on startup

### v2.1 — Polish & ops

* **Dark mode** for the configuration dialog
* **CSV export** of ECM history (button in Debug tab + `/api/ecm/export.csv` endpoint)
* **Automatic log rotation** at 5 MB (keeps 3 rotated files)
* **GitHub Actions CI**: builds the DLL on every push, creates a release automatically on tag push
* Build status badge

### v2.2 — Performance & telemetry

* **CW cache (opt-in)** for fast zapping — recent control words per SID are seeded into newly created descramblers, so returning to a channel watched in the last 12 seconds skips the ECM round-trip. Toggle via the *Advanced* group in the dialog or `[cache] cw=1` in the INI.
* **Channel watch-time heatmap** — top 10 most-watched services (`/api/heatmap/channels`)
* **Per-CAID ECM counters** (`/api/heatmap/caid`)
* CW cache stats exposed in `/api/decrypt/stats` (`hits`, `misses`, `stores`, `size`)

### v2.3 — Integration & quality

* **MQTT publisher** — minimal hand-rolled MQTT 3.1.1 client (no NuGet dependency). Publishes plugin state every 10 seconds on configurable topics with Last-Will-Testament for availability.
* **Home Assistant auto-discovery** — auto-creates 6 entities (connectivity, channel tuned, service ID, ECM latency, CW total, ECM total) under a single `dvbapiNET` device on first MQTT connection.
* **xUnit test project** (`dvbapiNet.Tests/`) — 13 tests covering `ReconnectionStrategy` and `CwCache` (run with `dotnet test`).

## Build

Prerequisites:

* Visual Studio 2022 (or MSBuild Tools 17+)
* .NET Framework 4.8.1 SDK
* Target platform: **x86** (DVBViewer is 32-bit)

```powershell
MSBuild dvbapi.net.sln /p:Configuration=Release /p:Platform=x86 /t:dvbapiNet
```

The DLL is generated in `dvbapiNet\bin\x86\Release\dvbapiNet.dll`.

## Installation

The same `dvbapiNet.dll` serves both host applications. Pick the section that matches your
viewer — only the plugin folder differs, the configuration is shared.

### Runtime dependency: FFDecsa.dll

The plugin calls into a native helper for DVB descrambling:

```
FFDecsa.dll   (32-bit, x86)
```

It must sit **next to `dvbapiNet.dll`** in the plugin folder. It is not bundled with the
release — obtain it from the upstream FFDecsa project and match the architecture (x86).

When you need it, and when you don't:

| Situation | FFDecsa.dll required? |
|---|---|
| DVBViewer, DVB-CSA mode (the normal case) | **yes** |
| DVBViewer, iCAM/VideoGuard 64-bit CWs (alt CSA) | **yes** |
| DVBViewer, DES or AES-128 mode | no — those are pure C# |
| ProgDVB / MDAPI | no — the host descrambles, the plugin only forwards control words |

**If it is missing, decryption fails silently.** The plugin logs an exception, leaves its
algorithm unset, and every subsequent descramble call quietly does nothing — the picture
stays scrambled with no error shown to the user. If a DVBViewer channel will not decrypt
even though ECMs are arriving, check this first. See *Verifying an install* below.

### DVBViewer

1. Close DVBViewer.
2. Copy `dvbapiNet.dll` to `C:\Program Files (x86)\DVBViewer\Plugins\`.
3. Start DVBViewer.
4. Open `Plugins → dvbapiNet` and configure the Oscam server.

### ProgDVB / MDAPI

ProgDVB loads plugins through the **MDAPI** interface, which the same DLL also implements.
There is no separate build and no extra dependency.

1. Close ProgDVB.
2. Copy `dvbapiNet.dll` into your host's MDAPI plugin folder — normally a `Plugins`
   subfolder next to the `ProgDVB.exe` / `MDAPI.exe` executable
   (e.g. `C:\Program Files\ProgDVB\Plugins\` or `C:\Program Files\MDAPI\Plugins\`).
   Use the folder your host actually scans; ProgDVB installations differ.
3. Start ProgDVB. The plugin registers itself and adds a **`dvbapiNET`** entry to the
   host's plugin menu.
4. Configure the Oscam server, then tune an encrypted channel. Look for repeated
   `MDAPI SetDcw` lines in the log — each one is a control word handed to the host.

> **Known issue — the ProgDVB menu entry does not open the dialog.** The menu item is
> created by `SetMenuHandle`, but the thread that listens for the click is only started
> from DVBViewer's `SetAppHandle` export, which MDAPI hosts never call. The click is
> therefore a silent no-op. Until this is fixed, open the configuration dialog from the
> **system tray icon** instead (enable it with `[ui] tray=1`, the default), or edit
> `%ProgramData%\dvbapiNET\dvbapiNET.ini` by hand.

Requirements and behaviour:

* The host process must be named `ProgDVB` or `MDAPI`. The plugin inspects the process name
  at startup to decide packet handling; any other name logs
  `Unknown host application, using 184 byte mode` and will not decrypt.
* Under MDAPI the plugin does **not** descramble the stream itself. It requests the PIDs it
  needs, forwards filtered sections to Oscam, and passes the returned control words back to
  the host via the MDAPI `DvbSetDescr` command — the host does the descrambling.
* 188-byte TS packets are always assumed on ProgDVB. On `MDAPI` they require version
  **0.9.0.1615** or newer; older builds fall back to 184-byte packets and no keep-alive.
* Up to **64 PID filters** are managed per host process.
* If the plugin log reports that a DVBAPI client was already running, that is normal when
  more than one host (or a second viewer instance) is active — only the first one opens the
  connection to Oscam and the others act as demux instances over a named pipe.

## Configuration

File: `%ProgramData%\dvbapiNET\dvbapiNET.ini`

This path is **shared by both hosts** and is created on first run. It is *not* read from
next to the DLL, so a single config applies no matter which viewer you start.

```ini
[dvbapi]
server=127.0.0.1
port=633
# Optional comma-separated failover servers (host:port pairs)
servers=192.168.1.10:633,192.168.1.11:633
offset=0

[log]
# Bitmask, not a level: 1=Error 2=Warning 4=Info 8=EcmInfo 16=DvbApi 32=PluginEvent 1024=CW
# Combine with "+". 0 = no logging at all. 31 is a good setting for troubleshooting.
debug=0
pretty=1

[debug]
streamdump=0

[web]
port=8080
# Empty user/password disables HTTP Basic Auth
user=
password=

[webhook]
# Comma-separated outgoing webhook URLs (POST JSON on events)
url=https://your-endpoint.example/hook

[cache]
# 1 = enable CW cache for fast zapping (experimental but safe — cached CWs are overwritten by fresh Oscam CWs)
cw=0

[ui]
tray=1
dark=0

[update]
check=1
owner=YOUR_GITHUB_USER
repo=dvbapiNET

[mqtt]
enabled=0
host=127.0.0.1
port=1883
user=
password=
topic=dvbapinet
ha_discovery=1
```

## Web Interface

Open <http://127.0.0.1:8080/> — auto-refreshes every 5 s.

### JSON API endpoints

| Endpoint | Description |
|---|---|
| `GET /api/status` | Connection state, tuned channel, SID, PID |
| `GET /api/decrypt/stats` | CW + ECM counters, latency, CW cache stats |
| `GET /api/ecm/recent` | Last 100 ECMs |
| `GET /api/ecm/latency-history` | Minute-by-minute latency buckets (60 min) |
| `GET /api/ecm/export.csv` | Full ECM history as CSV |
| `GET /api/heatmap/channels` | Top 10 watched channels by time |
| `GET /api/heatmap/caid` | ECM count per CAID |
| `GET /api/discovery/scan` | Local subnet scan for Oscam (~5 s) |
| `GET /api/config` | Current configuration (password redacted) |
| `GET /api/log/tail?n=200` | Last N log lines |
| `GET /api/reconnect` | Force a reconnection |
| `GET /api/decrypt/reset` | Reset counters |

## Logs & Troubleshooting

The plugin keeps its own log, separate from the host application's:

```
%ProgramData%\dvbapiNET\dvbapiNET.log
```

It rotates automatically at 5 MB and keeps 3 rotated files. The *Debug* tab of the
configuration dialog and `GET /api/log/tail?n=200` both read this same file.

> **Nothing is logged by default.** `[log] debug` defaults to `0`, and every log call is
> filtered against it. Set it before following the checklist below.

`debug` is a **bitmask**, not a severity level:

| Value | Enables |
|---|---|
| `1` | `Error` |
| `2` | `Warning` |
| `4` | `Info` |
| `8` | `EcmInfo` (per-ECM blocks) |
| `16` | `DvbApi` (connect/handshake tracing) |
| `32` | `DvbViewerPluginEvent` |
| `128`–`512` | Intercom socket traffic |
| `1024` | `ControlWord` |
| `32768` | adds hex dumps |

Combine with `+`. `debug=31` (= 1+2+4+8+16) is the useful setting for verifying an install;
`debug=1` is enough to just catch failures.

### Verifying an install

With `debug=31`, restart the host and check in order:

1. `Connecting to OScam dvbapi server...` then `Connected` — the TCP link to the dvbapi
   port is up.
2. `Server: <name>, protocol: <n>` — Oscam accepted the client handshake.
3. Tune an encrypted channel and look for the `ECM INFO` block (`Service ID`, `Caid`,
   `Reader`, `Time: <n>ms`). A plausible `Time` means Oscam is genuinely decrypting.
4. Confirm control words arrive. DVBViewer descrambles in-plugin; under MDAPI you should
   instead see repeated `MDAPI SetDcw` lines as each CW is handed to the host.

If step 1 never appears, check `server`/`port` in the INI and that Oscam's dvbapi listener
is enabled. If steps 1-3 work but the picture stays scrambled:

1. **Check for `FFDecsa.dll` next to `dvbapiNet.dll`** — a missing or wrong-architecture copy
   is the most common cause. With `debug=1` it shows up as a load failure in the
   `descrambler` section.
2. On MDAPI, confirm the host version is 0.9.0.1615+; older builds cannot apply the
   control words the plugin hands them.

## Testing

```powershell
cd dvbapiNet.Tests
dotnet test -p:Platform=x86
```

Requires .NET SDK (any recent version) — tests reference the pre-built DLL from `dvbapiNet\bin\x86\Release\`.

## Roadmap

### v2.4 (planned)
* Multi-language UI (FR / EN / DE) via centralized `Message` enum
* More tests covering `DecryptionMonitor` and `OscamDiscovery`
* Polish / bug fixes based on community feedback

## License

GPL-3.0 — see `LICENSE`.

Based on the original work by **t5b6_de** and the community.
