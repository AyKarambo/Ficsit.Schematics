#Requires -Version 7
<#
.SYNOPSIS
	Signs the draft release's installer locally and publishes the GitHub release.

.DESCRIPTION
	The release workflow uploads an UNSIGNED installer to a DRAFT GitHub Release,
	because Certum SimplySign only signs where its desktop app runs. This script is
	the maintainer-side second half: with SimplySign Desktop active it

	  1. checks the environment (gh authenticated, signtool present, a code-signing
	     certificate visible in CurrentUser\My, the draft release and its .msi exist),
	  2. downloads the draft's installer,
	  3. signs it (signtool sign /fd SHA256 /tr <RFC-3161> /td SHA256),
	  4. verifies the signature (signtool verify /pa),
	  5. replaces the release asset and publishes the release.

	It refuses to publish an unsigned installer unless -AllowUnsigned is passed
	explicitly. With -File it signs + verifies a local MSI instead and never touches
	GitHub. One-time setup: see installer/README.md.

.PARAMETER Tag
	The release tag to sign and publish, e.g. v0.1.0. The release must exist as a draft
	with exactly one .msi asset (created by .github/workflows/release.yml).

.PARAMETER File
	Sign + verify a local MSI instead of a draft release asset (no GitHub interaction).

.PARAMETER TimestampUrl
	RFC-3161 timestamp server (default: Certum's http://time.certum.pl).

.PARAMETER AllowUnsigned
	Explicit escape hatch: publish the release without signing. Never the default.

.EXAMPLE
	./installer/sign-release.ps1 -Tag v0.1.0

.EXAMPLE
	./installer/sign-release.ps1 -File installer/out/FicsitSchematics-Setup-0.1.0.msi
#>
[CmdletBinding(DefaultParameterSetName = 'Release')]
param(
	[Parameter(Mandatory, ParameterSetName = 'Release', Position = 0)]
	[string]$Tag,

	[Parameter(Mandatory, ParameterSetName = 'File')]
	[string]$File,

	[string]$TimestampUrl = 'http://time.certum.pl',

	[Parameter(ParameterSetName = 'Release')]
	[switch]$AllowUnsigned
)

$ErrorActionPreference = 'Stop'

function Find-SignTool {
	$cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
	if ($cmd) { return $cmd.Source }
	$kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
	if (Test-Path $kitsRoot) {
		$candidates = Get-ChildItem $kitsRoot -Directory |
			Where-Object Name -match '^10\.' | Sort-Object { [version]$_.Name } -Descending |
			ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
			Where-Object { Test-Path $_ }
		if ($candidates) { return $candidates[0] }
	}
	throw ("signtool.exe not found. Install the Windows SDK 'Signing Tools for Desktop Apps' " +
		"component (winget install Microsoft.WindowsSDK.10.0.26100) or add signtool to PATH.")
}

function Assert-SigningCertVisible {
	$certs = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
		Where-Object { $_.NotAfter -gt (Get-Date) }
	if (-not $certs) {
		throw ("No valid code-signing certificate visible in Cert:\CurrentUser\My. " +
			"Start SimplySign Desktop and connect (the Certum card exposes the certificate " +
			"only while the app is running), then retry.")
	}
	Write-Host "Code-signing certificate(s) visible: $(($certs | ForEach-Object Subject) -join '; ')"
}

function Invoke-Sign([string]$signtool, [string]$path) {
	Write-Host "Signing $path ..."
	& $signtool sign /a /fd SHA256 /tr $TimestampUrl /td SHA256 $path
	if ($LASTEXITCODE -ne 0) { throw "signtool sign failed (exit $LASTEXITCODE)." }
}

function Test-Signature([string]$signtool, [string]$path) {
	& $signtool verify /pa $path
	return ($LASTEXITCODE -eq 0)
}

# ---- local file mode: sign + verify, no GitHub -------------------------------
if ($PSCmdlet.ParameterSetName -eq 'File') {
	if (-not (Test-Path $File)) { throw "File not found: $File" }
	$signtool = Find-SignTool
	Assert-SigningCertVisible
	Invoke-Sign $signtool $File
	if (-not (Test-Signature $signtool $File)) { throw 'Signature verification (signtool verify /pa) failed.' }
	Write-Host "Signed and verified: $File"
	return
}

# ---- release mode: draft -> sign -> replace asset -> publish -----------------
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
	throw 'GitHub CLI (gh) not found. Install it and run gh auth login.'
}
gh auth status *> $null
if ($LASTEXITCODE -ne 0) { throw 'gh is not authenticated. Run: gh auth login' }

$releaseJson = gh release view $Tag --json isDraft,assets,url 2>$null
if ($LASTEXITCODE -ne 0) {
	throw "No release found for tag '$Tag'. Did the release workflow run (it creates the draft)?"
}
$release = $releaseJson | ConvertFrom-Json
if (-not $release.isDraft) {
	throw "Release '$Tag' is already published. This script only signs and publishes drafts."
}
$msiAssets = @($release.assets | Where-Object name -like '*.msi')
if ($msiAssets.Count -ne 1) {
	throw "Expected exactly one .msi asset on draft '$Tag' but found $($msiAssets.Count) ($(($msiAssets | ForEach-Object name) -join ', '))."
}
$assetName = $msiAssets[0].name

if (-not $AllowUnsigned) {
	$signtool = Find-SignTool
	Assert-SigningCertVisible
}

$workDir = Join-Path ([System.IO.Path]::GetTempPath()) "ficsit-sign-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $workDir | Out-Null
try {
	if ($AllowUnsigned) {
		Write-Warning 'Publishing UNSIGNED (-AllowUnsigned was passed) - users will see a hard SmartScreen warning.'
	}
	else {
		Write-Host "Downloading $assetName from draft '$Tag' ..."
		gh release download $Tag --pattern '*.msi' --dir $workDir
		if ($LASTEXITCODE -ne 0) { throw 'gh release download failed.' }
		$msi = Join-Path $workDir $assetName

		Invoke-Sign $signtool $msi
		if (-not (Test-Signature $signtool $msi)) {
			throw 'Signature verification (signtool verify /pa) failed - not publishing. Pass -AllowUnsigned only if you really mean to ship unsigned.'
		}

		Write-Host "Replacing the release asset with the signed installer ..."
		gh release upload $Tag $msi --clobber
		if ($LASTEXITCODE -ne 0) { throw 'gh release upload failed.' }
	}

	Write-Host "Publishing release '$Tag' ..."
	gh release edit $Tag --draft=false
	if ($LASTEXITCODE -ne 0) { throw 'gh release edit --draft=false failed.' }

	Write-Host "Done: $($release.url)"
}
finally {
	Remove-Item -Recurse -Force $workDir -ErrorAction SilentlyContinue
}
