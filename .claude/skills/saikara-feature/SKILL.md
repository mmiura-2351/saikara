---
name: saikara-feature
description: Implement a Saikara feature the right way — logic in Saikara.Core with TDD on Linux, thin UI in Saikara.App. Use when adding karaoke functionality (MIDI, pitch, scoring, lyrics, library, key/tempo).
---

# Add a Saikara feature

1. **Place it** (see `docs/ARCHITECTURE.md`): algorithmic / non-UI logic → `Saikara.Core`
   (net8.0, testable on Linux). UI → `Saikara.App` (Windows-only). Windows-only dependencies
   get an interface in Core and an implementation in App.
2. **TDD in Core:** add an xUnit test under `tests/Saikara.Core.Tests`, then implement under
   `src/Saikara.Core`. Run until green:
   `dotnet test tests/Saikara.Core.Tests/Saikara.Core.Tests.csproj --nologo`.
3. **Delegate** large, self-contained Core work to the `saikara-core-dev` agent to conserve
   context; integrate and sanity-check its result.
4. **UI:** bind a thin view-model in `Saikara.App` to the Core service. Use the official
   Microsoft WinUI skills at each stage — `winui:winui-design` for new XAML/theming,
   `winui:winui-code-review` before merge, `winui:winui-ui-testing` for UI tests — and
   delegate larger UI builds to the `winui:winui-dev` agent. The WinUI build is verified
   only by CI — see the `saikara-ci` skill.
5. **Track:** tick `docs/ROADMAP.md`, update the relevant GitHub issue.
