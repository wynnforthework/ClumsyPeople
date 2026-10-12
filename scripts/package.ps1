param([Parameter(Mandatory)][string]$Models)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'ClumsyPeople/bin/x64/Release'
$destination = Join-Path $root '.artifacts'
New-Item -ItemType Directory -Force $destination | Out-Null
$packageStage = Join-Path $destination ('package-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $packageStage | Out-Null
try {
    Get-ChildItem $output -File | Where-Object Extension -In '.dll','.json','.pdb' | Copy-Item -Destination $packageStage
    $native = Join-Path $output 'runtimes/win-x64/native'
    $nativeTarget = Join-Path $packageStage 'runtimes/win-x64/native'
    New-Item -ItemType Directory -Force $nativeTarget | Out-Null
    Copy-Item "$native/*.dll" $nativeTarget
    Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $packageStage
    Copy-Item (Join-Path $root 'licenses') $packageStage -Recurse
    $modelTarget = Join-Path $packageStage 'models'
    New-Item -ItemType Directory $modelTarget | Out-Null
    $asrName = 'sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23'
    $punctuationName = 'sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8'
    foreach ($name in @($asrName, $punctuationName)) {
        $model = Join-Path $Models $name
        if (-not (Test-Path -LiteralPath $model -PathType Container)) { throw "Missing extracted model directory: $model" }
        Copy-Item -LiteralPath $model -Destination $modelTarget -Recurse
    }
    foreach ($required in @('ClumsyPeople.dll','ClumsyPeople.json','sherpa-onnx.dll','runtimes/win-x64/native/onnxruntime.dll','runtimes/win-x64/native/sherpa-onnx-c-api.dll','runtimes/win-x64/native/e_sqlite3.dll',
        "models/$asrName/encoder-epoch-99-avg-1.int8.onnx", "models/$asrName/decoder-epoch-99-avg-1.int8.onnx", "models/$asrName/joiner-epoch-99-avg-1.int8.onnx", "models/$asrName/tokens.txt", "models/$punctuationName/model.int8.onnx")) {
        if (-not (Test-Path (Join-Path $packageStage $required))) { throw "Missing package file: $required" }
    }
    $manifest = Get-Content (Join-Path $packageStage 'ClumsyPeople.json') -Encoding UTF8 -Raw | ConvertFrom-Json
    if ($manifest.DalamudApiLevel -ne 15 -or $manifest.InternalName -ne 'ClumsyPeople') { throw 'Invalid manifest' }
    Compress-Archive -Path "$packageStage/*" -DestinationPath (Join-Path $destination 'ClumsyPeople-0.1.0.0.zip') -Force
    Get-FileHash (Join-Path $destination 'ClumsyPeople-0.1.0.0.zip') -Algorithm SHA256 |
        Format-List Algorithm,Hash,Path
} finally {
    # A newly created, absolute staging directory inside .artifacts only.
    $checkedStage = [IO.Path]::GetFullPath($packageStage)
    if (-not $checkedStage.StartsWith([IO.Path]::GetFullPath($destination) + [IO.Path]::DirectorySeparatorChar)) { throw 'Invalid staging path' }
    Remove-Item -LiteralPath $checkedStage -Recurse -Force
}
