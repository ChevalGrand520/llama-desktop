# Llama Desktop Project Nodes

## Goal

Ship a portable Windows llama.cpp desktop shell with a focused chat workspace, reliable process lifecycle, and a small, verifiable release footprint.

## Current State

- Branch: `master`; GitHub remote: `origin`.
- 2026-08-29 UI/code-lightening pass is implemented and verified.
- Publish now creates an empty `models\` entry instead of copying local GGUF weights; verification accepts zero bundled weights.
- Preserve the user's existing edit in `scripts/package-release.ps1` that excludes model weights from release archives.

## Current Decisions

- Keep the legacy PowerShell launcher as a documented compatibility and recovery path.
- Do not change llama.cpp arguments, model scanning, or the three-phase stop protocol in the UI pass.
- Use dependency-free WPF resources: Segoe UI Variable, Segoe Fluent Icons, and Cascadia Mono fallbacks.
- Keep the primary workspace dominant; controls live in a collapsible right sidebar.

## Key Paths

- App shell: `src/LlamaDesktop.App/`
- Core behavior: `src/LlamaDesktop.Core/`
- Process and persistence adapters: `src/LlamaDesktop.Infrastructure/`
- Tests: `tests/`
- Release scripts: `scripts/`
- Architecture report: `%TEMP%\architecture-review-20260829-182129.html`

## Verification

- Baseline: 42 tests passed; App build had CS1998 and CS4014 warnings.
- Current App build: 0 errors, 0 warnings.
- Native UI checked at 1770x1170: idle view, log collapse, sidebar collapse/expand, and accessible icon names.
- Full post-change test run: 42 .NET tests plus both PowerShell compatibility suites passed.
- Remaining verification warning: `NU1900` because the NuGet vulnerability feed was temporarily unreachable.

## Open Risks

- `ServerLifecycleStateMachine` and `ProcessIdentity.Matches` are not used by the production lifecycle path despite README safety claims.
- `ShellViewModel` still owns process, health, persistence, logging, and desktop actions; this limits test locality.
- Published runtime closure is about 813 MB and needs measured dependency analysis before any DLL removal.

## Next Steps

1. Add characterization tests around lifecycle and PID validation before integrating those modules.
2. Deepen the WebView host so it owns the actual WebView lifecycle and navigation policy.
3. Audit the publish dependency closure with launch verification before changing runtime files.
