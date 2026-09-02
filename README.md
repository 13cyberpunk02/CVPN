# CVPN

**English** · [Русский](README.ru.md)

[![Release](https://img.shields.io/github/v/release/13CyberPunk02/CVPN?label=release)](https://github.com/13CyberPunk02/CVPN/releases)
[![Downloads](https://img.shields.io/github/downloads/13CyberPunk02/CVPN/total)](https://github.com/13CyberPunk02/CVPN/releases)
[![Tests](https://github.com/13CyberPunk02/CVPN/actions/workflows/tests.yml/badge.svg)](https://github.com/13CyberPunk02/CVPN/actions/workflows/tests.yml)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

A desktop sing-box client for Windows. It builds `config.json` for you, runs the
core, and shows you what is happening to your traffic.

Supports **VLESS (Reality and WebSocket)**, **AnyTLS** and **NaiveProxy**, with
routing rules that decide which sites go through the proxy, which go direct,
and which get blocked.

> Not affiliated with the sing-box team. This is an independent wrapper around
> the official core.

---

## Contents

- [Features](#features)
- [Installation](#installation)
- [Quick start](#quick-start)
- [Profiles](#profiles)
- [Interception modes](#interception-modes)
- [Administrator rights](#administrator-rights)
- [Updates](#updates)
- [Kill switch](#kill-switch)
- [Autostart](#autostart)
- [Routing](#routing)
- [Connections](#connections)
- [File locations](#file-locations)
- [What goes into config.json](#what-goes-into-configjson)
- [Troubleshooting](#troubleshooting)
- [Building from source](#building-from-source)
- [Tests](#tests)
- [Building a release](#building-a-release)
- [Architecture](#architecture)
- [Roadmap](#roadmap)
- [Licences](#licences)

---

## Features

- VLESS+Reality, VLESS+WebSocket, AnyTLS and NaiveProxy
- Import from a share link, from an existing `config.json`, or from a subscription
- Export a profile as a link and a QR code
- Routing rules: geosite, geoip, domain, suffix, keyword, process name
- Custom `.srs` rule sets, remote or local
- Named rule sets you can switch between
- Server switching without restarting the core, plus automatic fastest-server selection
- Two interception modes: TUN and the Windows system proxy
- A Windows service so TUN does not ask for elevation on every launch
- Live traffic counters and latency measurement across all servers
- A list of live connections showing domain, outbound and matched rule
- Tray icon, autostart, real-time core output
- File logs kept for 7 days, crashes recorded with stack traces
- Kill switch: traffic outside the tunnel is blocked by the firewall
- Credentials encrypted with your Windows account key

---

## Installation

Grab a build from the [releases page](https://github.com/13CyberPunk02/CVPN/releases):

| File | What it is |
|---|---|
| `CVPN-x.y.z-setup.exe` | installer: Program Files, service, shortcuts |
| `CVPN-x.y.z-portable.zip` | portable build, just unpack it |

Both bundle `sing-box`. The installer is about 50 MB and downloads the .NET
runtime if your system does not have it (~60 MB, once per machine). The
portable build is self-contained: nothing to download, but it weighs ~200 MB.

> Windows SmartScreen will warn about an unknown publisher - the installer is
> not code-signed. That is expected for a project without a paid certificate:
> **More info** → **Run anyway**.

The portable build cannot install the service, so TUN will request elevation
every time. If you want elevation-free startup, use the installer.

### Requirements

| | |
|---|---|
| OS | Windows 10 1809 or newer / Windows 11 |
| Core | sing-box **1.12 or newer** (bundled in releases) |

The core version matters. sing-box 1.12 removed `geosite` and `geoip` from
routing rules in favour of rule sets, and replaced the `block` and `dns`
outbounds with rule actions. CVPN generates the new format, which will not
start on 1.11.

---

## Quick start

1. **Profiles** → paste a link and press **Parse link**, or **Import from file**
   if you already have a `config.json`.
2. Press **Select** on the server you want - it gets an accent-coloured border.
3. **Connection** → press the dial in the middle.

The profile name, country flag, protocol and latency are shown inside the dial.
The ring shows state: grey means disconnected, indigo with a running segment
means connecting, jade means connected, red means an error.

---

## Profiles

### Import from a link

```
vless://uuid@host:443?security=reality&pbk=<public_key>&sid=<short_id>&sni=www.google.com&flow=xtls-rprx-vision#Name
vless://uuid@host:443?type=ws&security=tls&path=/ws&host=example.com#Name
anytls://password@host:8443?sni=example.com#Name
naive+https://user:password@host:443#Name
```

### Import from JSON

**Import from file** accepts three shapes: a full sing-box config (it pulls out
every proxy outbound at once), an array of outbound objects, and a single
object. Anything that is not a supported protocol - `direct`, `selector`,
`urltest` - is skipped silently, so you can feed it someone else's working
config as is.

### Subscription

Paste a subscription URL on the Profiles page and press **Update
subscription**. The standard format is understood: a list of links, one per
line, base64-encoded as a whole (plain text works too).

An update only replaces profiles that came from that same subscription - they
are marked in the list. Manually created ones are left alone, and the selected
server is restored by name.

### Creating one by hand

**Create manually** opens a form whose fields change with the protocol: UUID,
public key, short id and flow for Reality; UUID and path for WebSocket; a
password for AnyTLS; a login and password for NaiveProxy. Validation checks the
UUID format and the port range before saving.

### Export

**Share** on a profile card opens a window with the link and a QR code.
**Export all** produces the whole list as a single subscription string, which
CVPN itself and most other clients will accept.

### Checking servers

Latency is measured when you first open the list; **Check all** refreshes it.
The probe is a plain TCP handshake, so it works without a tunnel and covers
every server at once.

This is not the same as **Measure latency** on the connection page: that one
sends a request through the live tunnel and reports the real round trip to a
target site.

### Switching without a restart

Every server goes into the configuration at once, and a sing-box selector picks
between them. Pressing **Select** on another profile therefore switches the
tunnel through the Clash API in milliseconds - the core is not restarted and
live connections are not dropped.

**Pick the fastest server automatically** hands the choice to the urltest
mechanism: the core probes servers every three minutes and keeps the connection
on the fastest one. It only switches when the new server is at least 50 ms
faster, otherwise the tunnel would bounce between nodes with similar latency.

### Country code

The flag is detected automatically - from the profile name ("Netherlands",
"Frankfurt") or from a host like `nl-01.example.net`. If the guess fails, set
the code by hand in the profile editor.

Flags are bitmaps rather than emoji: Windows does not render regional indicator
flags, showing two letters instead.

---

## Interception modes

The active mode is always shown in the bottom left.

### TUN

Creates a virtual network interface and captures **all** system traffic,
including applications that know nothing about proxies. Requires administrator
rights - see [the next section](#administrator-rights).

### System proxy

Writes `127.0.0.1:<port>` into the Windows proxy settings. No elevation needed,
but it only affects applications that respect those settings: browsers and most
messengers.

The previous settings are restored on disconnect, on exit, and even when the
core dies unexpectedly. Local addresses bypass the proxy.

> Switching modes while connected shows a warning: the change only applies
> after reconnecting. The core reads its configuration at startup only.

---

## Administrator rights

Only an elevated process can create a TUN interface, which otherwise means a
UAC prompt on every launch. There are two ways around it, and both cost exactly
one prompt at setup time.

### Tunnel service

`CVPN.Service` runs as `LocalSystem`, is installed once, and starts with the
system. The application runs as a normal user and asks the service to bring the
tunnel up over a named pipe.

```
CVPN.exe (user)  ──[pipe cvpn-tunnel]──►  CVPN.Service (LocalSystem)
                                                 └─► sing-box + TUN
```

The installer sets the service up for you. To do it manually, use **Install
service** in settings.

### Scheduled task

Simpler: no second project involved. The task is created with the highest
privileges, and the application is then launched through it already elevated.
Settings → **Create task**.

### Which to choose

| | Service | Scheduled task |
|---|---|---|
| Separate project | required | not required |
| Elevation scope | tunnel only | the whole application |
| Tunnel after sign-out | keeps running | drops |
| Starts before user sign-in | yes | no |
| One-time UAC prompt at setup | yes | yes |

The service is the cleaner separation: the UI stays an ordinary user process
and only the tunnel runs as `SYSTEM`. The task is simpler but elevates
everything, which among other things breaks drag-and-drop from Explorer.

### Service security

The service executes a configuration as `SYSTEM`, and that configuration is
supplied by an unprivileged process. Taking it at face value is not an option:
through `cache_file.path` or `log.output` any local user could make `SYSTEM`
write a file anywhere. So `ConfigSanitizer` rewrites it before launch:

- the cache path is forced into the service directory;
- `log.output` is removed - output goes to stdout only;
- local rule sets outside `%ProgramData%\CVPN\rules` are dropped;
- the Clash API is pinned back to loopback.

The service writes the config itself, into its own directory - a path from the
client is never accepted. The protocol has no commands beyond start, stop and
status.

Pipe access is deliberately open to all local users: restricting it to
administrators would put us back to needing elevation in the client, defeating
the point. If that is unacceptable on a shared machine, narrow the rule in
`PipeServer` to a specific SID instead of `BuiltinUsersSid`.

One requirement applies to both approaches: the executable must live somewhere
an ordinary user cannot write to. Otherwise replacing the file gives code
execution with administrator rights. That is why the installer puts the
application in Program Files.

---

## Updates

The application asks GitHub whether a newer release exists and reports it on
the settings page. It downloads and installs nothing: self-updating an
application that deploys a service and edits firewall rules deserves separate
care - for now that is the installer's job.

Drafts and pre-releases are ignored. The check can be disabled in settings.

> Replace the `Repository` constant in `Services/UpdateChecker.cs` with your own
> repository before the first release.

---

## Kill switch

While the tunnel is up, all outbound traffic that bypasses it is blocked by
Windows Firewall rules. Only the core, the application itself, loopback and the
local network are allowed. The point is that a dropped connection cannot
silently fall back to a direct route - traffic simply stops.

Enabled in settings, requires administrator rights.

> **This feature can leave the machine without internet.** If the application
> terminates without removing the rules, ordinary traffic stays blocked.
>
> There are two safety nets: the fact that it was enabled is recorded in a
> marker file, and the rules are removed automatically on the next launch. If
> you need connectivity right now, use **Restore network** in settings. The
> same thing by hand, from an elevated prompt:
>
> ```
> netsh advfirewall set allprofiles firewallpolicy blockinbound,allowoutbound
> ```

---

## Tray icon

Double-click opens the window. The menu offers connect, exit, and a **Server**
submenu listing your profiles with the active one ticked. Switching goes
through the selector, so the core is not restarted.

## Autostart

The toggle in settings registers the application in the current user's `Run`
key. It launches with `--minimized`: no window, only the tray icon.

A separate setting, **Connect on startup**, brings the tunnel up with the last
selected profile. Without the service or the scheduled task, TUN will ask for
elevation at that moment.

---

## Routing

Rules are evaluated **top to bottom, and the first match wins**. What happens to
everything else is set separately at the bottom of the page.

| Type | What to enter | Example |
|---|---|---|
| `geosite` | category name | `youtube`, `twitch`, `category-ads-all` |
| `geoip` | country code | `ru`, `de`, `us` |
| `domain` | exact match | `example.com` |
| `domain_suffix` | domain and all subdomains | `openai.com` |
| `domain_keyword` | substring in the domain | `google` |
| `process_name` | executable name | `Telegram.exe` |
| `rule_set · link` | URL of a rule set | `https://.../geosite-youtube.srs` |
| `rule_set · file` | path to a file | `C:\rules\twitch.srs` |

Actions: **through proxy**, **direct**, **block**.

Order is controlled by the ▲ ▼ arrows on each rule. This is not cosmetic: the
core takes the first match, so a rule above overrides everything below it. If
`geosite:youtube` sends traffic through the proxy, a `domain_suffix:youtube.com`
direct rule placed below will never fire.

Reordering applies after reconnecting.

### Testing a domain

The field above the list answers the question that usually sends people into
the logs: which rule will match. Enter a domain or a full URL and the
application names the outcome and the matching rule, without bringing the
tunnel up.

The contents of `geosite` and `.srs` sets cannot be checked - they are binary
files the application does not have. If such a set sits above the match found,
the check says so plainly instead of guessing.

### Rule sets

Named rule sets are switched in the top right corner. Each keeps its own rules
and its own fallback action - handy for keeping, say, "everything through the
proxy" and "only what is blocked" side by side.

Switching a set requires reconnecting: routes live in the core configuration,
unlike server selection which changes on the fly.

### About .srs sets

`geosite` and `geoip` are pulled from the official SagerNet repositories
automatically. They are downloaded **through the tunnel**, because
`raw.githubusercontent.com` is often unreachable directly, and the result is
cached, so it happens once. The trade-off: the first run with a new rule needs
a working proxy, or the core will not start.

Local `.srs` files are copied into `%APPDATA%\CVPN\rules` when added, so a
profile does not break if the original file moves.

When running through the service, rule set files are handed over together with
the configuration: the service runs as `SYSTEM` and cannot see the user
directory. Only the file name is accepted from the client - the service picks
the path itself.

### Starter set

On first launch two rules are created: ads (`geosite:category-ads-all`) blocked
and Russian addresses (`geoip:ru`) direct.

---

## Connections

This page shows what is flowing right now: domain, outbound, matched rule,
process, traffic volume and connection age. The outbound badge is colour-coded -
jade for direct, indigo for proxied.

It is the fastest way to understand why a site goes somewhere unexpected: no
need to enable verbose logging and hunt for the domain among thousands of lines.

Each row has **Direct** and **Block** buttons. They add a `domain_suffix` rule
for the second-level domain - `sun9-40.vkuserphoto.ru` becomes
`vkuserphoto.ru`. The rule applies after reconnecting.

The cross closes the connection so the application reopens it under the new
rules.

---

## File locations

```
%APPDATA%\CVPN\
├── profiles.json     profiles, rule sets and settings
├── config.json       regenerated on every connect
├── cache.db          core cache: rule sets and DNS
├── rules\            copies of local .srs files
└── logs\             cvpn-YYYY-MM-DD.log, kept for 7 days
```

The full log goes to file; the screen keeps the last 500 lines. Unhandled
exceptions land there too, with the build version and a full stack trace. The
folder can be opened from the Logs page - those are the files worth attaching
to a bug report.

The service keeps its own log in `%ProgramData%\CVPN\logs\` - it starts before
user sign-in, so its output would otherwise be lost entirely.

`config.json` is **overwritten on every launch**, so editing it by hand is
pointless. Use **Open config.json** on the connection page to inspect the
result.

When running through the service, the same files live in `%ProgramData%\CVPN\`.

### Credentials

UUIDs, passwords and the subscription URL are encrypted in `profiles.json`
using DPAPI - with your Windows account key. In the file they look like this:

```json
"Password": "dpapi:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA..."
```

No master password is needed: the system provides the key. The flip side is
that the file is tied to the account and the machine. Move it to another
computer and the application will report how many values could not be
decrypted, keeping the rest of the profile - only the secrets have to be
re-entered.

Files created before encryption was added are read as is and encrypted on the
first save.

The server address, port and SNI are not treated as secrets and stay readable -
they are visible on the wire anyway.

**What this protects and what it does not.** Encryption covers cases where the
file leaves the machine: backups, profile sync, attaching `profiles.json` to an
issue. Plus reads by another account on the same computer.

It does **not** protect against malicious code running as you: it can call
decryption exactly the way the application does. That is inherent to DPAPI
without a master password.

Note also that `config.json` holds the same credentials in plain text - the
core cannot read them otherwise. Inside `%APPDATA%` only the owner can read it,
and the service directory in `%ProgramData%` is locked down from the Users
group on every start, since it is world-readable by default.

---

## What goes into config.json

A few decisions worth knowing if you compare the output with other configs.

**The server address resolves outside the tunnel.** The proxy outbound gets a
`domain_resolver` pointing at the local resolver - otherwise you get a
chicken-and-egg problem: connecting to the server requires resolving it, which
requires the tunnel. The DNS rule with `outbound: "any"` that used to do this
was deprecated in 1.12 and removed in 1.13.

**Domains from direct rules resolve locally.** Otherwise the query goes through
the tunnel and a geo-balanced site returns a node close to the exit.

Only domain conditions (`domain`, `domain_suffix`, `domain_keyword`) and local
`.srs` sets are mirrored. Not mirrored:

- `geoip` and `process_name` - at DNS time there is no address or process yet;
- `geosite` and remote `rule_set` - the file is downloaded, and may not exist
  when the core starts. Routes survive that, but DNS rule initialisation fails
  with `rule-set not found`.

**The remote resolver defaults to DoH on port 443.** DoT (853) through a proxy
often hangs until timeout: the port is blocked by ISPs and proxy servers alike.
The transport comes from the URL scheme in settings: `https://`, `tls://`,
`quic://`, `udp://`.

**Blocking is an action, not an outbound.** Blocking rules get
`"action": "reject"`; the `block` outbound was removed from the core.

**Protocol detection before routing.** The first rule is `"action": "sniff"`,
without which domain rules never match connections arriving from TUN by IP.

**Private addresses always go direct.** The `ip_is_private` rule sits above the
user rules.

**The Clash API** listens on `127.0.0.1:9191` - that is where traffic counters,
latency probes and selector switching come from.

---

## Troubleshooting

### "core not found" in the title bar

The path to `sing-box.exe` is wrong. Settings → **Browse**.

### Configuration rejected by the core

The core's error text is shown in full - read it, it names the field. A common
cause is a core older than 1.12 that does not understand `rule_set` and
`action`. Check `sing-box version`.

### A site goes through the wrong outbound

Enable **Verbose log** in settings and look for the domain:

```
router: match[0] => sniff                                       protocol detection
router: match[1] protocol=dns => hijack-dns                     DNS capture
router: match[3] rule_set=geosite-category-ru => route(direct)   your rule
outbound/direct[direct]: outbound connection to 87.240.185.168:443
```

Indices 0–2 are the built-in rules; yours start at three. If there is no match
and the connection went to `proxy`, the domain simply is not in the set.

A frequent cause: the site does not live in the zone you assume. `2ip.ru`, for
example, moved to `2ip.io`, and a list of Russian sites contains no `.io`. It
takes a minute to check - add a `domain_suffix` rule for that domain and see
whether the outbound changes.

### Connected, but no internet

Check the mode in the bottom left. With the system proxy only browsers and
applications that read Windows settings are affected. Everything else needs TUN.

### On first launch: `create adapter: file already exists`

```
FATAL start service: start inbound/tun[tun-in]: configure tun interface:
(create adapter: Cannot create a file when that file already exists.
 | open existing adapter: Element not found.)
```

A TUN adapter is left over from a previous session: the `sing-box` process was
killed and never removed the interface.

CVPN handles this at three levels: it sends Ctrl+C on shutdown so the core
cleans up after itself; it removes stale Wintun adapters before starting; and it
retries three seconds later if the error still happens. You will see "removed
stale adapters" and "retrying in 3 s" in the log.

If none of those lines appear and the error repeats, you are probably running
an old build - see [the section below](#code-changes-have-no-effect).

To remove the adapter by hand, from an elevated PowerShell:

```powershell
Get-NetAdapter -IncludeHidden | Where-Object InterfaceDescription -like "*Wintun*" | Remove-NetAdapter -Confirm:$false
```

### The application crashed or behaves oddly

Look in `%APPDATA%\CVPN\logs\` - the **Log folder** button on the Logs page.
One file per day, a week of history. Crashes are written as a block with a
heading, the build version and a full stack trace.

On a UI error the application asks whether to continue. That is deliberate:
closing with the tunnel up is worse than continuing in an uncertain state,
because the user is left without internet and without an explanation.

### Code changes have no effect

The first line of the log holds the version and the path of the running file:

```
[cvpn] build 1.0.0.0 · D:\Projects\CVPN\bin\Debug\net10.0-windows\CVPN.exe
```

If the path is not where you build, a different copy is running. The usual
culprit is the scheduled task pointing at the previous executable:

```
[cvpn] warning: the scheduled task launches a different file - C:\Program Files\CVPN\CVPN.exe
[cvpn] recreate the task in settings, otherwise your changes will not apply
```

Fix it in settings: **Delete task** → **Create task** from the right build.
While debugging it is easier not to keep the task at all and accept the UAC
prompt - that way the current file always runs.

The same trap applies to the service: it points at `CVPN.Service.exe` in the
install directory, so reinstall it after rebuilding.

### `192.168.x.x` timeouts in the log

Those are requests to local network devices - file shares, printers, the
router. The `ip_is_private` rule sends them direct, and if the device is
unreachable the connection times out. Nothing to do with the tunnel.

### `rule-set not found` at startup

```
FATAL start service: initialize DNS rule[0]: rule-set not found: geosite-youtube
```

There were two causes, both fixed.

First: remote sets were mirrored into DNS rules, but they are downloaded after
initialisation. Only domain conditions and local sets are mirrored now.

Second, the main one: the application stores `.srs` in the user directory,
while the service runs as `SYSTEM` and never looks there. The set was dropped
but the reference in the rules remained. Rule set files are now handed to the
service together with the configuration, and if a set is dropped anyway, the
references to it are removed - the rule simply does not apply instead of
crashing the core.

### Testing a domain

The field above the list answers the question that usually sends people into
the logs: which rule will match. Enter a domain or a full URL and the
application names the outcome and the matching rule, without bringing the
tunnel up.

The contents of `geosite` and `.srs` sets cannot be checked - they are binary
files the application does not have. If such a set sits above the match found,
the check says so plainly instead of guessing.

### Rule sets do not download

They are fetched through the tunnel, so the proxy has to work on the first
connect. Remove the geosite/geoip rules, connect, and add them back.

### The application will not start via `dotnet run`

That is expected if you put `requireAdministrator` back into the manifest:
`dotnet run` starts the process with `CreateProcess`, which cannot raise a UAC
prompt, and fails with error 740. The manifest in this repository is
deliberately `asInvoker`.

---

## Building from source

```bash
git clone https://github.com/13CyberPunk02/CVPN.git
cd CVPN
dotnet restore
dotnet build -c Release
dotnet run --project CVPN
```

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
Put `sing-box.exe` into `CVPN\bin\Debug\net10.0-windows\core\` or point at it in
settings.

The service is copied into a `service` subfolder next to the application
automatically - that is the `CopyServiceOutput` target in its `csproj`. If
**Install service** reports that the files are missing, build the whole
solution rather than just the application project.

> While debugging, avoid keeping both a scheduled task and an installed
> service: each points at a specific executable and will launch that instead of
> your build. The first log line tells you which file is actually running.

NaiveProxy additionally needs `libcronet.dll` next to `sing-box.exe`: the
protocol runs on Chromium's network stack. VLESS and AnyTLS do not need it.

### Dependencies

| Package | Why |
|---|---|
| [QRCoder](https://www.nuget.org/packages/QRCoder) | QR codes for export |
| [System.Security.Cryptography.ProtectedData](https://www.nuget.org/packages/System.Security.Cryptography.ProtectedData) | DPAPI for credentials |

Everything else is the standard library. The core is driven by
`System.Diagnostics.Process` directly - needed for a graceful shutdown, which
process wrappers do not offer.

---

## Tests

```bash
dotnet test
```

The suite covers the pure functions where mistakes are quietest and most
expensive: `config.json` generation, link parsing and building, importing
foreign configs, and the service sanitiser. Those are exactly the places where
bugs were found by hand - a missing selector tag, `geoip` in DNS rules, the
resolver transport.

Tests run in CI on every push and pull request
(`.github/workflows/tests.yml`).

---

## Building a release

```powershell
.\installer\build.ps1 -Version 1.0.0
```

The script publishes both projects, downloads `sing-box`, lays out the files
and invokes Inno Setup. The result is `dist\CVPN-1.0.0-setup.exe`.

Requires [Inno Setup 6](https://jrsoftware.org/isdl.php) and the .NET 10 SDK.

By default the build is framework-dependent: ~50 MB on disk, with the runtime
downloaded by the installer. `-SelfContained` produces a standalone ~200 MB
build, which is how the portable version is made.

Size is kept in check three ways: one copy of `sing-box` shared by the
application and the service, trimming the service (`PublishTrimmed` is not
supported for WPF but works for the console project), and disabling satellite
resource assemblies.

CI does the same via `.github/workflows/release.yml`: pushing a `v1.0.0` tag
builds the installer and the portable archive and attaches both to the release.

> The workflow needs write access: the `permissions: contents: write` block in
> the file and "Read and write permissions" under Settings → Actions → General.
> Without it, creating a release returns 403.
>
> Inno Setup is not preinstalled on the `windows-latest` image - the workflow
> installs it through Chocolatey in a separate step.

---

## Architecture

```
CVPN.sln
├── CVPN/                    WPF application
│   ├── Theme/               design tokens and control styles
│   ├── Controls/            ConnectDial - the connection dial
│   ├── Views/               6 pages + profile editor + export
│   ├── ViewModels/          MainViewModel + one per page
│   ├── Models/              ServerProfile, RouteRule, RoutingProfile
│   ├── Core/                Mvvm, Elevation, Secret
│   ├── Services/            ConfigBuilder, ClashApiClient, TrayIcon, …
│   ├── Ipc/                 service client
│   └── Assets/              icons and flags
│
├── CVPN.Service/            Windows service (Worker Service)
│   ├── PipeServer.cs        named pipe and its ACL
│   ├── CoreRunner.cs        starting and stopping sing-box
│   ├── ConfigSanitizer.cs   sanitising the incoming configuration
│   └── DataDirectory.cs     locking the directory down from ordinary users
│
├── CVPN.Shared/             code shared by the application and the service
│   ├── IpcContract.cs       pipe protocol
│   ├── ConsoleSignal.cs     graceful core shutdown
│   └── FileLog.cs           file logging
│
├── CVPN.Tests/              xUnit: config, links, sanitiser
├── installer/               Inno Setup and the build script
└── .github/workflows/       tests and release
```

Pages sit on their own view models: navigation supplies an object and WPF picks
the markup by `DataTemplate`, so page state survives navigation. The extraction
guide is in [REFACTORING.md](REFACTORING.md).

The application and the service do not reference each other - shared code lives
in `CVPN.Shared`. That way the pipe protocol cannot be changed on one side and
forgotten on the other.

Every colour lives in `Theme/Palette.xaml`; there are no literal colours
anywhere else. MVVM is hand-rolled in `Core/Mvvm.cs`: with this amount of logic
a toolkit would not earn its keep.

### Worth knowing when working on it

`ElementName` and `Storyboard.TargetName` **do not work** inside `Style` and
`ControlTemplate` - they have no access to the name scope. Use
`RelativeSource AncestorType` and property paths like
`(UIElement.RenderTransform).(RotateTransform.Angle)`.

Inside a `DataTemplate` the data context is the list item, not the page. Page
commands are reached with
`{Binding DataContext.Command, RelativeSource={RelativeSource AncestorType=UserControl}}`.

Models are serialised to JSON, so every computed property needs `[JsonIgnore]` -
otherwise the serialiser will try to write it too.

---

## Roadmap

- [x] Test a domain before saving a rule: show which rule will match
- [x] Quick server switching from the tray menu
- [ ] Automatic daily subscription refresh
- [ ] Per-session statistics
- [ ] Code-signed installer

Suggestions welcome in [issues](https://github.com/13CyberPunk02/CVPN/issues).

---

## Licences

CVPN is released under the MIT licence - see [LICENSE](LICENSE).

`sing-box` is released under GPL-3.0-or-later. The application runs the core as
a separate process over the command line, does not link against it and contains
none of its code, so this is aggregation rather than a derivative work. The
core's licence text ships next to the binary (`LICENSE.sing-box.txt`) as GPL
requires, and the build script does that automatically.

The sing-box licence separately forbids derivative works from using its name or
implying an association without consent. CVPN therefore does not call itself a
sing-box client in its name and states plainly that it is not affiliated with
the sing-box team.
