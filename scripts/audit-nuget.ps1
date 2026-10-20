param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function Write-AuditDiagnosticCodes {
    param([object[]]$CommandOutput)

    # Never print tool output: URLs, server responses and parser errors can contain credentials.
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

    $vulnerabilityCount = 0
    foreach ($project in $projects) {
        Write-Host ('Auditing {0} ({1})' -f $project, $Configuration)
        $restoreArgs = @('restore', $project, "-p:Configuration=$Configuration", '-p:NuGetAudit=true', '-p:NuGetAuditMode=all', '-p:CheckEolWorkloads=false')
        $restoreOutput = @(dotnet @restoreArgs 2>&1)
        $restoreExitCode = $LASTEXITCODE
        if ($restoreExitCode -ne 0) {
            Write-AuditDiagnosticCodes -CommandOutput $restoreOutput
            throw ('Restore failed for {0} (exit code {1}).' -f $project, $restoreExitCode)
        }
        # Restore can return zero after an audit-data failure or vulnerability warning.
        if (($restoreOutput -join [Environment]::NewLine) -match '\bNU190[0-5]\b') {
            Write-AuditDiagnosticCodes -CommandOutput $restoreOutput
            throw ('NuGet restore reported audit warnings for {0}.' -f $project)
        }

        $listArgs = @('list', $project, 'package', '--vulnerable', '--include-transitive', '--format', 'json', '--output-version', '1', '--no-restore')
        $output = @(dotnet @listArgs 2>&1)
        $auditExitCode = $LASTEXITCODE
        if ($auditExitCode -ne 0) {
            Write-AuditDiagnosticCodes -CommandOutput $output
            throw ('NuGet audit failed for {0} (exit code {1}).' -f $project, $auditExitCode)
        }
        try {
            $report = ($output -join [Environment]::NewLine) | ConvertFrom-Json
        }
        catch {
            Write-AuditDiagnosticCodes -CommandOutput $output
            throw ('Invalid NuGet audit JSON for {0}. Raw parser diagnostics are withheld.' -f $project)
        }
        if ($report.version -ne 1 -or $null -eq $report.projects -or @($report.projects).Count -ne 1) {
            throw ('Invalid NuGet audit report for {0}.' -f $project)
        }
        foreach ($entry in $report.projects) {
            foreach ($framework in $entry.frameworks) {
                foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                    foreach ($vulnerability in $package.vulnerabilities) {
                        $vulnerabilityCount++
                    }
                }
            }
        }
        if (@($report.problems).Where({ $null -ne $_ }).Count -gt 0) {
            Write-AuditDiagnosticCodes -CommandOutput $output
            throw ('NuGet audit reported problems for {0}. Raw problem details are withheld.' -f $project)
        }
    }
    if ($vulnerabilityCount -gt 0) {
        throw ('NuGet audit found {0} vulnerability entries. Package metadata and advisory URLs are withheld.' -f $vulnerabilityCount)
    }
    Write-Host ('No known vulnerable packages in {0} projects ({1}), including transitive dependencies.' -f $projects.Count, $Configuration)
}
finally {
    Pop-Location
}
