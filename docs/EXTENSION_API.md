# Player extension API v1

Settings → General → Extensions → Player extensions opens the host. Choose a JSON manifest, inspect the displayed executable and arguments, and explicitly run it. The host sends a playback snapshot, displays proposed actions and waits for a separate Apply. No programs are discovered or started automatically. Closing the panel cancels its running job.

Extensions are trusted local Windows programs with the user's normal Windows access. This is an external command-provider API, not a sandbox or a decoder/audio-effect ABI. Version 1 has no persistent event subscriptions. Credentials, media file paths and library records are not part of the snapshot.

Manifest example (relative executable paths resolve beside the manifest):

```json
{
  "apiVersion": 1,
  "id": "example.quiet-mode",
  "name": "Quiet mode",
  "executable": "QuietMode.exe",
  "arguments": []
}
```

The manifest is at most 64 KiB. IDs contain 1–64 ASCII letters, digits, dots, underscores or hyphens, starting with a letter/digit. Names contain 1–100 printable characters. Only existing local `.exe` programs are supported. There are at most 32 arguments of at most 4,096 characters; arguments are passed individually without a shell.

One invocation receives one JSON line on standard input, followed by EOF:

```json
{
  "apiVersion": 1,
  "title": "Example track",
  "playing": true,
  "positionMs": 12000,
  "durationMs": 180000,
  "volume": 50,
  "muted": false,
  "rate": 1,
  "queue": [{ "index": 0, "title": "Example track" }]
}
```

The queue includes at most its first 200 entries. Return exactly one JSON document on standard output, write diagnostics to standard error, then exit with code zero:

```json
{
  "apiVersion": 1,
  "message": "Set quiet listening volume.",
  "actions": [{ "command": "volume", "value": 25 }]
}
```

Allowed actions:

| Command | Value |
| --- | --- |
| `play`, `pause`, `stop`, `next`, `previous` | Omit or use zero |
| `volume` | 0–100; the player's configured maximum still applies |
| `mute` | 0 or 1 |
| `rate` | 0.5–2 |
| `seek` | Absolute milliseconds in the current known duration, at most 86,400,000 |
| `queue` | Integer index in the returned queue, 0–199 |

Responses allow at most 20 actions, 4,000 message characters and 65,536 output characters. Jobs have a 30-second deadline. Nonzero exits, malformed JSON, unsupported API versions, invalid commands, excessive output and cancellation discard the response. The host attempts to terminate the owned process tree on cancellation/failure. Providers should remain one-shot programs and terminate any subprocesses they own.

Applying consumes the reviewed response once. A changed current item or queue requires a new run. All action shapes and initial media/queue bounds are checked before transport commands execute. Commands execute sequentially against the live player; playback commands do not form a rollback transaction. The protocol does not open arbitrary media, edit files or expose arbitrary player internals.

Minimal .NET console implementation:

```csharp
using System.Text.Json;

using var input = JsonDocument.Parse(Console.ReadLine() ?? "{}");
if (input.RootElement.GetProperty("apiVersion").GetInt32() != 1)
    return 2;

Console.Write(JsonSerializer.Serialize(new
{
    apiVersion = 1,
    message = "Set quiet listening volume.",
    actions = new[] { new { command = "volume", value = 25 } }
}));
return 0;
```

Publish this as a Windows executable using the .NET SDK, place its manifest beside it and load the manifest through the Extensions panel. Test fixtures in `ContinuationChecks` exercise actual child processes, invalid replies, exit failures, output flooding and cancellation.
