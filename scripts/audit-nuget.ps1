param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function Write-AuditDiagnosticCodes {
    param([object[]]$CommandOutput)

    # Never print tool output: URLs and server responses can contain credentials.
    $text = $CommandOutput -join [Environment]::NewLine
    $knownCodes = @(
        'NU1101', 'NU1102', 'NU1301', 'NU1302',
        'NU1900', 'NU1901', 'NU1902', 'NU1903', 'NU1904', 'NU1905',
        'NETSDK1004', 'NETSDK1045', 'NETSDK1147'
    )
    foreach ($code in $knownCodes) {
        $pattern = '\b' + $code + '\b'
        if ($text -match $pattern) { Write-Host ('NuGet diagnostic code: {0}' -f $code) }
    }
    Write-Host 'Raw command output is withheld because it can contain sensitive data.'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $projects = @(git ls-files -- '*.csproj')
    if ($LASTEXITCODE -ne 0 -or $projects.Count -eq 0) {
        throw 'No tracked C# projects found.'
    }

    foreach ($project in $projects) {
        Write-Host ('Auditing {0} ({1})' -f $project, $Configuration)
        $restoreArgs = @('restore', $project, "-p:Configuration=$Configuration", '-p:AuditPipeline=true', '-p:CheckEolWorkloads=false')
        try {
            $restoreOutput = @(dotnet @restoreArgs 2>&1)
            $restoreExitCode = $LASTEXITCODE
        }
        catch {
            throw 'Could not run NuGet restore. Raw exception details are withheld.'
        }
        if ($restoreExitCode -ne 0) {
            Write-AuditDiagnosticCodes -CommandOutput $restoreOutput
            throw ('Restore failed for {0} (exit code {1}).' -f $project, $restoreExitCode)
        }
    }
    Write-Host ('No known vulnerable packages in {0} projects ({1}), including transitive dependencies.' -f $projects.Count, $Configuration)
}
finally {
    Pop-Location
}
