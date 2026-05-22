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

1. Close DVBViewer.
2. Copy `dvbapiNet.dll` to `C:\Program Files (x86)\DVBViewer\Plugins\`.
3. Start DVBViewer.
4. Open `Plugins → dvbapiNet` and configure the Oscam server.

## Configuration

File: `%ProgramData%\dvbapiNET\dvbapiNET.ini`

```ini
[dvbapi]
server=127.0.0.1
port=633
# Optional comma-separated failover servers (host:port pairs)
servers=192.168.1.10:633,192.168.1.11:633
offset=0

[log]
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

## Roadmap

### v2.3 (planned)
* xUnit test project for non-UI code (CwCache, ReconnectionStrategy, DecryptionMonitor)
* MQTT publisher with Home Assistant auto-discovery
* Multi-language UI (FR / EN / DE)

## License

GPL-3.0 — see `LICENSE`.

Based on the original work by **t5b6_de** and the community.
