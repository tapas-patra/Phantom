# Phantom Live Copilot Implementation Plan

This is the repository-level entrypoint for the cross-platform Phantom Live Copilot plan.

The complete implementation authority is [`phantom-windows-app/docs/11-live-copilot-optimization-plan.md`](phantom-windows-app/docs/11-live-copilot-optimization-plan.md). Its location under the Windows documentation tree is historical; the plan applies equally to:

- `phantom-windows-app/`;
- `phantom-mac-app/`;
- `phantom-windows-app-backend/`;
- `phantom-dashboard-backend/` where shared request observability is involved;
- `phantom-website-dashboard/` where shared correlation behavior is involved.

Coding agents must implement shared contracts and fixtures once, then provide compatible native C# and Swift implementations. A work package is not complete after changing only Windows or only macOS unless the assigned task explicitly narrows its platform scope.

