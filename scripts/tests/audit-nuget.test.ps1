param(
    [string]$FixtureScenario,
    [string]$Phase
)

$ErrorActionPreference = 'Stop'

if ($FixtureScenario) {
    $secret = 'SYNTHETIC_AUDIT_SECRET'
    $sensitiveText = "https://user:${secret}@private.invalid/feed?token=${secret} Authorization: Bearer ${secret}"
    if ($Phase -eq 'restore') {
        [Console]::Out.WriteLine($sensitiveText)
        [Console]::Error.WriteLine($sensitiveText)
        if ($FixtureScenario -eq 'restore-failure') {
            [Console]::Error.WriteLine('NU1301')
            exit 7
        }
        if ($FixtureScenario -match '^restore-warning-(NU190[0-5])$') {
            [Console]::Error.WriteLine(('warning {0}: {1}' -f $Matches[1], $sensitiveText))
        }
        exit 0
    }

    switch ($FixtureScenario) {
        'audit-failure' {
            [Console]::Out.WriteLine($sensitiveText)
            [Console]::Error.WriteLine("NU1900 $sensitiveText")
            exit 9
        }
        'invalid-json' {
            [Console]::Out.WriteLine("{ $sensitiveText")
            exit 0
        }
        'audit-stderr' {
            [Console]::Error.WriteLine($sensitiveText)
        }
    }

    $report = @{
        version = 1
        projects = @(@{
            path = $sensitiveText
            frameworks = @(@{
                framework = $sensitiveText
                topLevelPackages = @()
                transitivePackages = @()
            })
        })
        problems = @()
    }
    if ($FixtureScenario -eq 'invalid-report') { $report.version = 2 }
    if ($FixtureScenario -eq 'problems') {
        $report.problems = @(@{ text = "NU1900 $sensitiveText" })
    }
    if ($FixtureScenario -eq 'vulnerabilities') {
        $report.projects[0].frameworks[0].transitivePackages = @(@{
            id = $sensitiveText
            vulnerabilities = @(@{ advisoryurl = $sensitiveText; severity = $sensitiveText })
        })
    }
    $report | ConvertTo-Json -Depth 10 -Compress
    exit 0
}

$auditScript = Join-Path $PSScriptRoot '../audit-nuget.ps1'
$nativePwsh = (Get-Process -Id $PID).Path
$cases = @(
    @{ Name = 'success'; Fails = $false; Expected = 'No known vulnerable packages' },
    @{ Name = 'restore-failure'; Fails = $true; Expected = 'exit code 7'; Code = 'NU1301' },
    @{ Name = 'audit-failure'; Fails = $true; Expected = 'exit code 9'; Code = 'NU1900' },
    @{ Name = 'invalid-json'; Fails = $true; Expected = 'Invalid NuGet audit JSON' },
    @{ Name = 'audit-stderr'; Fails = $true; Expected = 'Invalid NuGet audit JSON' },
    @{ Name = 'invalid-report'; Fails = $true; Expected = 'Invalid NuGet audit report' },
    @{ Name = 'problems'; Fails = $true; Expected = 'NuGet audit reported problems'; Code = 'NU1900' },
    @{ Name = 'vulnerabilities'; Fails = $true; Expected = 'found 1 vulnerability entries' }
)
$cases += 0..5 | ForEach-Object {
    $code = 'NU190{0}' -f $_
    @{ Name = 'restore-warning-' + $code; Fails = $true; Expected = 'NuGet restore reported audit warnings'; Code = $code }
}

foreach ($case in $cases) {
    $state = @{ Failed = $false }
    $records = @(& {
        param($Case, $State, $AuditScript, $FixtureScript, $NativePwsh)

        # Exercise the real audit script with native child-process stdout/stderr.
        $PSNativeCommandUseErrorActionPreference = $true
        function git {
            $global:LASTEXITCODE = 0
            'Flowery.Uno/Flowery.Uno.csproj'
        }
        function dotnet {
            $fixtureArgs = @('-NoProfile', '-File', $FixtureScript,
                '-FixtureScenario', $Case.Name, '-Phase', $args[0])
            & $NativePwsh @fixtureArgs
            $global:LASTEXITCODE = $LASTEXITCODE
        }
        try {
            & $AuditScript -Configuration Debug
        }
        catch {
            $State.Failed = $true
            Write-Output $_.Exception.ToString()
        }
    } $case $state $auditScript $PSCommandPath $nativePwsh *>&1)

    $text = ($records | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    if ($text -match 'SYNTHETIC_AUDIT_SECRET|private\.invalid|Authorization:|Bearer|https://user:') {
        throw ('Sensitive output escaped in case {0}.' -f $case.Name)
    }
    if ($state.Failed -ne $case.Fails -or !$text.Contains($case.Expected)) {
        throw ('Unexpected audit result in case {0}.' -f $case.Name)
    }
    if ($case.Code -and !$text.Contains($case.Code)) {
        throw ('Expected diagnostic code missing in case {0}.' -f $case.Name)
    }
    Write-Host ('Passed: {0}' -f $case.Name)
}
