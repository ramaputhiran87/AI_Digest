param(
    [string]$ApiBaseUrl = "http://localhost:5050",
    [string]$DocumentPath = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($DocumentPath)) {
    $DocumentPath = Join-Path (Split-Path $PSScriptRoot -Parent) "docs\sample-architecture.md"
}

if (-not (Test-Path -LiteralPath $DocumentPath -PathType Leaf)) {
    throw "Sample document not found: $DocumentPath"
}

$document = Get-Content -LiteralPath $DocumentPath -Raw
$body = @{
    title = [IO.Path]::GetFileName($DocumentPath)
    text = $document
    useCase = "Assess security, availability, compliance evidence, and operational impact."
} | ConvertTo-Json -Depth 5

$response = Invoke-RestMethod `
    -Method Post `
    -Uri "$($ApiBaseUrl.TrimEnd('/'))/api/documents/analyse-text" `
    -ContentType "application/json" `
    -Body $body

if ($null -eq $response.modelInfo -or $response.modelInfo.inferenceCalls -lt 1) {
    throw "The API response did not confirm a real AI provider inference call."
}

Write-Output "Provider: $($response.modelInfo.provider)"
Write-Output "Model: $($response.modelInfo.modelName)"
Write-Output "Model version: $($response.modelInfo.modelVersion)"
Write-Output "Inference calls: $($response.modelInfo.inferenceCalls)"
Write-Output "Finding count: $($response.aiReport.findings.Count)"
Write-Output "Evidence checked: $($response.modelInfo.findingOrigin)"
Write-Output ""
$response | ConvertTo-Json -Depth 20
