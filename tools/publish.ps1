# Builds the Windows release: dist\EmpyrionAdminManager-<version>\ and its zip (needs the .NET 8 SDK).
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
$version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version
$name = "EmpyrionAdminManager-$version"
if (Test-Path dist) { Remove-Item -Recurse -Force dist }
$common = @('-c','Release','-r','win-x64','--self-contained','true','-p:DebugType=none','-o',"dist\$name")
dotnet publish src\Eam.Engine\Eam.Engine.csproj @common
dotnet publish src\Eam.Desktop\Eam.Desktop.csproj @common
Copy-Item -Recurse packaging\* "dist\$name"
Copy-Item LICENSE "dist\$name\LICENSE.txt"
Copy-Item CHANGELOG.md "dist\$name\CHANGELOG.md"
Compress-Archive -Path "dist\$name" -DestinationPath "dist\$name.zip"
Write-Host "Built dist\$name.zip"
