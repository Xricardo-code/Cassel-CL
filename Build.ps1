param(
    [string]$Original = (Join-Path $PSScriptRoot 'input\Plain Craft Launcher 2.exe'),
    [string]$Output = (Join-Path $PSScriptRoot 'release-no-updates\Cassell CL - 无更新版.exe'),
    [switch]$Preview
)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path $compiler
$themeFile = Join-Path $PSScriptRoot $(if($Preview){'tools\InfiniteCL.Preview.dll'}else{'tools\InfiniteCL.Theme.dll'})
$compilerSettings = @('/nologo','/optimize+','/target:library')
if($Preview){$compilerSettings += '/define:PREVIEW'}
$assets = Join-Path $PSScriptRoot 'assets'
& $compiler /nologo /optimize+ "/out:$PSScriptRoot\tools\PatchBrand.exe" "/reference:$PSScriptRoot\tools\dnlib.dll" (Join-Path $PSScriptRoot 'PatchBrand.cs') (Join-Path $PSScriptRoot 'LauncherUpdatePolicy.cs')
if ($LASTEXITCODE -ne 0) { throw 'Brand and update-policy patcher compilation failed.' }
New-Item -ItemType Directory -Path (Split-Path $Output) -Force | Out-Null
& $compiler @compilerSettings "/out:$themeFile" "/resource:$assets\Cassell.png,InfiniteCL.png" "/resource:$assets\DragonAcademy.png,InfiniteCL.City.png" "/resource:$assets\Ricardo.jpg,InfiniteCL.Ricardo.jpg" "/resource:$assets\OriginalAuthor.jpg,InfiniteCL.OriginalAuthor.jpg" "/reference:$framework\WPF\PresentationCore.dll" "/reference:$framework\WPF\PresentationFramework.dll" "/reference:$framework\WPF\WindowsBase.dll" "/reference:$framework\System.Xaml.dll" (Join-Path $PSScriptRoot 'Theme.cs') (Join-Path $PSScriptRoot 'PointerLight.cs') (Join-Path $PSScriptRoot 'RainScene.cs') (Join-Path $PSScriptRoot 'NavigationShell.cs') (Join-Path $PSScriptRoot 'InterfaceCopy.cs') (Join-Path $PSScriptRoot 'HomeCredits.cs') (Join-Path $PSScriptRoot 'AboutCredits.cs') (Join-Path $PSScriptRoot 'DragonHome.cs') (Join-Path $PSScriptRoot 'LauncherMotion.cs') (Join-Path $PSScriptRoot 'LaunchSuccess.cs') (Join-Path $PSScriptRoot 'LauncherUpdateUi.cs')
if ($LASTEXITCODE -ne 0) { throw 'Theme compilation failed.' }
& (Join-Path $PSScriptRoot 'tools\PatchBrand.exe') $Original $themeFile (Join-Path $assets 'Cassell.png') (Join-Path $assets 'Cassell.ico') $Output
if ($LASTEXITCODE -ne 0) { throw 'Launcher build or structural verification failed.' }
Write-Output "Built: $Output"
