# SoundBridge

**Turn your Windows PC into a Bluetooth speaker for your phone — with a UI that actually feels like Windows 11.**

SoundBridge is a modern, open-source rewrite of
[AudioPlaybackConnector](https://github.com/ysc3839/AudioPlaybackConnector)
(a 1.6k-star project that has not shipped a release since September 2020).
It manages the Bluetooth **A2DP Sink** role that Windows 10 2004+ ships but
leaves without any first-party UI: pair your phone, click once, and your PC
plays whatever your phone plays.

> **What "A2DP Sink" means:** your PC acts like a Bluetooth speaker. Audio
> *arrives* from your phone/tablet; it does not go out to your headphones.

| | AudioPlaybackConnector (2020) | **SoundBridge (this project)** |
|---|---|---|
| UI | Win32 `DevicePicker` flyout popup | Fluent dashboard, device cards, Mica backdrop, dark/light theme |
| Start with Windows | No | **Yes** — per-user Run key, starts silently in the tray |
| Start minimized / tray-first | Menu-only | Tray icon + context menu + balloon notifications |
| Auto-reconnect | Partial, connection was forgotten on drop | Watchdog with adaptive backoff, per-device favorites (star), reconnects when a phone comes back in range |
| Close button | Exited the app | Minimizes to tray (configurable) |
| Platform | C++/Win32, hard to contribute | C# / .NET 8 / WinUI 3, single small project |
| Maintenance | Last release v1.3.1, Sep 2020 | Active, 35 open upstream issues triaged into a [roadmap](docs/ROADMAP.md) |

## Features

- **Device dashboard** — every paired A2DP-capable device as a card with live
  status, one-click connect/disconnect and a star toggle.
- **Start with Windows** — sign in and SoundBridge is already in your tray,
  waiting (this was the #1 feature request on the original tracker).
- **Auto-reconnect favorites** — star a device once; the watchdog re-establishes
  audio when it drops or your phone comes back in range, with adaptive backoff
  so the Bluetooth stack is never hammered.
- **Fluent, glassy UI** — Mica material on Windows 11 (graceful solid fallback
  on Windows 10), follows system light/dark or can be pinned.
- **Tray-first** — left-click opens the dashboard, right-click gives quick
  actions (connect last device, toggles, exit), balloon notifications on
  connect/reconnect.
- **No installer, no runtime prompts** — self-contained publish bundles the
  Windows App SDK and .NET runtime next to the exe.

## Requirements

- Windows 10 version 2004 (build 19041) or newer, or Windows 11
- A Bluetooth adapter (built-in or USB dongle)
- A phone/device that can act as an **A2DP source** (virtually all phones)
- For building: .NET 8 SDK + Windows App SDK (auto-restored via NuGet)

## Get the exe without building anything

You do **not** need to build locally — GitHub Actions does it for you:

- **Every push:** open the repo's **Actions → Build** tab, click the latest
  run, and download the `SoundBridge-x64` (or `-ARM64`) artifact. It contains
  a ready-to-run self-contained `SoundBridge.exe`.
- **Releases:** pushing a tag (`git tag v0.1.0 && git push --tags`) triggers
  `release.yml`, which publishes self-contained builds and attaches
  `SoundBridge-<version>-win-<arch>.zip` files to a GitHub Release.
- **Later:** `winget install <publisher>.SoundBridge` once the
  [winget submission](docs/winget-submission.md) is merged.

> Note: builds are unsigned for now, so SmartScreen may show "Windows
> protected your PC" on first run — choose *More info → Run anyway*.

## Build from source

### Visual Studio (recommended)

1. Install **Visual Studio 2022** (17.10+) with the **.NET desktop development**
   workload (WinUI 3 templates are *not* required to build this repo).
2. Open `SoundBridge.sln`.
3. Pick a platform (`x64` recommended) and press **F5**.

### Command line

```powershell
# Dev run
dotnet build src/SoundBridge -c Debug -p:Platform=x64

# Self-contained publish (no .NET / WinAppSDK install needed on target PCs)
dotnet publish src/SoundBridge -c Release -r win-x64 --self-contained -p:Platform=x64 -o publish
```

`ARM64` works identically (`-p:Platform=ARM64 -r win-arm64`).

## Usage

1. Launch SoundBridge.
2. If your phone isn't paired yet, press **Pair device** (opens Windows
   Bluetooth settings) and pair it.
3. Back in SoundBridge, press **Refresh** — your phone appears as a card.
4. Click **Connect**. Play music on your phone — it comes out of your PC.
5. Click the **star** to mark it as a favorite and let auto-reconnect handle it
   from now on (enable **Start with Windows** once and forget it).

Settings are stored in `%LOCALAPPDATA%\SoundBridge\settings.json`.

## Troubleshooting (answers to the most common upstream issues)

- **Connected but no sound** — Windows may be routing the incoming stream to
  the wrong endpoint. Check *Settings → System → Sound → Output* while
  connected. (Selectable per-app output routing is on the roadmap.)
- **Device never appears in the list** — the device must be *paired* in
  Windows Bluetooth settings first; SoundBridge lists paired A2DP devices.
- **Connection times out** — the phone must be awake and in range; some TVs
  and headsets only expose A2DP *sink* (not *source*) and will never connect.
- **Volume behaves oddly / faint audio at zero volume** — this is Windows'
  absolute-volume implementation with certain phones, not something the
  connector can change. See upstream issue tracker; the "Alternative A2DP
  Driver" is a known third-party workaround.

## How it works

`AudioPlaybackConnection` (WinRT, `Windows.Media.Audio`) is the
2004-era API that turns Windows into an A2DP sink. SoundBridge enumerates
devices with its AQS selector, then `TryCreateFromId → StartAsync → OpenAsync`
exactly like the original app — the difference is everything around it.
Details, diagrams and design decisions: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Roadmap

Prioritized straight from the 35 open issues on the original tracker:
multi-device simultaneous receive, AVRCP play/pause from the PC, output-device
routing, CLI verbs, winget/installer, localization. See
[docs/ROADMAP.md](docs/ROADMAP.md) — PRs welcome.

## Credits & license

MIT — see [LICENSE](LICENSE). The connection engine is a faithful C# port of
the mechanism in **AudioPlaybackConnector** by Richard Yu ([@ysc3839](https://github.com/ysc3839)).
Thank you for proving the concept and open-sourcing it. Tray icon support via
[H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) (MIT).

*(Screenshot placeholder — replace after first build.)*
