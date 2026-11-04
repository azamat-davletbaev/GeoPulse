<#
.SYNOPSIS
    Проверка фронтенда GeoPulse (web/app.js) без браузера.

.DESCRIPTION
    Прогоняет сценарии tests/frontend/webapp-check.js через Node в Docker-контейнере,
    поэтому Node.js на хосте не требуется. Стенд подменяет Leaflet, DOM и fetch
    заглушками и проверяет: слои строятся только по валидным данным, битая скважина
    изолирована своим guard'ом, первичный вид учитывает траектории, карта остаётся живой.

    Проверка отдельная от dotnet test (это не xUnit-тест и не csproj): запускается
    вручную или отдельным шагом CI.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tests/frontend/run-webapp-check.ps1
#>
param(
    [string]$NodeImage = 'node:20-alpine'
)

$ErrorActionPreference = 'Stop'

# Корень репозитория: tests/frontend -> tests -> корень.
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$scenarios = @('mixed', 'empty', 'select', 'autoselect')
$failed = 0

foreach ($scenario in $scenarios) {
    Write-Host "== web/app.js: сценарий $scenario =="
    docker run --rm -e "SCENARIO=$scenario" -v "${repoRoot}:/repo:ro" $NodeImage node /repo/tests/frontend/webapp-check.js $scenario
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[FAIL] сценарий $scenario"
        $failed++
    }
}

if ($failed -gt 0) {
    Write-Host "Провалено сценариев: $failed из $($scenarios.Count)"
    exit 1
}

Write-Host "Все проверки фронтенда пройдены: $($scenarios.Count) из $($scenarios.Count)"
