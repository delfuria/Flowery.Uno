param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [Parameter(Mandatory)][string]$OutputPath,
    [Parameter(Mandatory)][string]$Correlator,
    [Parameter(Mandatory)][ValidatePattern('^refs/heads/.+')][string]$Ref,
    [switch]$Submit,
    [string]$Repository = $env:GITHUB_REPOSITORY
)

$ErrorActionPreference = 'Stop'
if ($Submit -and $Repository -notmatch '^[^/\s]+/[^/\s]+$') { throw 'Submission requires an owner/repository name.' }
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
Push-Location $RepositoryRoot
try {
    $gitArgs = @('ls-files', '--', '*.csproj')
    $projects = @(git @gitArgs)
    if ($LASTEXITCODE -ne 0 -or $projects.Count -eq 0) { throw 'No tracked C# projects found.' }
    $gitArgs = @('rev-parse', 'HEAD')
    $sha = git @gitArgs
    if ($LASTEXITCODE -ne 0 -or $sha -notmatch '^[a-f0-9]{40,64}$') { throw 'Could not read repository commit.' }
    if ($Submit) {
        $gitArgs = @('status', '--porcelain', '--untracked-files=no')
        $changes = git @gitArgs
        if ($LASTEXITCODE -ne 0 -or $changes) { throw 'Submission requires an unchanged tracked checkout.' }
    }
    $manifests = [ordered]@{}
    $packageCount = 0
    foreach ($project in $projects) {
        if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw ('Missing tracked project: {0}' -f $project) }
        $propertyArgs = @('msbuild', $project, '-nologo', '-getProperty:ProjectAssetsFile,MSBuildProjectFullPath', "-p:Configuration=$Configuration")
        $propertyOutput = dotnet @propertyArgs
        if ($LASTEXITCODE -ne 0) { throw ('Could not evaluate assets path for {0}.' -f $project) }
        $properties = ($propertyOutput -join [Environment]::NewLine) | ConvertFrom-Json -AsHashtable
        $assetsPath = $properties.Properties.ProjectAssetsFile
        if ([string]::IsNullOrWhiteSpace($assetsPath) -or -not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) {
            throw ('Restore assets are missing for {0}.' -f $project)
        }
        $assets = Get-Content -LiteralPath $assetsPath -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
        if ($assets.project.restore.projectPath -ne $properties.Properties.MSBuildProjectFullPath -or
            -not $assets.targets -or -not $assets.project.frameworks -or @($assets.logs | Where-Object { $_.level -eq 'Error' }).Count -gt 0) {
            throw ('Incomplete or failed restore assets for {0}.' -f $project)
        }
        $resolved = [ordered]@{}
        foreach ($targetEntry in $assets.targets.GetEnumerator()) {
            $target = $targetEntry.Value
            $frameworkName = ($targetEntry.Key -split '/', 2)[0]
            $framework = $assets.project.frameworks[$frameworkName]
            if (-not $framework) { throw ('Missing restore framework in {0}: {1}.' -f $project, $frameworkName) }
            $targetPackages = @{}
            $targetProjects = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($key in $target.Keys) {
                $parts = $key -split '/', 2
                if ($parts.Count -ne 2) { throw ('Invalid resolved dependency in {0}.' -f $project) }
                if ($target[$key].type -eq 'project') { [void]$targetProjects.Add($parts[0]); continue }
                if ($target[$key].type -ne 'package') { continue }
                $id = [Uri]::EscapeDataString($parts[0].ToLowerInvariant())
                $version = [Uri]::EscapeDataString($parts[1])
                $purl = 'pkg:nuget/{0}@{1}' -f $id, $version
                if ($targetPackages.ContainsKey($parts[0])) { throw ('Ambiguous resolved package in {0}.' -f $project) }
                $targetPackages[$parts[0]] = $purl
                if (-not $resolved.Contains($purl)) {
                    $resolved[$purl] = [ordered]@{
                        package_url = $purl
                        relationship = 'indirect'
                        dependencies = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                    }
                }
                if ($framework.dependencies -and $framework.dependencies.Contains($parts[0])) {
                    $resolved[$purl].relationship = 'direct'
                }
            }
            foreach ($key in $target.Keys) {
                if ($target[$key].type -ne 'package' -or -not $target[$key].dependencies) { continue }
                $name = ($key -split '/', 2)[0]
                $node = $resolved[$targetPackages[$name]]
                foreach ($dependency in $target[$key].dependencies.Keys) {
                    if ($targetProjects.Contains($dependency)) { continue }
                    if (-not $targetPackages.ContainsKey($dependency)) {
                        throw ('Unresolved package edge in {0}: {1} -> {2}.' -f $project, $name, $dependency)
                    }
                    [void]$node.dependencies.Add($targetPackages[$dependency])
                }
            }
        }
        foreach ($node in $resolved.Values) { $node.dependencies = @($node.dependencies | Sort-Object) }
        $source = $project.Replace('\', '/')
        $manifests[$source] = [ordered]@{ name = $source; file = @{ source_location = $source }; resolved = $resolved }
        $packageCount += $resolved.Count
    }
    if ($packageCount -eq 0) { throw 'No resolved NuGet packages found.' }
    $now = [DateTime]::UtcNow
    $jobId = if ($env:GITHUB_RUN_ID) { $env:GITHUB_RUN_ID } else { $now.ToString('yyyyMMddTHHmmssfffffffZ') }
    $snapshot = [ordered]@{
        version = 0
        sha = $sha
        ref = $Ref
        job = @{ id = $jobId; correlator = $Correlator }
        detector = @{ name = 'flowery-nuget-assets'; version = '1.0.0'; url = 'https://github.com/tobitege/Flowery.Uno' }
        scanned = $now.ToString('O')
        manifests = $manifests
    }
    $json = $snapshot | ConvertTo-Json -Depth 100
    $outputDirectory = Split-Path -Parent $OutputPath
    if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) { [void][IO.Directory]::CreateDirectory($outputDirectory) }
    [IO.File]::WriteAllText($OutputPath, $json, [Text.UTF8Encoding]::new($false))
    $readback = Get-Content -LiteralPath $OutputPath -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
    if ($readback.sha -ne $sha -or $readback.manifests.Count -ne $projects.Count) { throw 'Snapshot readback failed.' }
    Write-Host ('Prepared {0} manifests and {1} resolved package entries ({2}).' -f $projects.Count, $packageCount, $Configuration)
    if ($Submit) {
        $endpoint = 'repos/{0}/dependency-graph/snapshots' -f $Repository
        $apiArgs = @('api', '--method', 'POST', $endpoint, '--input', $OutputPath, '-H', 'Accept: application/vnd.github+json')
        $response = gh @apiArgs
        if ($LASTEXITCODE -ne 0) { throw 'Dependency snapshot submission failed.' }
        $result = ($response -join [Environment]::NewLine) | ConvertFrom-Json
        if ($result.result -ne 'SUCCESS') { throw 'GitHub did not confirm dependency snapshot submission.' }
        Write-Host ('Dependency snapshot submitted: {0}.' -f $result.id)
    }
}
finally {
    Pop-Location
}
