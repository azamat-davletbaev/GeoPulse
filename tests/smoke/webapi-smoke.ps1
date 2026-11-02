<#
.SYNOPSIS
    Смок-тест REST API GeoPulse (точечные данные из PostgreSQL, этап 3).

.DESCRIPTION
    Проверяет health, список скважин, карточку скважины, страницу суточной добычи
    (пагинация, фильтр по датам, сортировка), выборку за конкретную дату и коды ошибок 400/404.
    Требует заполненной БД: сначала пройдите смок-тест пайплайна из README (Kafka -> Consumer -> PostgreSQL).

.EXAMPLE
    powershell -File tests/smoke/webapi-smoke.ps1 -BaseUrl http://localhost:5146
#>
param(
    [string]$BaseUrl = 'http://localhost:5146'
)

$ErrorActionPreference = 'Stop'
$script:failures = 0
$script:checks = 0

function Invoke-Api {
    param([string]$Path)

    try {
        $response = Invoke-WebRequest -Uri ($BaseUrl + $Path) -UseBasicParsing
        return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $response.Content }
    }
    catch {
        $response = $_.Exception.Response
        if ($null -eq $response) {
            throw
        }

        $reader = New-Object System.IO.StreamReader($response.GetResponseStream())
        return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $reader.ReadToEnd() }
    }
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)

    $script:checks++
    if ($Expected -ne $Actual) {
        $script:failures++
        Write-Host "[FAIL] $Message (ожидалось: $Expected, получено: $Actual)" -ForegroundColor Red
    }
    else {
        Write-Host "[ OK ] $Message" -ForegroundColor Green
    }
}

function Assert-True {
    param($Condition, [string]$Message)

    $script:checks++
    if (-not [bool]$Condition) {
        $script:failures++
        Write-Host "[FAIL] $Message" -ForegroundColor Red
    }
    else {
        Write-Host "[ OK ] $Message" -ForegroundColor Green
    }
}

Write-Host "Смок-тест API: $BaseUrl" -ForegroundColor Cyan

# 1. Health
$health = Invoke-Api '/health'
Assert-Equal 200 $health.Status 'GET /health -> 200'
$healthBody = $health.Body | ConvertFrom-Json
Assert-Equal 'ok' $healthBody.status 'health.status = ok'
Assert-Equal 'up' $healthBody.database 'health.database = up'

# 2. Список скважин
$wellsResponse = Invoke-Api '/api/wells'
Assert-Equal 200 $wellsResponse.Status 'GET /api/wells -> 200'
# В PowerShell 5.1 ConvertFrom-Json отдаёт массив одним объектом, поэтому сначала присваивание, затем @().
$wellsJson = $wellsResponse.Body | ConvertFrom-Json
$wells = @($wellsJson)
Assert-True ($wells.Count -gt 0) "GET /api/wells вернул скважины (count=$($wells.Count))"

$propertyNames = @($wells[0].PSObject.Properties.Name)
Assert-True ($propertyNames -contains 'npdWellBoreCode') 'в ответе есть натуральный ключ npdWellBoreCode'
Assert-True (-not ($propertyNames -contains 'id')) 'внутренние id из БД в ответе не публикуются'

# 3. Карточка скважины
$code = $wells[0].npdWellBoreCode
$detailsResponse = Invoke-Api "/api/wells/$code"
Assert-Equal 200 $detailsResponse.Status "GET /api/wells/$code -> 200"
$details = $detailsResponse.Body | ConvertFrom-Json
Assert-Equal $code $details.npdWellBoreCode 'карточка относится к запрошенной скважине'
Assert-True ($details.productionDays -gt 0) "productionDays > 0 (=$($details.productionDays))"
Assert-True ($null -ne $details.firstProductionDate -and $null -ne $details.lastProductionDate) 'в карточке есть границы периода добычи'

