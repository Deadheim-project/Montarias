<#
    Teste de ponta a ponta do Montarias com o Mestre das Montarias do NpcValheim, sem
    ninguem clicar em nada.

    Sobe um servidor dedicado local e um cliente real do Valheim, cada um com a propria
    arvore BepInEx (via argumentos do Doorstop, como o launcher faz): Deadheim e NpcValheim
    do perfil Default do Deadheim Launcher (o que os jogadores usam) e o Montarias deste
    repositorio (obj\package, montado por tools\package.ps1). O cliente leva tambem o
    MontariasTestDriver, que cria o personagem, conecta e segue o roteiro de Driver.cs:
    API, painel do Mestre, compra de nivel (Coins) e da Capivara (Deadcoins), montar e
    desmontar pelo item e o bloqueio em combate. O resultado sai como linhas [MONTTEST] no
    log do cliente e as fotos em <Root>\sync\fotos.

    Nada e instalado nas pastas do jogo. As preferencias do Valheim (registro) sao salvas
    antes e restauradas no fim.

    Uso:
      powershell -ExecutionPolicy Bypass -File tools\package.ps1
      powershell -ExecutionPolicy Bypass -File tools\e2e\run-e2e.ps1
#>
param(
    [string]$Root = 'D:\tmp\monttest',
    [int]$Port = 2486,
    [string]$Password = 'monttest1',
    [string]$ServerDir = 'D:\ValheimDedicatedServer',
    [string]$ClientDir = '',
    [string]$Profile = (Join-Path $env:APPDATA 'DeadheimLauncher\profiles\Default\game\BepInEx'),
    [int]$TimeoutMinutes = 15,
    [switch]$KeepRunning
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$montarias = Join-Path $repo 'obj\package'
$driverProj = Join-Path $PSScriptRoot 'MontariasTestDriver\MontariasTestDriver.csproj'
$driverDll = Join-Path $PSScriptRoot 'MontariasTestDriver\bin\Release\MontariasTestDriver.dll'
$character = 'Montador'

function Write-Step($text) { Write-Host ("[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $text) }

if (-not $ClientDir) {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    $libs = @()
    if ($steam) {
        $libs += $steam -replace '/', '\'
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content -Raw $vdf), '"path"\s+"([^"]+)"')) { $libs += $m.Groups[1].Value -replace '\\\\', '\' }
        }
    }
    $ClientDir = $libs | ForEach-Object { Join-Path $_ 'steamapps\common\Valheim' } |
        Where-Object { Test-Path (Join-Path $_ 'valheim.exe') } | Select-Object -First 1
    if (-not $ClientDir) { throw 'Nao achei o Valheim nas bibliotecas da Steam. Passe -ClientDir.' }
}

foreach ($f in @("$montarias\ValheimMontarias.dll", "$Profile\core\BepInEx.Preloader.dll", "$Profile\plugins\deadheim\Deadheim.dll",
                 "$Profile\plugins\npcs\NpcValheim.dll", "$ServerDir\valheim_server.exe", "$ClientDir\valheim.exe")) {
    if (-not (Test-Path $f)) { throw "Nao encontrei $f (rode tools\package.ps1 antes?)" }
}

Write-Step 'Compilando o MontariasTestDriver'
& dotnet build $driverProj -c Release -nologo -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'O MontariasTestDriver nao compilou.' }

$activeUser = (Get-ItemProperty 'HKCU:\Software\Valve\Steam\ActiveProcess' -ErrorAction SilentlyContinue).ActiveUser
if (-not $activeUser) { throw 'Nao achei a conta do Steam (Steam aberto?).' }
$steamId = ([UInt64]76561197960265728 + [UInt64]$activeUser).ToString()

function New-Tree([string]$dir, [switch]$Driver) {
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    New-Item -ItemType Directory -Force "$dir\BepInEx\plugins", "$dir\BepInEx\config" | Out-Null
    Copy-Item -Recurse "$Profile\core" "$dir\BepInEx\core"
    Copy-Item -Recurse "$Profile\plugins\deadheim" "$dir\BepInEx\plugins\deadheim"
    Copy-Item -Recurse "$Profile\plugins\npcs" "$dir\BepInEx\plugins\npcs"
    # O NpcValheim depende do VipList.
    Copy-Item -Recurse "$Profile\plugins\viplist" "$dir\BepInEx\plugins\viplist"
    Copy-Item -Recurse $montarias "$dir\BepInEx\plugins\ValheimMontarias"
    if ($Driver) { Copy-Item $driverDll "$dir\BepInEx\plugins\" }
}

$processes = @()
function Stop-All {
    foreach ($p in $script:processes) { try { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } } catch { } }
    $script:processes = @()
}

