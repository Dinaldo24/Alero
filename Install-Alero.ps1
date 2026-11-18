<#
  Instala Alero como una aplicación normal, sin permisos de administrador:
    - copia los archivos a %LocalAppData%\Programs\Alero
    - crea accesos directos en el menú Inicio y en el escritorio
    - lo registra en "Aplicaciones instaladas" (para desinstalarlo desde Configuración)
    - lo pone en el arranque de Windows
  Para quitarlo todo:  .\Install-Alero.ps1 -Uninstall   (o desde Configuración > Aplicaciones)
#>
param([switch]$Uninstall, [switch]$NoStart)

$ErrorActionPreference = 'Stop'
$name    = 'Alero'
$dest    = Join-Path $env:LOCALAPPDATA 'Programs\Alero'
$exe     = Join-Path $dest 'Alero.exe'
$startLnk   = Join-Path ([Environment]::GetFolderPath('Programs')) 'Alero.lnk'
$desktopLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Alero.lnk'
$runKey  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$uninKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Alero'

function Stop-Alero { Get-Process Alero -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false; Start-Sleep -Milliseconds 600 }

if ($Uninstall) {
    Stop-Alero
    Remove-ItemProperty -Path $runKey -Name $name -ErrorAction SilentlyContinue
    Remove-Item $startLnk, $desktopLnk -Force -Confirm:$false -ErrorAction SilentlyContinue
    Remove-Item $uninKey -Recurse -Force -Confirm:$false -ErrorAction SilentlyContinue
    if (Test-Path $dest) {
        # El script puede estar corriendo desde esa carpeta: se borra en segundo plano.
        Start-Process powershell -WindowStyle Hidden -ArgumentList '-NoProfile','-Command',"Start-Sleep 2; Remove-Item -LiteralPath '$dest' -Recurse -Force -Confirm:`$false"
    }
    "Alero desinstalado. Tus ajustes siguen en %AppData%\Alero (bórralos si quieres)."
    return
}

$src = Join-Path $PSScriptRoot 'publish'
if (-not (Test-Path (Join-Path $src 'Alero.exe'))) { throw "No encuentro $src\Alero.exe. Ejecuta primero: dotnet publish" }

Stop-Alero
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item -Path (Join-Path $src '*') -Destination $dest -Recurse -Force
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $dest 'Install-Alero.ps1') -Force

$shell = New-Object -ComObject WScript.Shell
foreach ($lnk in $startLnk, $desktopLnk) {
    $s = $shell.CreateShortcut($lnk)
    $s.TargetPath = $exe
    $s.WorkingDirectory = $dest
    $s.IconLocation = "$exe,0"
    $s.Description = 'Alero: reproductor, borrar, volumen y cronómetro en el borde de la pantalla'
    $s.Save()
}

# Arranque con Windows (el mismo valor que usa la opción "Iniciar con Windows" de la bandeja).
New-ItemProperty -Path $runKey -Name $name -Value "`"$exe`"" -PropertyType String -Force | Out-Null

# Aparece en Configuración > Aplicaciones > Aplicaciones instaladas.
New-Item -Path $uninKey -Force | Out-Null
$size = [int]((Get-ChildItem $dest -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
$props = @{
    DisplayName     = 'Alero'
    DisplayVersion  = '0.3.1'
    Publisher       = 'Alero'
    DisplayIcon     = $exe
    InstallLocation = $dest
    UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$dest\Install-Alero.ps1`" -Uninstall"
    EstimatedSize   = $size
}
foreach ($k in $props.Keys) {
    $type = if ($props[$k] -is [int]) { 'DWord' } else { 'String' }
    New-ItemProperty -Path $uninKey -Name $k -Value $props[$k] -PropertyType $type -Force | Out-Null
}
New-ItemProperty -Path $uninKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null

if (-not $NoStart) { Start-Process -FilePath $exe -WorkingDirectory $dest }
"Alero instalado en $dest"

