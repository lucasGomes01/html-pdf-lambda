# Gera lambda.zip pronto para subir na AWS (runtime Node.js 22.x).
# Uso: powershell -ExecutionPolicy Bypass -File .\empacotar.ps1
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

# Só dependências de produção: o @aws-sdk já vem no runtime da Lambda.
npm ci --omit=dev --no-fund --no-audit
if ($LASTEXITCODE -ne 0) { throw "npm ci falhou." }

if (Test-Path lambda.zip) { Remove-Item lambda.zip }

# tar.exe (nativo do Windows) grava caminhos com "/"; o Compress-Archive do
# PowerShell 5 grava com "\" e a Lambda (Linux) não encontra os arquivos.
tar.exe -a -c -f lambda.zip index.mjs package.json node_modules
if ($LASTEXITCODE -ne 0) { throw "Falha ao gerar lambda.zip." }

$mb = [math]::Round((Get-Item lambda.zip).Length / 1MB, 1)
Write-Host "lambda.zip gerado ($mb MB)."

# Volta a instalar as devDependencies para desenvolvimento local.
npm install --no-fund --no-audit | Out-Null
