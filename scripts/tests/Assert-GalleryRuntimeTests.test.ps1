# Checks that scripts/ci/Assert-GalleryRuntimeTests.ps1, which decides the runtime test steps of the Gallery jobs in CI,
# fails and passes on the right results. Each case writes a small result file and runs the real script on it.

$ErrorActionPreference = 'Stop'

$assertScript = Join-Path $PSScriptRoot '../ci/Assert-GalleryRuntimeTests.ps1'
$requiredTests = @(
    'Flowery.Uno.RuntimeTests.Controls.Given_CarouselRendering.When_Checkerboard_Transitions_MaskChangesDifferentRegions',
    'Flowery.Uno.RuntimeTests.Controls.Given_CarouselRendering.When_Leaving_AnimatedSlide_OutgoingImagePoseStaysFrozen',
    'Flowery.Uno.RuntimeTests.Controls.Given_SkiaImageProcessing.When_GlassEffect_ProcessesPixels_BlurSaturationAndRoundedClipApply',
    'Flowery.Uno.Gallery.Tests.Given_CarouselGalleryCycles.When_TransitionOutlastsTimer_EachCompletedSlideGetsItsNextEffect',
    'Flowery.Uno.Gallery.Tests.Given_CarouselGalleryCycles.When_FiniteSlideshowEnds_EnablingInfiniteResumesWithNextEffect'
)

# Writes a result file in the format of RuntimeTestRunner: required tests with the given outcome, plus other tests.
function New-Result {
    param([string]$Path, [string]$RequiredOutcome = 'Passed', [string[]]$Skip = @(), [hashtable]$Others = @{ 'Other.Test' = 'Passed' })

    $tests = @()
    foreach ($name in $requiredTests | Where-Object { $_ -notin $Skip }) { $tests += @{ Name = $name; Outcome = $RequiredOutcome } }
    foreach ($entry in $Others.GetEnumerator()) { $tests += @{ Name = $entry.Key; Outcome = $entry.Value } }
    $passed = @($tests | Where-Object Outcome -EQ 'Passed').Count
    $failed = @($tests | Where-Object Outcome -EQ 'Failed').Count
    $inconclusive = @($tests | Where-Object Outcome -EQ 'Inconclusive').Count
    $lines = $tests | ForEach-Object { '  <test name="{0}" outcome="{1}" />' -f $_.Name, $_.Outcome }
    $xml = @('<tests total="{0}" passed="{1}" failed="{2}" inconclusive="{3}">' -f $tests.Count, $passed, $failed, $inconclusive) + $lines + '</tests>'
    Set-Content -LiteralPath $Path -Value $xml -Encoding utf8
}

$directory = Join-Path ([System.IO.Path]::GetTempPath()) ('assert-gallery-runtime-tests-{0}' -f [guid]::NewGuid())
New-Item -ItemType Directory -Path $directory | Out-Null
$cases = @(
    @{ Name = 'all passed'; Fails = $false; Setup = { param($p) New-Result $p } },
    @{ Name = 'other test inconclusive'; Fails = $false; Setup = { param($p) New-Result $p -Others @{ 'Other.Test' = 'Inconclusive' } } },
    @{ Name = 'other test failed'; Fails = $true; Expected = 'tests failed'; Setup = { param($p) New-Result $p -Others @{ 'Other.Test' = 'Failed' } } },
    @{ Name = 'no tests'; Fails = $true; Expected = 'no Gallery runtime tests ran'; Setup = { param($p) New-Result $p -Skip $requiredTests -Others @{} } },
    @{ Name = 'required test inconclusive'; Fails = $true; Expected = 'required regression test'; Setup = { param($p) New-Result $p -RequiredOutcome 'Inconclusive' } },
    @{ Name = 'required test missing'; Fails = $true; Expected = 'required regression test'; Setup = { param($p) New-Result $p -Skip $requiredTests[0] } },
    @{ Name = 'error exit'; Fails = $true; Expected = 'exited with code 1'; ExitCode = 1; Setup = { param($p) New-Result $p } },
    @{ Name = 'missing result'; Fails = $true; Expected = 'no runtime test result'; Setup = { param($p) } }
)

try {
    foreach ($case in $cases) {
        $result = Join-Path $directory ('{0}.xml' -f ($case.Name -replace '\W', '-'))
        & $case.Setup $result
        $exitCode = if ($case.ExitCode) { $case.ExitCode } else { 0 }
        $message = $null
        try {
            & $assertScript -Result $result -Label $case.Name -ExitCode $exitCode 6> $null
        }
        catch {
            $message = $_.Exception.Message
        }

        if ($case.Fails -and -not $message) { throw ('{0}: the check passed, but it has to fail.' -f $case.Name) }
        if (-not $case.Fails -and $message) { throw ('{0}: the check failed, but it has to pass: {1}' -f $case.Name, $message) }
        if ($case.Fails -and $message -notlike ('*{0}*' -f $case.Expected)) {
            throw ('{0}: the check failed for another reason: {1}' -f $case.Name, $message)
        }
        Write-Host ('{0}: {1}' -f $case.Name, $(if ($case.Fails) { 'fails as expected' } else { 'passes as expected' }))
    }
}
finally {
    Remove-Item -LiteralPath $directory -Recurse -Force
}
