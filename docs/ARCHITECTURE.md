# Architecture

SoundBridge is a single WinUI 3 (Windows App SDK 1.8) desktop app targeting
`net8.0-windows10.0.19041.0`, deployed **unpackaged and self-contained**
(`WindowsPackageType=None` + `WindowsAppSDKSelfContained=true`): the exe runs
from any folder with no installer and no runtime download.

```
                        ┌──────────────────────────────────────────┐
                        │                  App                     │
                        │  composition root, single-instance mutex │
                        │  theme, --minimized arg, exit handling   │
                        └───────┬───────────────────┬──────────────┘
                                │                   │
              ┌─────────────────┴──────┐   ┌────────┴─────────────────────┐
              │      MainWindow        │   │         TrayService          │
              │  (device dashboard)    │   │  H.NotifyIcon tray icon      │
              │  MainViewModel         │   │  context menu, balloons      │
              │  DeviceViewModel (xN)  │◄──┤  left-click → ShowMainWindow │
              └───────┬────────────────┘   └────────┬─────────────────────┘
                      │                             │
        ┌─────────────┼──────────────┬──────────────┴───────────┐
        │             │              │                          │
┌───────▼──────┐ ┌────▼─────┐ ┌──────▼───────┐ ┌────────────────▼┐
│A2dpSinkService│ │Settings  │ │Reconnect     │ │AutoStartService │
│  (WinRT BT)  │ │Service   │ │Service       │ │ (HKCU Run key)  │
│ enumerate /  │ │ JSON in  │ │ watchdog,    │ │                 │
│ connect /    │ │ %LOCALAP-│ │ adaptive     │ └─────────────────┘
│ state events │ │ PDATA    │ │ backoff      │
└───────┬──────┘ └──────────┘ └──────┬───────┘
        │                            │
        │      Windows Runtime APIs (Windows SDK 10.0.19041)
        ▼
Windows.Media.Audio.AudioPlaybackConnection
Windows.Devices.Enumeration.DeviceInformation
```

## The Bluetooth core (ported 1:1 from the original)

The original C++ app (`ysc3839/AudioPlaybackConnector`) connects like this:

```cpp
auto connection = AudioPlaybackConnection::TryCreateFromId(device.Id());
connection.StateChanged(...);
co_await connection.StartAsync();
auto result = co_await connection.OpenAsync();
// result.Status(): Success | RequestTimedOut | DeniedBySystem | UnknownFailure
```

`A2dpSinkService` implements the identical sequence in C#:

```csharp
var connection = AudioPlaybackConnection.TryCreateFromId(device.Id);
connection.StateChanged += OnConnectionStateChanged;
await connection.StartAsync();          // tell Windows this app wants audio
var result = await connection.OpenAsync(); // establish the A2DP link
```

- **Enumeration** — `AudioPlaybackConnection.GetDeviceSelector()` returns an
  Advanced Query Syntax string selecting every paired device capable of acting
  as an A2DP source. The original appended this selector to a Win32
  `DevicePicker` popup; SoundBridge runs it through
  `DeviceInformation.FindAllAsync` and renders the results as its own Fluent
  card list (this is the single biggest UI improvement).
- **Lifecycle** — `StateChanged` reports `Opened`/`Closed`. A `Closed` event
  removes and disposes the connection exactly like the original; the
  ReconnectService then picks favorites back up automatically (the original
  just forgot the device until you clicked again).
- **Disconnect** — `connection.Close()`; cleanup is idempotent on both paths.

## Threading model

- `StateChanged` and the watchdog loop arrive on **background threads**.
- `App.UiDispatcher` (captured at launch) marshals every UI mutation through
  `TryEnqueue`.
- Tray balloon notifications from background code go through
  `TrayService.NotifyFromBackground` → same dispatcher.
- Services never touch XAML objects directly; they raise plain .NET events.

## Persistence

| What | Where |
|---|---|
| Settings | `%LOCALAPPDATA%\SoundBridge\settings.json` |
| Auto-start | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `"...\SoundBridge.exe" --minimized` |

Per-user Run keys were chosen over MSIX startup tasks because the app is
deliberately unpackaged: no admin rights, no package identity, works on plain
folder distributions. `--minimized` makes the launch silent (tray only).

## Single instance

A named `Mutex` (`Local\SoundBridge.SingleInstance`) prevents a second tray
icon when the autostart entry and a manual launch race.

## UI specifics

- **Mica** (`MicaBackdrop`) is applied only on Windows 11 (build 22000+);
  Windows 10 falls back to the solid `ApplicationPageBackgroundThemeBrush`.
- Custom title bar via `ExtendsContentIntoTitleBar` + `SetTitleBar`; all
  interactive controls live outside the drag region.
- Device cards bind status via pre-computed `SolidColorBrush` properties on
  `DeviceViewModel`, avoiding converter classes entirely.
- Theme changes apply per-window (`RootGrid.RequestedTheme`) at runtime;
  the process-level `Application.RequestedTheme` is only set at startup.

## Versioning of the API surface

The entire Bluetooth feature set depends on APIs introduced in **Windows 10
2004 (build 19041)**, hence `TargetPlatformMinVersion=10.0.19041.0` — matching
the original project's requirement exactly.
