<#
.SYNOPSIS
    Checks the result of a Gallery runtime test run in CI.

.DESCRIPTION
    Lists the tests that did not pass, with their messages, so the log shows the cause, and then fails when the run
    exited with an error, when a test failed, when no test ran, or when one of the required regression tests did not
    pass. Inconclusive tests other than the required ones are listed but do not fail the check. Every Gallery job of
    .github/workflows/ci.yml that runs the runtime tests calls this script after its run, so the list of required tests
    lives in one place. scripts/tests/Assert-GalleryRuntimeTests.test.ps1 checks it.

.PARAMETER Result
    Path of the result file that the Gallery wrote with --runtime-tests.

.PARAMETER Label
    Name of the run in the log, such as "Linux Desktop".

.PARAMETER ExitCode
    Exit code of the Gallery run.

.EXAMPLE
    ./scripts/ci/Assert-GalleryRuntimeTests.ps1 -Result $result -Label 'Linux Desktop' -ExitCode $LASTEXITCODE
#>
param(
    [Parameter(Mandatory)][string]$Result,
    [Parameter(Mandatory)][string]$Label,
    [Parameter(Mandatory)][int]$ExitCode
)

$ErrorActionPreference = 'Stop'

# Regression tests that must pass on every head; they have regressed silently before.
$requiredTests = @(
    'Flowery.Uno.RuntimeTests.Controls.Given_CarouselRendering.When_Checkerboard_Transitions_MaskChangesDifferentRegions',
    'Flowery.Uno.RuntimeTests.Controls.Given_CarouselRendering.When_Leaving_AnimatedSlide_OutgoingImagePoseStaysFrozen',
    'Flowery.Uno.RuntimeTests.Controls.Given_SkiaImageProcessing.When_GlassEffect_ProcessesPixels_BlurSaturationAndRoundedClipApply',
    'Flowery.Uno.Gallery.Tests.Given_CarouselGalleryCycles.When_TransitionOutlastsTimer_EachCompletedSlideGetsItsNextEffect',
    'Flowery.Uno.Gallery.Tests.Given_CarouselGalleryCycles.When_FiniteSlideshowEnds_EnablingInfiniteResumesWithNextEffect'
)

# Name failed and inconclusive tests before stopping, so the log shows the cause.
if (Test-Path -LiteralPath $Result) {
    [xml]$report = Get-Content -LiteralPath $Result -Raw
    foreach ($test in @($report.tests.test | Where-Object outcome -NE 'Passed')) {
        Write-Host ('{0}: {1} {2}' -f $Label, $test.outcome, $test.name)
        if ($test.message) { Write-Host $test.message }
    }
}

if ($ExitCode -ne 0) { throw ('{0}: the Gallery runtime tests exited with code {1}.' -f $Label, $ExitCode) }
if (-not (Test-Path -LiteralPath $Result)) { throw ('{0}: no runtime test result at {1}.' -f $Label, $Result) }
if ([int]$report.tests.failed -ne 0) { throw ('{0}: Gallery runtime tests failed.' -f $Label) }
if ([int]$report.tests.total -le 0 -or [int]$report.tests.passed -le 0) { throw ('{0}: no Gallery runtime tests ran.' -f $Label) }
foreach ($name in $requiredTests) {
    $test = @($report.tests.test | Where-Object name -EQ $name)
    if ($test.Count -ne 1 -or $test[0].outcome -ne 'Passed') {
        throw ('{0}: required regression test did not pass: {1}' -f $Label, $name)
    }
}
Write-Host ('{0}: {1} runtime tests passed' -f $Label, $report.tests.passed)
