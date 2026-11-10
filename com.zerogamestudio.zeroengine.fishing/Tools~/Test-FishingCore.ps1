#Requires -Version 7.0
[CmdletBinding()]
param([string]$ResultsDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('ze-fishing-tests-' + [guid]::NewGuid().ToString('N'))))
$ErrorActionPreference = 'Stop'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'The portable tests require an existing .NET SDK.' }
& dotnet test (Join-Path $PSScriptRoot 'PortableTests/Fishing.Tests.csproj') --results-directory $ResultsDirectory --logger 'trx;LogFileName=fishing-core.trx' --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "Fishing core tests failed: exit $LASTEXITCODE" }
