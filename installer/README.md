# Releasing Ficsit.Schematics

A release takes two manual steps:

```
1. git tag v0.1.0 && git push origin v0.1.0     (or run the Release workflow manually)
       │
       ▼
   GitHub Actions (release.yml, windows-latest)
   build → test → self-contained win-x64 publish → WiX MSI
       │
       ▼
   DRAFT GitHub Release "v0.1.0" + UNSIGNED FicsitSchematics-Setup-0.1.0.msi
       │
2. ./installer/sign-release.ps1 -Tag v0.1.0     (your machine, SimplySign Desktop running)
   download → signtool sign (RFC-3161 timestamp) → signtool verify /pa
   → replace asset → publish the release
```

Signing cannot happen in CI: the Certum Open Source certificate lives behind
**SimplySign Desktop**, which only runs interactively on your machine. The draft
therefore stays private until the signed installer replaces the unsigned one.

## One-time setup (maintainer machine)

1. **SimplySign Desktop** — install Certum's SimplySign Desktop and confirm you can
   connect. While connected, the certificate must appear in the *current user's*
   certificate store; check with:

   ```pwsh
   Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert
   ```

2. **signtool** — part of the Windows SDK "Signing Tools for Desktop Apps":

   ```pwsh
   winget install Microsoft.WindowsSDK.10.0.26100
   ```

   `sign-release.ps1` finds it on `PATH` or under `%ProgramFiles(x86)%\Windows Kits\10\bin`.

3. **GitHub CLI** — `winget install GitHub.cli`, then `gh auth login`.

Certum Open Source cert reminders: the signed software must stay open-source /
non-commercial, and the account shares a **5000 signatures/month** cap — one
signature per release is expected usage.

## Cutting a release

```pwsh
git tag v0.1.0
git push origin v0.1.0
# wait for the Release workflow to finish (Actions tab), then:
./installer/sign-release.ps1 -Tag v0.1.0
```

The script refuses to publish anything that does not pass `signtool verify /pa`.
If you ever genuinely need to ship unsigned, that decision has to be explicit:
`-AllowUnsigned` skips signing and publishes with a warning.

Versions are semver, injected from the tag: `v0.2.0-rc.1` produces an exe with
file version `0.2.0` and informational version `0.2.0-rc.1`; the MSI
`ProductVersion` is the numeric `0.2.0` (MSI cannot carry prerelease suffixes,
so two prereleases of the same triple upgrade-replace each other). Tags with a
`-` suffix are automatically marked prerelease on the release.

## Test builds without a tag

Run the **Release** workflow manually (Actions → Release → *Run workflow*) with a
version like `0.2.0-rc.1` and the prerelease flag. This produces a normal draft
with the unsigned MSI but **no tag** — the tag only comes into existence if the
draft is published. Delete test drafts when you are done with them:

```pwsh
gh release delete v0.2.0-rc.1 --yes
```

## Building the installer locally

```pwsh
./installer/build-installer.ps1 -Version 0.1.0-local
# → installer/out/FicsitSchematics-Setup-0.1.0-local.msi (unsigned)
# optional: sign a local file without touching GitHub
./installer/sign-release.ps1 -File installer/out/FicsitSchematics-Setup-0.1.0-local.msi
```

The script publishes the app (self-contained win-x64, `Properties/PublishProfiles/win-x64.pubxml`),
then builds `Product.wxs` with the WiX toolset pinned in `.config/dotnet-tools.json`
(`dotnet tool restore` happens automatically). CI runs exactly this script.

The MSI installs **per user** (no admin prompt) to `%LOCALAPPDATA%\Programs\Ficsit.Schematics`,
with a Start Menu shortcut and an Add/Remove Programs entry.

## SmartScreen note

A young certificate has no SmartScreen reputation: users may see a soft
"unrecognized app" prompt at first. That is expected and clears as installs
accumulate — it is not a defect in the pipeline.
