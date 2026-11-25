# Documentation

This folder contains the active engineering reference, reproducible validation records and preserved historical reports.

## Start here

- [Architecture overview](architecture/ARCHITECTURE.md) — runtime composition, subsystem ownership and thread boundaries.
- [Data lifecycle](architecture/DATA_LIFECYCLE.md) — world edits, meshing, persistence and compatibility invariants.
- [Development setup](development/SETUP.md) — Godot and .NET toolchain.
- [Testing and validation](development/TESTING.md) — headless suites, soak tests, visual captures and benchmarks.
- [Adding content safely](development/ADDING_CONTENT.md) — IDs, generator versions, assets and save compatibility.
- [Code style](development/CODE_STYLE.md) — EditorConfig and formatter workflow.
- [Release process](development/RELEASING.md) — versioning, exports, checksums and release verification.
- [Repository profile](REPOSITORY_PROFILE.md) — suggested GitHub description, topics and visibility decision.

## Current reference material

| Area | Documents |
|---|---|
| Performance | [Benchmark procedure](PERFORMANCE_BENCHMARKS.md), [baseline](PERF_BASELINE.md), [Milestone 2](PERFORMANCE_MILESTONE_2_MX230.md), [Milestone 3](PERFORMANCE_MILESTONE_3_STREAMING.md), [renderer quality matrix](RENDERER_QUALITY_MATRIX.md) |
| Visual quality | [Visual Milestone 4](VISUAL_MILESTONE_4_ENVIRONMENT.md), [lighting comparison](visual/LIGHTING_COMPARISON_REPORT_2026-11-22.md), [material gallery](visual/MATERIAL_GALLERY_VISUAL_QUALITY_REPORT_2026-11-22.md), [procedural texture validation](visual/PROCEDURAL_TEXTURE_VALIDATION.md) |
| World generation | [V8 visual validation](worldgen/V8_VISUAL_VALIDATION.md), [V9 visual validation](worldgen/V9_VISUAL_VALIDATION.md) |
| Gameplay and reliability | [Gameplay Milestone 5](GAMEPLAY_MILESTONE_5.md), [player journey repair](PLAYER_JOURNEY_REPAIR_REPORT_2026-11-21.md), [survival progression audit](SURVIVAL_PROGRESSION_AUDIT_2026-11-21.md), [reliability audit](RELIABILITY_AUDIT_2026-11-21.md), [engine audit](ENGINE_AUDIT_2026-11-20.md) |
| UI and first person | [UI design guidelines](UI_DESIGN_GUIDELINES.md), [first-person animation](FIRST_PERSON_ANIMATION.md) |
| Integration records | [PRs 81–91](INTEGRATION_PR81_91.md), [PRs 70–79](INTEGRATION_PR70_79.md), [PRs 59–68](INTEGRATION_PR59_68.md), [PRs 49–57](INTEGRATION_PR49_57.md), [November 21 integration](INTEGRATION_2026-11-21.md) |

## Historical records

The [history folder](history/) contains pre-restructure snapshots of the README, roadmap and plan. They preserve the detailed implementation diary that was previously mixed into top-level project documents. The current README, ROADMAP and PLAN are the active sources of truth; historical snapshots should not be used to infer current status.

The remediation folder contains the frozen worker-assignment issue ledger, release/test baseline and the 2026-11-25 unmerged-branch audit (W11 selection, W12 slice plan, W15 decision and W13 measurements). Those files describe their explicitly recorded baseline commits and are intentionally retained as historical records.
