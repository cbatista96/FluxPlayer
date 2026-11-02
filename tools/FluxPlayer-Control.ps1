param(
    [Parameter(Mandatory)][string]$PipeName,
    [Parameter(Mandatory)][ValidateSet('ping','state.get','play','pause','stop','next','previous','seek','volume','rate','mute','open','queue.add','queue.clear','events.get')][string]$Method,
    [string]$Token = $env:FLUXPLAYER_CONTROL_TOKEN,
    [string]$Parameters = '{}'
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Token)) { throw 'Set FLUXPLAYER_CONTROL_TOKEN to the active session token shown in Local external control.' }
$arguments = $Parameters | ConvertFrom-Json
$request = @{jsonrpc='2.0';id=[Guid]::NewGuid().ToString('N');method=$Method;params=$arguments;token=$Token} | ConvertTo-Json -Depth 12 -Compress
if ($request.Length -gt 65536) { throw 'Request exceeds the control-message limit.' }
$pipe = [IO.Pipes.NamedPipeClientStream]::new('.', $PipeName, [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
try {
    $pipe.Connect(2000)
    $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false), 4096, $true)
    $reader = [IO.StreamReader]::new($pipe, [Text.UTF8Encoding]::new($false), $false, 4096, $true)
    try { $writer.AutoFlush = $true; $writer.WriteLine($request); $result = $reader.ReadLineAsync(); if (-not $result.Wait(5000)) { throw 'Control response timed out.' }; $result.Result }
    finally { $reader.Dispose(); $writer.Dispose() }
} finally { $pipe.Dispose() }
