param([string]$OutputPath, [switch]$Test)
$ErrorActionPreference = 'Stop'
$g70Compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $g70Compiler)) { throw 'Windows .NET Framework compiler is required.' }
$g70Output = if ($OutputPath) { [IO.Path]::GetFullPath($OutputPath) } elseif ($Test) { Join-Path $PSScriptRoot 'qa/G70.Tests.exe' } else { Join-Path (Split-Path $PSScriptRoot -Parent) 'G70.exe' }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($g70Output)) | Out-Null
$g70Target = if ($Test) { '/target:exe' } else { '/target:winexe' }
$g70Arguments = @('/nologo', $g70Target, '/platform:anycpu', '/optimize+', '/warnaserror+', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', ('/out:' + $g70Output))
foreach ($g70Asset in @('car', 'dial', 'front', 'rear', 'far', 'hub', 'led', 'flutter', 'faces', 'warning-faces')) {
    $g70AssetPath = Join-Path $PSScriptRoot ('assets/' + $g70Asset + '.png')
    if (-not (Test-Path -LiteralPath $g70AssetPath)) { throw ('Missing asset: ' + $g70Asset) }
    $g70Arguments += '/resource:' + $g70AssetPath + ',' + $g70Asset + '.png'
}
$g70Arguments += Join-Path $PSScriptRoot 'Desktop.cs'
if ($Test) {
    $g70Arguments += '/define:TESTS', '/main:TestProgram'
    $g70Arguments += Join-Path $PSScriptRoot 'tests/Checks.cs'
    $g70Arguments += '/resource:' + (Join-Path $PSScriptRoot 'tests/reference.csv') + ',reference.csv'
}
& $g70Compiler @g70Arguments
if ($LASTEXITCODE -ne 0) { throw 'G70 build failed.' }
Write-Output $g70Output
