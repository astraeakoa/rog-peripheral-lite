param([string]$OutputDir = "dist")
$ErrorActionPreference = 'Stop'
$env:LIB = ''
Push-Location $PSScriptRoot
try {
    dotnet run --project tests/ROGPeripheral.Tests.csproj -c Release -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    dotnet publish src/ROGPeripheral.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $OutputDir -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    Copy-Item LICENSE,README.md,THIRD_PARTY_NOTICES.md,docs/MACRO_FORMATS.md $OutputDir
    Copy-Item examples $OutputDir -Recurse -Force
    Copy-Item licenses $OutputDir -Recurse -Force
    # Ship notices matching the runtime selected by this SDK, including on CI.
    $restore = Get-Content src/obj/project.assets.json -Raw | ConvertFrom-Json
    foreach ($framework in $restore.project.frameworks.PSObject.Properties.Value) {
        foreach ($dependency in $framework.downloadDependencies) {
            if ($dependency.name -notin @('Microsoft.NETCore.App.Runtime.win-x64','Microsoft.WindowsDesktop.App.Runtime.win-x64')) { continue }
            $version = $dependency.version.Trim('[',']').Split(',')[0].Trim()
            $package = $null
            foreach ($folder in $restore.packageFolders.PSObject.Properties.Name) {
                $candidate = Join-Path $folder ($dependency.name.ToLowerInvariant() + '/' + $version)
                if (Test-Path -LiteralPath $candidate) { $package = $candidate; break }
            }
            if (!$package) { throw "Runtime notices not found for $($dependency.name) $version" }
            if ($dependency.name -eq 'Microsoft.NETCore.App.Runtime.win-x64') {
                Copy-Item "$package/LICENSE.TXT" "$OutputDir/licenses/dotnet-runtime.txt" -Force
                Copy-Item "$package/THIRD-PARTY-NOTICES.TXT" "$OutputDir/licenses/dotnet-third-party.txt" -Force
            } else {
                Copy-Item "$package/LICENSE" "$OutputDir/licenses/dotnet-windowsdesktop.txt" -Force
            }
        }
    }
    New-Item -ItemType Directory -Force "$OutputDir/docs" | Out-Null
    Copy-Item docs/MACRO_FORMATS.md,docs/CHANGELOG.md,docs/MACRO_EDITOR_QA.md "$OutputDir/docs"
    Compress-Archive -Path "$OutputDir/ROGPeripheral.exe","$OutputDir/LICENSE","$OutputDir/README.md","$OutputDir/THIRD_PARTY_NOTICES.md","$OutputDir/MACRO_FORMATS.md","$OutputDir/examples","$OutputDir/licenses","$OutputDir/docs" -DestinationPath ROGPeripheral-win-x64.zip -Force
} finally { Pop-Location }
