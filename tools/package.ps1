# Monta dist/Montarias.zip, o asset do release que o launcher e o deploy-mod.sh usam.
#
# Layout do zip = conteudo da pasta de plugin:
#   ValheimMontarias.dll
#   Assets/*.bin|*.skin|*.png      (malhas e texturas das montarias)
#   Assets/Shop/*.png              (imagens da loja)
#
# Os _mesh.npz/_bones.npz de Assets sao intermediarios da conversao do modelo e o
# mod nao le em runtime; ficam fora (sao ~30 MB).
#
# Uso: powershell -File tools/package.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

dotnet build "$root\ValheimMontarias.csproj" -c Release -nologo -v q "-p:BepInExPlugins=$root\obj\noplugins"
if ($LASTEXITCODE -ne 0) { throw "build falhou" }

$stage = Join-Path $root 'obj\package'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force "$stage\Assets\Shop" | Out-Null

Copy-Item "$root\bin\Release\ValheimMontarias.dll" $stage
Get-ChildItem "$root\Assets" -File | Where-Object { $_.Extension -in '.bin', '.skin', '.png' } |
    Copy-Item -Destination "$stage\Assets"
Get-ChildItem "$root\Assets" -Directory | Where-Object { $_.Name -ieq 'shop' } |
    ForEach-Object { Get-ChildItem $_.FullName -File -Filter *.png } |
    Copy-Item -Destination "$stage\Assets\Shop"

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist 'Montarias.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
# Entrada a entrada, com '/': o CreateFromDirectory do .NET Framework (PowerShell
# 5.1) grava 'Assets\x.png', que o unzip do deploy-mod.sh extrai como nome de
# arquivo com barra invertida em vez de pasta.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$arquivo = [IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    Get-ChildItem $stage -Recurse -File | ForEach-Object {
        $nome = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($arquivo, $_.FullName, $nome)
    }
} finally { $arquivo.Dispose() }

$n = (Get-ChildItem $stage -Recurse -File).Count
Write-Host "$zip : $n arquivos, $([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB"
