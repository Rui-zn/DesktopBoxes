param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$artifactRoot = Join-Path $projectRoot "artifacts"
$installerPublish = Join-Path $artifactRoot "installer"
$portablePublish = Join-Path $artifactRoot "portable"
$portableZip = Join-Path $artifactRoot "DesktopBoxes-portable-win-x64-$Version.zip"
$standaloneExe = Join-Path $artifactRoot "DesktopBoxes-win-x64-$Version.exe"
$project = Join-Path $projectRoot "src\DesktopBoxes.App\DesktopBoxes.App.csproj"

$artifactRootFull = [IO.Path]::GetFullPath($artifactRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
foreach ($target in @($installerPublish, $portablePublish)) {
    $targetFull = [IO.Path]::GetFullPath($target)
    if (-not $targetFull.StartsWith($artifactRootFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a path outside the artifacts directory: $targetFull"
    }
    if (Test-Path -LiteralPath $targetFull) {
        if (Test-Path -LiteralPath (Join-Path $targetFull "data")) {
            throw "Refusing to clean portable user data: $targetFull\data. Move the build output to a safe location first."
        }
        Remove-Item -LiteralPath $targetFull -Recurse -Force
    }
}

dotnet publish $project -c Release -p:RestoreLockedMode=true -p:DebugType=None -p:DebugSymbols=false -p:Version=$Version -o $installerPublish
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
Copy-Item -LiteralPath (Join-Path $installerPublish "DesktopBoxes.App.exe") -Destination $standaloneExe -Force

Copy-Item -LiteralPath $installerPublish -Destination $portablePublish -Recurse -Force
New-Item -ItemType File -Path (Join-Path $portablePublish "portable") -Force | Out-Null

if (Test-Path -LiteralPath $portableZip) {
    Remove-Item -LiteralPath $portableZip -Force
}
Compress-Archive -Path (Join-Path $portablePublish "*") -DestinationPath $portableZip -CompressionLevel Optimal

Write-Host "Installer input: $installerPublish"
Write-Host "Standalone executable: $standaloneExe"
Write-Host "Portable package: $portableZip"
