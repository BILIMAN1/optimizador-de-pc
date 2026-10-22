# Compila PruebasOptimizador.exe junto a una copia de la versión de pruebas de la app (en %TEMP%\opc_pruebas)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot
& (Join-Path $raiz 'build.ps1') -Prueba | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'La app no compila' }
$destino = Join-Path $env:TEMP 'opc_pruebas2'
New-Item -ItemType Directory -Force -Path $destino | Out-Null
Copy-Item (Join-Path $raiz 'dist\OptimizadorPC_prueba.exe') $destino -Force
$csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:winexe /platform:anycpu /langversion:5 /codepage:65001 `
    "/out:$destino\PruebasOptimizador.exe" "/r:$destino\OptimizadorPC_prueba.exe" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.ServiceProcess.dll /r:Microsoft.CSharp.dll `
    (Join-Path $PSScriptRoot 'PruebasOptimizador.cs')
if ($LASTEXITCODE -ne 0) { throw 'Las pruebas no compilan' }
"Listo: $destino\PruebasOptimizador.exe"
