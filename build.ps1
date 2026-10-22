<#
.SYNOPSIS
    Compila el Optimizador de PC con el compilador de C# que trae Windows (.NET Framework 4.8).
    No hace falta instalar nada.

.PARAMETER Prueba
    Compila la versión de pruebas (no pide administrador) en dist\OptimizadorPC_prueba.exe.

.PARAMETER Solo
    Compila usando solo las páginas reales indicadas (p. ej. "PaginaLimpieza,PaginaResumen") y las
    páginas provisionales de 'stubs' para el resto. Sirve para comprobar páginas aunque las demás estén a medias.

.PARAMETER Salida
    Ruta del .exe de salida (opcional).
#>
param(
    [switch]$Prueba,
    [string]$Solo,
    [string]$Salida
)

$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot
$csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'No se encontró csc.exe de .NET Framework 4.' }

$soloLista = @($Solo -split '[,;\s]+' | Where-Object { $_ })
$fuentes = @(Get-ChildItem (Join-Path $raiz 'src\*.cs') | ForEach-Object FullName)
foreach ($p in Get-ChildItem (Join-Path $raiz 'src\Paginas\*.cs')) {
    if ($soloLista.Count -gt 0 -and $soloLista -notcontains $p.BaseName) {
        $stub = Join-Path $raiz "stubs\$($p.BaseName).cs"
        if (Test-Path $stub) { $fuentes += $stub; continue }
    }
    $fuentes += $p.FullName
}

$manifiesto = Join-Path $raiz $(if ($Prueba) { 'recursos\app_prueba.manifest' } else { 'recursos\app.manifest' })
$icono = Join-Path $raiz 'recursos\icono.ico'
if (-not $Salida) {
    $Salida = Join-Path $raiz $(if ($Prueba) { 'dist\OptimizadorPC_prueba.exe' } else { 'dist\OptimizadorPC.exe' })
}
New-Item -ItemType Directory -Force -Path (Split-Path $Salida) | Out-Null

$argumentos = @(
    '/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', '/langversion:5', '/codepage:65001',
    '/warn:4', '/nowarn:1591',
    "/out:$Salida", "/win32manifest:$manifiesto",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
    '/r:System.ServiceProcess.dll', '/r:Microsoft.VisualBasic.dll', '/r:Microsoft.CSharp.dll'
)
if (Test-Path $icono) { $argumentos += "/win32icon:$icono" }
$argumentos += $fuentes

& $csc @argumentos
if ($LASTEXITCODE -ne 0) {
    Write-Host "`nERROR: la compilación falló." -ForegroundColor Red
    exit 1
}
Write-Host "`nCompilado: $Salida" -ForegroundColor Green
