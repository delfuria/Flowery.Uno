param(
    [string]$FixtureScenario
)

$ErrorActionPreference = 'Stop'

if ($FixtureScenario) {
    $secret = 'SYNTHETIC_AUDIT_SECRET'
    $sensitiveText = "https://user:${secret}@private.invalid/feed?token=${secret} Authorization: Bearer ${secret}"
    [Console]::Out.WriteLine($sensitiveText)
    [Console]::Error.WriteLine($sensitiveText)
    if ($FixtureScenario -eq 'restore-failure') {
        [Console]::Error.WriteLine('NU1301')
        exit 7
    }
    if ($FixtureScenario -match '^audit-error-(NU190[0-5])$') {
        [Console]::Error.WriteLine(('error {0}: {1}' -f $Matches[1], $sensitiveText))
        exit 1
    }
    exit 0
}

$auditScript = Join-Path $PSScriptRoot '../audit-nuget.ps1'
$nativePwsh = (Get-Process -Id $PID).Path
$cases = @(
    @{ Name = 'success'; Fails = $false; Expected = 'No known vulnerable packages' },
    @{ Name = 'restore-failure'; Fails = $true; Expected = 'exit code 7'; Code = 'NU1301' },
    @{ Name = 'launch-failure'; Fails = $true; Expected = 'Could not run NuGet restore' }
)
$cases += 0..5 | ForEach-Object {
    $code = 'NU190{0}' -f $_
    @{ Name = 'audit-error-' + $code; Fails = $true; Expected = 'exit code 1'; Code = $code }
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
            if ($args[0] -ne 'restore' -or $args -notcontains '-p:AuditPipeline=true') {
                throw 'Unexpected audit command.'
            }
            if ($Case.Name -eq 'launch-failure') { throw 'SYNTHETIC_AUDIT_SECRET' }
            $fixtureArgs = @('-NoProfile', '-File', $FixtureScript,
                '-FixtureScenario', $Case.Name)
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

# GitHub's pwsh wrapper must not inherit the last expected failing fixture's exit code.
exit 0
