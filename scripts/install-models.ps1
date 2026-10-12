param([Parameter(Mandatory)][string]$Destination, [string]$Proxy = '')
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Destination | Out-Null
$archives = @(
    @{Tag='asr-models';Name='sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23';Hash='2cbd71b640d9c37d3784f29367333a4577b0398b62e9deeed418170b081cba8b'},
    @{Tag='punctuation-models';Name='sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8';Hash='c0d5aa5f8eeb686032345e180bedf39319dc2e0556781c6264bcadba8328a6e1'}
)
foreach ($archive in $archives) {
    $path = Join-Path $Destination ($archive.Name + '.tar.bz2')
    $request = @{Uri="https://github.com/k2-fsa/sherpa-onnx/releases/download/$($archive.Tag)/$($archive.Name).tar.bz2";OutFile=$path;UseBasicParsing=$true}
    if ($Proxy) { $request.Proxy = $Proxy }
    Invoke-WebRequest @request
    if ((Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $archive.Hash) { throw "Checksum failed: $path" }
    & tar.exe -xjf $path -C $Destination
    if ($LASTEXITCODE) { throw 'Model extraction failed' }
    Remove-Item -LiteralPath $path
}
