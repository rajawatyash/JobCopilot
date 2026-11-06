# Sends the sample CV and JD to a running JobCopilot API and saves the response.
# Start the API first (cd src/JobCopilot.Api; dotnet run), then from the repo root run:
#   ./samples/run-sample.ps1
param([string]$BaseUrl = "http://localhost:5141")

$body = @{
    # ReadAllText returns a plain string. Get-Content -Raw adds PowerShell properties
    # (PSPath etc.) that Windows PowerShell 5.1 serialises into the JSON as an object.
    cvText         = [System.IO.File]::ReadAllText("$PSScriptRoot/sample-cv.txt")
    jobDescription = [System.IO.File]::ReadAllText("$PSScriptRoot/sample-jd.txt")
} | ConvertTo-Json

$response = Invoke-RestMethod -Uri "$BaseUrl/api/match" -Method Post `
    -ContentType "application/json; charset=utf-8" -Body ([System.Text.Encoding]::UTF8.GetBytes($body))

$json = $response | ConvertTo-Json -Depth 10
$json | Set-Content "$PSScriptRoot/sample-response.json" -Encoding utf8
$json
