# phantom-windows-app

Future dedicated repository for the Windows desktop app.

Current reality:
- the active implementation still lives in the current workspace root
- this folder is the extraction target, not the active build root yet

Owns:
- WPF UI
- local runtime persistence
- local secret vault
- context packs
- local telemetry and sync queues
- offline continuation behavior

Extraction note:
- do not physically move the live app here until build verification is available
