; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
;
; Reserved ID: VEX0002 is deliberately absent from the table below - it is claimed by the planned
; T5 rule VEX0002CallbackDataTooLong (callback data must be <= 64 bytes when UTF-8 encoded).
; Do not reuse VEX0002 for another rule and do not renumber the rules that follow.

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
VEX0001 | Vexel.Telegram | Error | Missing [Handler] on Vexel route type
VEX0003 | Vexel.Telegram | Error | Invalid Telegram command name
VEX0004 | Vexel.Telegram | Error | Unbindable request shape
VEX0005 | Vexel.Telegram | Error | Duplicate route key