function Wait-Log([string]$path, [string]$pattern, [int]$seconds) {
    $until = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $until) {
        if (Test-Path $path) {
            $hit = Select-String -Path $path -Pattern $pattern -ErrorAction SilentlyContinue | Select-Object -Last 1
            if ($hit) { return $hit.Line }
        }
        Start-Sleep -Seconds 2
    }
    return $null
}

$prefsBackup = "$Root\valheim-prefs.reg"
New-Item -ItemType Directory -Force $Root | Out-Null
& reg export "HKCU\Software\IronGate\valheim" $prefsBackup /y | Out-Null

try {
    Write-Step "Montando arvores BepInEx em $Root"
    New-Tree "$Root\server"
    New-Tree "$Root\client" -Driver
    foreach ($d in @("$Root\sync", "$Root\chars", "$Root\saves-server")) {
        if (Test-Path $d) { Remove-Item -Recurse -Force $d }
        New-Item -ItemType Directory -Force $d | Out-Null
    }
    # 1000 Deadcoins para o personagem de teste (a Capivara custa 300).
    New-Item -ItemType Directory -Force "$Root\server\BepInEx\config\DonationShop" | Out-Null
    Set-Content -Path "$Root\server\BepInEx\config\DonationShop\$character-Steam_$steamId.json" -Value '1000' -Encoding ASCII -NoNewline

    $serverLog = "$Root\server-unity.log"
    $env:SteamAppId = '892970'
    $argLine = "--doorstop-enabled true --doorstop-target-assembly `"$Root\server\BepInEx\core\BepInEx.Preloader.dll`" " +
               "-nographics -batchmode -name DeadheimMontTest -port $Port -world MontTest -password $Password -public 0 " +
               "-savedir `"$Root\saves-server`" -logFile `"$serverLog`""
    Write-Step "Servidor na porta $Port"
    $script:processes += Start-Process -FilePath "$ServerDir\valheim_server.exe" -ArgumentList $argLine -WorkingDirectory $ServerDir -WindowStyle Hidden -PassThru
    if (-not (Wait-Log $serverLog 'Opened Steam server' 900)) { throw "O servidor nao abriu conexoes. Veja $serverLog" }
    Start-Sleep -Seconds 5

    $argLine = "--doorstop-enabled true --doorstop-target-assembly `"$Root\client\BepInEx\core\BepInEx.Preloader.dll`" " +
               "+connect 127.0.0.1:$Port -password $Password " +
               "-monttest-sync `"$Root\sync`" -monttest-save `"$Root\chars`" " +
               "-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile `"$Root\client-unity.log`""
    Write-Step 'Cliente'
    $client = Start-Process -FilePath "$ClientDir\valheim.exe" -ArgumentList $argLine -WorkingDirectory $ClientDir -PassThru
    $script:processes += $client

    $until = (Get-Date).AddMinutes($TimeoutMinutes)
    while (-not (Test-Path "$Root\sync\result.txt")) {
        if ($client.HasExited) { Write-Step "O cliente caiu (codigo $($client.ExitCode))"; break }
        if ((Get-Date) -gt $until) { Write-Step "Tempo esgotado ($TimeoutMinutes min)"; break }
        Start-Sleep -Seconds 5
    }

    Write-Host ''
    Write-Host '===== Cliente ====='
    $log = "$Root\client\BepInEx\LogOutput.log"
    if (Test-Path $log) { Select-String -Path $log -Pattern '\[MONTTEST\]' | ForEach-Object { $_.Line -replace '^.*\[MONTTEST\] ', '' } }
    Write-Host ''
    Write-Host '===== Servidor ====='
    Select-String -Path $serverLog -Pattern 'ValheimMontarias|Montarias|mount|Exception' | ForEach-Object { $_.Line } | Select-Object -Last 40
    Write-Host ''
    Write-Host "Deadcoins do $character no fim: $(Get-Content "$Root\server\BepInEx\config\DonationShop\$character-Steam_$steamId.json")"
    if (Test-Path "$Root\sync\fotos") { Write-Host "Fotos em $Root\sync\fotos" }
}
finally {
    if (-not $KeepRunning) { Stop-All }
    if (Test-Path $prefsBackup) {
        & reg delete "HKCU\Software\IronGate\valheim" /f | Out-Null
        & cmd /c "reg import `"$prefsBackup`" >nul 2>&1"
        Write-Step 'Preferencias do Valheim restauradas'
    }
}

$result = Get-Content "$Root\sync\result.txt" -ErrorAction SilentlyContinue
if ($result -match 'fail=0$') { exit 0 }
exit 1
