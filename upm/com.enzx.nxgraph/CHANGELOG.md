# Changelog

All notable changes to this package will be documented in this file.

## [Unreleased]
- Staging documentation now describes the actual mechanism (`dotnet run --project NxGraph.Build -- stage-source|stage-binary`); the previously referenced `scripts/build-upm.ps1` no longer exists.
- Source staging now includes the `Blackboards` and `Shims` folders (the source-mode package could not compile without them).
- Refreshed the staged binary (`Runtime/Plugins/NxGraph.dll`) — the previously committed DLL was a stale 1.0.0 build predating failure edges, suspend/resume, parallel composites, and blackboards.
- Aligned the assembly definition name (`NxGraph.Unity.Runtime`) with its file name.
- Install instructions now point at the released `upm` branch/tags instead of the main-branch package folder.

## [2.0.1-alpha]
- Alpha release published from the `upm/v2.0.1-alpha` tag (binary staging mode).

## [2.0.0.1-alpha]
- First alpha publish of the 2.x package pipeline (tag `upm/v2.0.0.1-alpha`; note: not valid SemVer — superseded by 2.0.1-alpha).
