param([switch]$Standalone, [switch]$DesktopShortcut, [switch]$Package)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'src/HttpTester.App/HttpTester.App.csproj'
$projectXml = [xml](Get-Content -LiteralPath $projectPath -Encoding UTF8 -Raw)
$appName = [string]$projectXml.Project.PropertyGroup.AssemblyName
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($appName) -or [string]::IsNullOrWhiteSpace($version)) { throw 'Project name or version is missing.' }
$versionParts = $version.Split('.')
$series = if ($versionParts.Length -ge 2) { $versionParts[0] + '.' + $versionParts[1] } else { $version }
$flavor = if ($Standalone) { 'standalone' } else { 'portable' }
$publishPath = Join-Path $projectRoot "artifacts/v$series/$flavor"
$buildPath = Join-Path $projectRoot 'artifacts/build'
$publishArguments = @('publish', $projectPath, '-c', 'Release', '-r', 'win-x64', '--self-contained', $Standalone.ToString().ToLowerInvariant(), '-p:PublishSingleFile=true', '-p:DebugType=None', '-p:DebugSymbols=false', '--artifacts-path', $buildPath, '-o', $publishPath)
if ($Standalone) { $publishArguments += @('-p:EnableCompressionInSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true') }
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $publishPath 'README.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $publishPath 'LICENSE')
Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Destination (Join-Path $publishPath 'CHANGELOG.md')
$executablePath = Join-Path $publishPath ($appName + '.exe')
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) { throw "Published executable not found: $executablePath" }
if ($DesktopShortcut) {
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) ($appName + '.lnk')
    if (Test-Path -LiteralPath $shortcutPath) {
        Write-Host "Desktop shortcut already exists; left unchanged: $shortcutPath"
    } else {
        $shortcutShell = New-Object -ComObject WScript.Shell
        $shortcut = $shortcutShell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $executablePath
        $shortcut.WorkingDirectory = $publishPath
        $shortcut.IconLocation = "$executablePath,0"
        $shortcut.Description = $appName
        $shortcut.Save()
        Write-Host "Desktop shortcut created: $shortcutPath"
    }
}
if ($Package) {
    $packagePath = Join-Path $projectRoot 'artifacts/packages'
    $absolutePackagePath = [IO.Path]::GetFullPath($packagePath) + [IO.Path]::DirectorySeparatorChar
    $stagePath = [IO.Path]::GetFullPath((Join-Path $packagePath ('stage-' + [Guid]::NewGuid().ToString('N'))))
    if (-not $stagePath.StartsWith($absolutePackagePath, [StringComparison]::OrdinalIgnoreCase)) { throw 'Package staging path escapes the package directory.' }
    $zipName = "$appName-$version-$flavor-win-x64.zip"
    $zipPath = Join-Path $packagePath $zipName
    $hashPath = "$zipPath.sha256"
    try {
        New-Item -ItemType Directory -Path $stagePath -Force | Out-Null
        Copy-Item -LiteralPath $executablePath -Destination $stagePath
        foreach ($document in @('README.md', 'LICENSE', 'CHANGELOG.md')) {
            Copy-Item -LiteralPath (Join-Path $publishPath $document) -Destination $stagePath
        }
        $imageDestination = Join-Path $stagePath 'docs/images'
        New-Item -ItemType Directory -Path $imageDestination -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/images/main-window.png') -Destination $imageDestination

        if ($Standalone) {
            $assetsFile = Get-ChildItem -LiteralPath $buildPath -Filter 'project.assets.json' -File -Recurse |
                Where-Object { $_.FullName -match '[\\/]HttpTester\.App[\\/]' } |
                Sort-Object LastWriteTimeUtc -Descending |
                Select-Object -First 1
            if ($null -eq $assetsFile) { throw 'The application project assets file was not found.' }
            $assetsJson = Get-Content -LiteralPath $assetsFile.FullName -Encoding UTF8 -Raw | ConvertFrom-Json
            $nugetOutput = (& dotnet nuget locals global-packages --list) | Out-String
            $nugetRoot = ($nugetOutput -replace '(?s).*?global-packages:\s*', '').Trim()
            $noticesPath = Join-Path $stagePath 'THIRD-PARTY-NOTICES'
            New-Item -ItemType Directory -Path $noticesPath -Force | Out-Null
            $noticeCount = 0
            foreach ($framework in $assetsJson.project.frameworks.PSObject.Properties) {
                foreach ($dependency in @($framework.Value.downloadDependencies)) {
                    if ($null -eq $dependency -or $dependency.name -notmatch '(?i)Runtime[.]win-x64$') { continue }
                    $dependencyVersion = (($dependency.version -replace '[\[\]\(\)]', '').Split(',')[0]).Trim()
                    $packageRoot = Join-Path $nugetRoot ($dependency.name.ToLowerInvariant() + '/' + $dependencyVersion)
                    if (-not (Test-Path -LiteralPath $packageRoot -PathType Container)) { throw "NuGet runtime package not found: $($dependency.name) $dependencyVersion" }
                    $licenseFiles = @(Get-ChildItem -LiteralPath $packageRoot -File |
                        Where-Object { $_.Name -match '(?i)^(LICENSE.*|THIRD-PARTY-NOTICES.*)$' })
                    if ($licenseFiles.Count -eq 0) { throw "Runtime license or notices not found: $($dependency.name) $dependencyVersion" }
                    foreach ($licenseFile in $licenseFiles) {
                        $noticeName = "$($dependency.name)-$dependencyVersion-$($licenseFile.Name)"
                        Copy-Item -LiteralPath $licenseFile.FullName -Destination (Join-Path $noticesPath $noticeName)
                        $noticeCount++
                    }
                }
            }
            if ($noticeCount -eq 0) { throw 'No .NET runtime licenses were collected for the standalone package.' }
        }

        if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
        if (Test-Path -LiteralPath $hashPath) { Remove-Item -LiteralPath $hashPath -Force }
        Compress-Archive -Path (Join-Path $stagePath '*') -DestinationPath $zipPath -CompressionLevel Optimal
        $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        [IO.File]::WriteAllText($hashPath, "$hash *$zipName`n", [Text.UTF8Encoding]::new($false))
        Write-Host "Package: $zipPath"
        Write-Host "SHA-256: $hash"
    }
    finally {
        if (Test-Path -LiteralPath $stagePath -PathType Container) { Remove-Item -LiteralPath $stagePath -Recurse -Force }
    }
}
Write-Host "Output: $publishPath"
