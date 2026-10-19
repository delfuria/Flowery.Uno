param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $projects = @(git ls-files -- '*.csproj')
    if ($LASTEXITCODE -ne 0 -or $projects.Count -eq 0) {
        throw 'No tracked C# projects found.'
    }

    $vulnerabilities = @()
    foreach ($project in $projects) {
        Write-Host ('Auditing {0} ({1})' -f $project, $Configuration)
        $restoreArgs = @('restore', $project, "-p:Configuration=$Configuration", '-p:NuGetAudit=true', '-p:NuGetAuditMode=all', '-p:CheckEolWorkloads=false')
        dotnet @restoreArgs
        if ($LASTEXITCODE -ne 0) { throw ('Restore failed for {0}.' -f $project) }

        $listArgs = @('list', $project, 'package', '--vulnerable', '--include-transitive', '--format', 'json', '--output-version', '1', '--no-restore')
        $output = dotnet @listArgs
        if ($LASTEXITCODE -ne 0) { throw ('NuGet audit failed for {0}.' -f $project) }
        $report = ($output -join [Environment]::NewLine) | ConvertFrom-Json
        if ($report.version -ne 1 -or $null -eq $report.projects -or @($report.projects).Count -ne 1) {
            throw ('Invalid NuGet audit report for {0}.' -f $project)
        }
        foreach ($entry in $report.projects) {
            foreach ($framework in $entry.frameworks) {
                foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                    foreach ($vulnerability in $package.vulnerabilities) {
                        $vulnerabilities += '{0} {1} {2}: {3} ({4})' -f $project, $framework.framework, $package.id, $vulnerability.advisoryurl, $vulnerability.severity
                    }
                }
            }
        }
        foreach ($problem in $report.problems) {
            throw ('NuGet audit could not complete: {0}' -f $problem.text)
        }
    }
    if ($vulnerabilities.Count -gt 0) {
        throw ($vulnerabilities -join [Environment]::NewLine)
    }
    Write-Host ('No known vulnerable packages in {0} projects ({1}), including transitive dependencies.' -f $projects.Count, $Configuration)
}
finally {
    Pop-Location
}
