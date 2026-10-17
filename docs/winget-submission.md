# Publishing SoundBridge to winget

Goal: `winget install YOURGITHUBHANDLE.SoundBridge` works for anyone.

The three manifest templates live in `packaging/winget/`. They follow the
[winget-pkgs](https://github.com/microsoft/winget-pkgs) manifest schema 1.6.0.

## Prerequisites

1. A public GitHub repo with at least one **published Release** (push a tag
   `v0.1.0` — `release.yml` builds and attaches the zips automatically).
2. Your GitHub handle chosen as the publisher id. Community packages usually
   use the handle: `YOURGITHUBHANDLE.SoundBridge`.

## Step 1 — compute SHA256 for each release zip

```powershell
Get-FileHash .\SoundBridge-0.1.0-win-x64.zip -Algorithm SHA256
Get-FileHash .\SoundBridge-0.1.0-win-arm64.zip -Algorithm SHA256
```

Paste the hashes into `SoundBridge.installer.yaml`.

## Step 2 — fill in the placeholders

Replace every `YOURGITHUBHANDLE`, version string and `PASTE_SHA256...`
across the three files. They must all agree on `PackageIdentifier` and
`PackageVersion`.

## Step 3 — submit

Two options:

**Easy — wingetcreate (recommended):**

```powershell
winget install wingetcreate
wingetcreate update YOURGITHUBHANDLE.SoundBridge --version 0.1.0 --urls <x64 zip url>;<arm64 zip url>
```

`wingetcreate` generates the manifests from the installer URLs, validates
them, and opens a PR against `microsoft/winget-pkgs` for you. When using this
route you can skip the hand-written templates — they remain useful as
documentation and for review.

**Manual:**

1. Fork `microsoft/winget-pkgs`.
2. Create `manifests/y/YOURGITHUBHANDLE/SoundBridge/0.1.0/` and copy the
   three manifests in (note the first letter of the publisher is the top
   folder).
3. Validate locally: `winget validate --manifest .` (or the repo's Azure
   pipeline validates on the PR).
4. Open a PR titled `Add: YOURGITHUBHANDLE.SoundBridge 0.1.0`.
5. A bot (`winget-pkgs-automation`) reviews; respond to any schema feedback.

## Code signing — current status

Release zips are **unsigned** (no certificate available yet). Consequences:

- SmartScreen may warn on first run: "More info → Run anyway".
- winget accepts unsigned portable/zip installs fine.

When ready, the cleanest upgrade path is **Azure Trusted Signing**
(pay-as-you-go, no EV hardware token, integrates into `release.yml` via
`dotnet sign` or signtool task), which removes SmartScreen warnings for
reputation-built binaries. Until then the README documents the
SmartScreen workaround.

## Later versions

For each new release: bump `PackageVersion` in all three manifests, update
URLs + hashes, and put the folder under the new version path (a PR per
version). `wingetcreate update` automates exactly this.
