# Roadmap

Priorities are derived from real, open issues on the original
[AudioPlaybackConnector tracker](https://github.com/ysc3839/AudioPlaybackConnector/issues)
— each item names the upstream issue it addresses.

## P0 — next release (Pass 1 complete)

- [x] **winget manifest + release automation** (upstream #25 "Add to winget",
      #18 "Installer") — `release.yml` publishes self-contained zips to
      GitHub Releases on every `v*` tag; winget manifest templates live in
      `packaging/winget/` with a full submission walkthrough in
      [docs/winget-submission.md](winget-submission.md). *Code signing is
      deferred until a certificate / Azure Trusted Signing is available.*
- [x] **Event-driven reconnect** — `StateChanged(Closed)` drops of favorite
      devices trigger an immediate +5s/+15s/+30s retry burst; the adaptive
      poll remains as a safety net. Manual disconnects are honored via a
      suppression list (upstream #6 "Auto-Connect", #9 "repeatedly reconnect
      when moving away", #24 "randomly dropped").
- [x] **First-run UX** — an InfoBar hint card with a deep-link button into
      Bluetooth settings appears whenever no paired A2DP devices are found.
- [ ] **Localization infrastructure** — .resw strings, zh-CN first (the
      original README is bilingual and a large share of the user base is
      Chinese-speaking). *Deliberately sequenced after the P1 UI features
      settle so strings are translated once, not twice.*

## Phase 0.5 — localization (standalone pass after P1 UI stabilizes)

- [ ] `Strings/en-US/Resources.resw` + `Strings/zh-CN/Resources.resw`
- [ ] `x:Uid` wiring across MainWindow/InfoBar/tray menu
- [ ] Culture-detection + language switcher in settings

## P1 — high-value user requests

- [ ] **Multiple simultaneous devices** (upstream #5 "listen to 2 devices at
      the same time") — the service already keys connections by device id;
      the watchdog and UI need multi-select semantics.
- [ ] **Output device routing** (upstream #8 "Request: Change output", #19, #21
      "Select audio output device") — let users pick which render endpoint the
      incoming A2DP stream plays on (currently default-only).
- [ ] **AVRCP media controls** (upstream #7, #22 "send pause/play commands") —
      play/pause/next from the PC card when the phone supports AVRCP
      passthrough.
- [ ] **CLI verbs** (upstream #1 "connect and disconnect command-line") —
      `SoundBridge.exe connect <name|id>`, `disconnect`, `list`, `status` so
      scripts and hotkey tools can drive connections.

## P2 — polish and platform reach

- [ ] **MSIX + Store package** with optional identity (notifications upgrade
      to AppNotifications when identity exists).
- [ ] **Diagnostics page** — adapter info, codec in use (SBC vs vendor
      codecs), last N connection events with reasons; copyable for bug
      reports (upstream #23 codec questions, #17 Alternative A2DP Driver).
- [ ] **Audio capture tap for OBS** (upstream #12) — expose a virtual capture
      endpoint or document a loopback workflow.
- [ ] **Volume sync toggle** — workaround switch for the absolute-volume
      quirks reported in upstream #3 (residual audio at volume 0) and #4.
- [ ] **Per-device tray quick menu** — right-click tray → devices → connect.
- [ ] **ARM64 smoke-test workflow** on real hardware in CI.

## Non-goals (documented deliberately)

- **Custom A2DP codecs (LDAC/aptX on the sink side)** — gated by the Windows
  Bluetooth stack, not by any connector app; replacing the driver is out of
  scope for a user-space app (see upstream #17 discussion).
- **Audio *output* to Bluetooth devices (A2DP source role)** — Windows already
  does this natively; SoundBridge is specifically the *sink* (receive) story.
