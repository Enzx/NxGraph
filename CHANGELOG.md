# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.2.0-alpha]

### Added

- Graph payload version 12: the nested-machine subgraph entries carry the owns-board flag plus a ports list, and composite entries carry sparse per-region ports, so a composite that runs its child on the child's own board with declared inputs and outputs survives the round trip. Ports rebuild name-bound and resolve per application; pre-v12 payloads read shared-board and port-free.

- Graph payload version 11: the nested-machine subgraph entries and the history composite entries carry optional outcome key names, so a composite that publishes its child's terminal outcome onto declared blackboard keys survives the round trip. Pre-v11 payloads read outcome-key-free.

- Initial package. Ships `NxGraph.Serialization.dll` built for netstandard2.1, together with its bundled dependencies (MessagePack, System.Text.Json, and the BCL facades they require).
- Depends on `com.enzx.nxgraph` at an exactly pinned version; the two packages are versioned and released as a unit.
