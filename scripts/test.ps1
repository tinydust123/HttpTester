$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$buildPath = Join-Path $projectRoot 'artifacts/build'
& dotnet run --project (Join-Path $projectRoot 'tests/HttpTester.Tests') -c Release --artifacts-path $buildPath
if ($LASTEXITCODE -ne 0) { throw 'Network and storage tests failed.' }
& dotnet run --project (Join-Path $projectRoot 'tests/HttpTester.UiTests') -c Release --artifacts-path $buildPath -- (Join-Path $projectRoot 'artifacts/v1.1/screenshots')
if ($LASTEXITCODE -ne 0) { throw 'UI logic tests failed.' }
