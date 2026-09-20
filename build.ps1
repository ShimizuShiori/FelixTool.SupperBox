[CmdletBinding()]
param(
    [string]$PublishDirectory = 'C:\Tools\FelixTool.SupperBox'
)

$ErrorActionPreference = 'Stop'

$projectDirectory = $PSScriptRoot
$projectPath = Join-Path $projectDirectory 'src\FelixTool.SupperBox\FelixTool.SupperBox.csproj'
$executablePath = Join-Path $PublishDirectory 'FelixTool.SupperBox.exe'
$runKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'FelixTool.SupperBox'

Write-Host 'Stopping running SupperBox instances...'
$runningProcesses = Get-Process -Name 'FelixTool.SupperBox' -ErrorAction SilentlyContinue
foreach ($process in $runningProcesses) {
    Stop-Process -Id $process.Id -Force
    Wait-Process -Id $process.Id -ErrorAction SilentlyContinue
}

try {
    New-Item -ItemType Directory -Path $PublishDirectory -Force | Out-Null

    Write-Host "Publishing SupperBox to $PublishDirectory..."
    & dotnet publish $projectPath `
        --configuration Release `
        --output $PublishDirectory `
        --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $runCommand = '"' + $executablePath + '"'
    New-Item -Path $runKeyPath -Force | Out-Null
    New-ItemProperty `
        -Path $runKeyPath `
        -Name $runValueName `
        -Value $runCommand `
        -PropertyType String `
        -Force | Out-Null
    Write-Host "Configured current-user startup: $runValueName"
}
finally {
    if (Test-Path $executablePath) {
        Write-Host 'Starting SupperBox...'
        Start-Process -FilePath $executablePath
    }
}

Write-Host 'SupperBox build and deployment completed.'
