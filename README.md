# dvbapiNET

Plugin .NET pour **DVBViewer** et **MDAPI** qui décrypte les flux DVB via un serveur **Oscam** (protocole dvbapi).

## Fonctionnalités

### Core
- Décryptage DVB en temps réel via Oscam (TCP dvbapi)
- Compatible **DVBViewer** (plugin natif) et **MDAPI** (ProgDVB, etc.)
- Reconnexion automatique avec backoff exponentiel
- Heartbeat 30s pour détection de coupure
- Queue de commandes pendantes pendant déconnexion

### v2.0 — nouveautés
- **Menu intégré** `Plugins → dvbapiNet` dans DVBViewer (clic = configuration)
- **Dialog Windows à onglets** : Configuration / Statut & Actions / Debug / À propos
- **Interface web** intégrée sur port 8080 (TcpListener, pas d'URL ACL)
  - Page status auto-refresh
  - Stats décryptage (CW total/even/odd, ECM total, latence)
  - Historique des 100 dernières ECMs (CAID, PID, latence, reader, protocole)
  - Graphique sparkline latence ECM sur 60 min
  - Endpoints JSON
- **Auto-discovery** des serveurs Oscam sur le subnet local
- **Failover multi-serveurs** Oscam (liste prioritaire, bascule auto)
- **Tray icon Windows** avec menu rapide et notifications natives
  - Vert = chaîne tunée, orange = connecté en attente, rouge = déconnecté
  - Toasts sur événements : Oscam down/up, ECM timeout > 15s
- **Auth basique HTTP** optionnelle sur l'interface web
- **Webhooks sortants** (POST JSON) sur événements
- **Diagnostic 1-clic** : génère un ZIP (log + config masqué + snapshot)
- **Auto-updater** GitHub : check des releases, notification au démarrage

## Build

Prérequis :
- Visual Studio 2022 (ou MSBuild Tools 17+)
- .NET Framework 4.8.1 SDK
- Plateforme cible : **x86** (DVBViewer 32-bit)

```powershell
MSBuild dvbapi.net.sln /p:Configuration=Release /p:Platform=x86 /t:dvbapiNet
```

La DLL est générée dans `dvbapiNet\bin\x86\Release\dvbapiNet.dll`.

## Installation

1. Fermer DVBViewer.
2. Copier `dvbapiNet.dll` dans `C:\Program Files (x86)\DVBViewer\Plugins\`.
3. Démarrer DVBViewer.
4. Menu `Plugins → dvbapiNet` → configurer le serveur Oscam.

## Configuration

Fichier `%ProgramData%\dvbapiNET\dvbapiNET.ini` :

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

## Interface web

Naviguer sur `http://127.0.0.1:8080/` — auto-refresh 5s.

API JSON :
- `GET /api/status` — connexion, chaîne tunée, SID, PID
- `GET /api/decrypt/stats` — compteurs CW, ECM, latence
- `GET /api/ecm/recent` — 100 dernières ECMs
- `GET /api/ecm/latency-history` — buckets latence par minute (60 min)
- `GET /api/discovery/scan` — scan subnet local pour Oscam (~5s)
- `GET /api/reconnect` — force une reconnexion
- `GET /api/decrypt/reset` — reset compteurs
- `GET /api/config` — config courante (sans password)
- `GET /api/log/tail?n=200` — N dernières lignes du log

## License

GPL-3.0 — voir `LICENSE`.

Basé sur le travail original de **t5b6_de** et la communauté.