# 4. Страница добычи (по умолчанию — сначала новые даты)
$pageResponse = Invoke-Api "/api/wells/$code/production?pageSize=3"
Assert-Equal 200 $pageResponse.Status 'GET /api/wells/{code}/production?pageSize=3 -> 200'
$page = $pageResponse.Body | ConvertFrom-Json
$pageItems = @($page.items)
Assert-Equal 3 $page.pageSize 'pageSize = 3'
Assert-Equal 3 $pageItems.Count 'в странице 3 записи'
Assert-True ($page.totalCount -ge 3) "totalCount >= 3 (=$($page.totalCount))"
Assert-True ($page.hasNextPage) 'hasNextPage = true'
Assert-True (-not $page.hasPreviousPage) 'hasPreviousPage = false'
Assert-True ([string]::CompareOrdinal($pageItems[0].date, $pageItems[1].date) -gt 0) 'по умолчанию сортировка по убыванию даты'

# 5. Фильтр по диапазону дат и сортировка по возрастанию
$filteredResponse = Invoke-Api "/api/wells/$code/production?from=2016-01-01&to=2016-12-31&pageSize=2&sort=asc"
Assert-Equal 200 $filteredResponse.Status 'GET .../production?from=2016-01-01&to=2016-12-31&sort=asc -> 200'
$filtered = $filteredResponse.Body | ConvertFrom-Json
$filteredItems = @($filtered.items)
Assert-True ($filtered.totalCount -gt 0 -and $filtered.totalCount -le $page.totalCount) "фильтр по датам сузил выборку (totalCount=$($filtered.totalCount))"
Assert-True ($filteredItems.Count -gt 0) 'фильтр вернул записи'
$outOfRange = @($filteredItems | Where-Object {
        [string]::CompareOrdinal($_.date, '2016-01-01') -lt 0 -or [string]::CompareOrdinal($_.date, '2016-12-31') -gt 0
    })
Assert-Equal 0 $outOfRange.Count 'все записи фильтра попадают в диапазон дат'
Assert-True ([string]::CompareOrdinal($filteredItems[0].date, $filteredItems[1].date) -lt 0) 'sort=asc сортирует по возрастанию даты'

# 6. Запись за конкретную дату
$date = $pageItems[0].date
$byDateResponse = Invoke-Api "/api/wells/$code/production/$date"
Assert-Equal 200 $byDateResponse.Status "GET /api/wells/$code/production/$date -> 200"
$byDate = $byDateResponse.Body | ConvertFrom-Json
Assert-Equal $date $byDate.date 'запись соответствует запрошенной дате'

# 7. Ошибки: 404
$unknownWell = Invoke-Api '/api/wells/1'
Assert-Equal 404 $unknownWell.Status 'GET /api/wells/1 (нет скважины) -> 404'
Assert-True ($unknownWell.Body -match '"status":404') '404 отдан как ProblemDetails'

$missingRecord = Invoke-Api "/api/wells/$code/production/1999-01-01"
Assert-Equal 404 $missingRecord.Status 'GET .../production/1999-01-01 (нет записи) -> 404'

# 8. Ошибки: 400 (валидация параметров)
$rangeError = Invoke-Api "/api/wells/$code/production?from=2016-01-01&to=2007-01-01"
Assert-Equal 400 $rangeError.Status 'from > to -> 400'

$pageSizeError = Invoke-Api "/api/wells/$code/production?pageSize=0"
Assert-Equal 400 $pageSizeError.Status 'pageSize=0 -> 400'

$sortError = Invoke-Api "/api/wells/$code/production?sort=sideways"
Assert-Equal 400 $sortError.Status 'sort=sideways -> 400'

$pageError = Invoke-Api "/api/wells/$code/production?page=0"
Assert-Equal 400 $pageError.Status 'page=0 -> 400'

$dateError = Invoke-Api "/api/wells/$code/production/not-a-date"
Assert-Equal 400 $dateError.Status 'production/not-a-date -> 400'

# 9. Swagger
$swagger = Invoke-Api '/swagger/v1/swagger.json'
Assert-Equal 200 $swagger.Status 'GET /swagger/v1/swagger.json -> 200'

Write-Host ''
if ($script:failures -gt 0) {
    Write-Host "Провалено проверок: $($script:failures) из $($script:checks)" -ForegroundColor Red
    exit 1
}

Write-Host "Все проверки пройдены: $($script:checks)" -ForegroundColor Green
exit 0
