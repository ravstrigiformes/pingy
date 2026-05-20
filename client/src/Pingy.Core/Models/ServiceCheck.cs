namespace Pingy.Core.Models;

// Optional L7 health check that runs only after the TCP port connects. All fields
// past Kind are nullable/defaulted so absence means "no L7 check" and pre-dev.4
// targets.json deserializes unchanged.
public sealed record ServiceCheck(
    string Kind,                     // "http" | "https" (v1)
    string? Path = null,             // request path, defaults to "/"
    int? ExpectStatus = null,        // expected HTTP status, defaults to 200
    bool AcceptSelfSigned = false,   // https only — skip cert validation (intranet self-signed)
    int? TimeoutMs = null);          // optional L7 timeout override; falls back to the probe timeout
