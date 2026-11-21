param(
    [string]$Theme = (Join-Path $PSScriptRoot 'tools\InfiniteCL.Theme.dll'),
    [string]$Output = (Join-Path $PSScriptRoot 'qa-rain')
)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path $compiler
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$references = @(
    "/reference:$framework\WPF\PresentationCore.dll",
    "/reference:$framework\WPF\PresentationFramework.dll",
    "/reference:$framework\WPF\WindowsBase.dll",
    "/reference:$framework\System.Xaml.dll"
)
foreach ($name in @('VerifyPresentation', 'VerifyPointerLight', 'VerifyRainScene', 'VerifyNavigationShell', 'VerifyHomeCredits', 'VerifyAboutCredits', 'VerifyDragonHome', 'VerifyLauncherMotion', 'VerifyLaunchSuccess')) {
    $source = if ($name -eq 'VerifyDragonHome' -or $name -eq 'VerifyLauncherMotion' -or $name -eq 'VerifyLaunchSuccess') { Join-Path $PSScriptRoot ('test\' + $name + '.cs') } else { Join-Path $PSScriptRoot ($name + '.cs') }
    if (!(Test-Path -LiteralPath $source)) { throw "Missing regression harness: $source" }
    $executable = Join-Path $Output ($name + '.exe')
    & $compiler /nologo /optimize+ "/out:$executable" @references $source
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $name" }
    if ($name -eq 'VerifyPointerLight' -or $name -eq 'VerifyDragonHome' -or $name -eq 'VerifyLauncherMotion' -or $name -eq 'VerifyLaunchSuccess') { & $executable $Theme $Output }
    else { & $executable $Theme | Tee-Object -FilePath (Join-Path $Output ($name + '.txt')) }
    if ($LASTEXITCODE -ne 0) { throw "Regression failed: $name" }
}
