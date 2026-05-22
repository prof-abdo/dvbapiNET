# dvbapiNET

[![Build](https://github.com/prof-abdo/dvbapiNET/actions/workflows/build.yml/badge.svg)](https://github.com/prof-abdo/dvbapiNET/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/prof-abdo/dvbapiNET)](https://github.com/prof-abdo/dvbapiNET/releases)
[![License](https://img.shields.io/badge/License-GPL%203.0-blue.svg)](LICENSE)

.NET plugin for **DVBViewer** and **MDAPI** that decrypts DVB streams via an **Oscam** server (dvbapi protocol).

## Features

### Core

* Real-time DVB decryption via Oscam (TCP dvbapi)
* Compatible with **DVBViewer** (native plugin) and **MDAPI** (ProgDVB, etc.)
* Automatic reconnection with exponential backoff
* 30s heartbeat for connection loss detection
* Pending command queue during disconnection

### v2.0 — New Features

* **Integrated menu** `Plugins → dvbapiNet` in DVBViewer (click = configuration)
* **Tabbed Windows Dialogs**: Configuration / Status & Actions / Debug / About
* **Integrated web interface** on port 8080 (TcpListener, no URL ACL required)
* Auto-refreshing status page
* Decryption stats (total/even/odd CWs, total ECMs, latency)
* History of the last 100 ECMs (CAID, PID, latency, reader, protocol)
* 60-min ECM latency sparkline chart
* JSON endpoints


* **Auto-discovery** of Oscam servers on the local subnet
* **Multi-server failover** for Oscam (priority list, auto-switch)
* **Windows tray icon** with quick menu and native notifications
* Green = channel tuned, orange = connected & waiting, red = disconnected
* Toast notifications on events: Oscam down/up, ECM timeout > 15s


* Optional **HTTP Basic Auth** on the web interface
* **Outgoing webhooks** (POST JSON) on events
* **1-click diagnostics**: generates a ZIP archive (logs + masked config + snapshot)
* **GitHub auto-updater**: checks for releases, notifies on startup

## Build

Prerequisites:

* Visual Studio 2022 (or MSBuild Tools 17+)
* .NET Framework 4.8.1 SDK
* Target platform: **x86** (DVBViewer 32-bit)

```powershell
MSBuild dvbapi.net.sln /p:Configuration=Release /p:Platform=x86 /t:dvbapiNet

```

The DLL is generated in `dvbapiNet\bin\x86\Release\dvbapiNet.dll`.

## Installation

1. Close DVBViewer.
2. Copy `dvbapiNet.dll` to `C:\Program Files (x86)\DVBViewer\Plugins\`.
3. Start DVBViewer.
4. Go to the `Plugins → dvbapiNet` menu → configure the Oscam server.

## Configuration

File: `%ProgramData%\dvbapiNET\dvbapiNET.ini`

```ini
[dvbapi]
server=127.0.0.1
port=633
servers=192.168.1.10:633,192.168.1.11:633

[log]
debug=0
pretty=1

[web]
port=8080
user=
password=

[webhook]
url=https://your-endpoint.example/hook

[ui]
tray=1

[update]
check=1
owner=YOUR_GITHUB_USER
repo=dvbapiNET

```

## Web Interface

Navigate to `[http://127.0.0.1:8080/](http://127.0.0.1:8080/)` — auto-refreshes every 5s.

JSON API endpoints:

* `GET /api/status` — connection, tuned channel, SID, PID
* `GET /api/decrypt/stats` — CW, ECM counters, latency
* `GET /api/ecm/recent` — last 100 ECMs
* `GET /api/ecm/latency-history` — minute-by-minute latency buckets (60 min)
* `GET /api/discovery/scan` — local subnet scan for Oscam (~5s)
* `GET /api/reconnect` — forces a reconnection
* `GET /api/decrypt/reset` — resets counters
* `GET /api/config` — current configuration (excluding password)
* `GET /api/log/tail?n=200` — last N log lines

## License

GPL-3.0 — see `LICENSE`.

Based on the original work by **t5b6_de** and the community.
