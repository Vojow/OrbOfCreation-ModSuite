param(
    [Parameter(Mandatory = $true)]
    [string] $SourcePath
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$dataDirectory = Join-Path $repositoryRoot 'data'
$sourceDirectory = Join-Path $dataDirectory 'source'
$mappingPath = Join-Path $dataDirectory 'entity-mappings.tsv'
$typeSummaryPath = Join-Path $dataDirectory 'entity-types.tsv'
$preservedSourcePath = Join-Path $sourceDirectory 'message.txt'
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

New-Item -ItemType Directory -Force -Path $dataDirectory, $sourceDirectory | Out-Null

$records = [System.Collections.Generic.List[object]]::new()
$lineNumber = 0

foreach ($line in [System.IO.File]::ReadAllLines($SourcePath, [System.Text.Encoding]::UTF8)) {
    $lineNumber++
    if ($line -notmatch '^([0-9a-fA-F-]{36})\s+\u2192\s+(.+?)\s+\u2192\s+(.+?)\s*$') {
        throw "Invalid mapping at line ${lineNumber}: $line"
    }

    $records.Add([pscustomobject]@{
        id = $Matches[1].ToLowerInvariant()
        name = $Matches[2].Trim()
        type = $Matches[3].Trim()
    })
}

$duplicateIds = $records | Group-Object id | Where-Object Count -gt 1
if ($duplicateIds) {
    throw "Duplicate entity IDs found: $($duplicateIds.Name -join ', ')"
}

foreach ($record in $records) {
    if ($record.name.Contains("`t") -or $record.type.Contains("`t")) {
        throw "Tabs are not allowed in TSV fields: $($record.id)"
    }
}

$mappingLines = [System.Collections.Generic.List[string]]::new()
$mappingLines.Add("id`tname`ttype")
foreach ($record in $records) {
    $mappingLines.Add("$($record.id)`t$($record.name)`t$($record.type)")
}
[System.IO.File]::WriteAllLines($mappingPath, $mappingLines, $utf8NoBom)

$summaryLines = [System.Collections.Generic.List[string]]::new()
$summaryLines.Add("type`tcount")
foreach ($group in ($records | Group-Object type | Sort-Object Name)) {
    $summaryLines.Add("$($group.Name)`t$($group.Count)")
}
[System.IO.File]::WriteAllLines($typeSummaryPath, $summaryLines, $utf8NoBom)

$sourceText = [System.IO.File]::ReadAllText($SourcePath, [System.Text.Encoding]::UTF8)
[System.IO.File]::WriteAllText($preservedSourcePath, $sourceText, $utf8NoBom)

Write-Host "Imported $($records.Count) entity mappings."
Write-Host "Mappings: $mappingPath"
Write-Host "Types:    $typeSummaryPath"
Write-Host "Source:   $preservedSourcePath"
