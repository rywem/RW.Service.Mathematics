# Documentation

> **Handing this repo to someone (or some agent) new? Start with [handoff-consumed.md](handoff-consumed.md).**
> Collection-wide context: [`Solutions/Mathematics/handoff-consumed.md`](../../Solutions/Mathematics/handoff-consumed.md)


This repository is part of the **RW Mathematics** project collection. The canonical
documentation lives in the modules repository and covers every repo in the collection:

| Document | Location | Covers |
|---|---|---|
| Licensing (GPL / Maxima / App Store) | `RW.Mathematics.Modules/Documentation/LICENSING.md` | **Read before touching Maxima source** |
| Mistakes register | `RW.Mathematics.Modules/Documentation/MISTAKES.md` | Changes that broke something, and the rule that prevents a repeat |
| Lessons learned | `RW.Mathematics.Modules/Documentation/LESSONS.md` | Architecture and CAS rationale |
| Improvements log | `RW.Mathematics.Modules/Documentation/IMPROVEMENTS.md` | Provenance and authorship record |
| iOS port briefing | `iOS.Matrix/Documentation/README.md` | For the agent doing the mobile port |
| Test catalog | `RW.Mathematics.Modules/docs/TEST-CATALOG.md` | Every math/EE case, with stable IDs |
| RPC reference | `RW.Mathematics.Modules/docs/RPC.md` | Driving the engine from other programs |

## This repository's role

Thin facade over AngouriMath (MIT). Wrapped by the `algebra` module. Predates the modular architecture.

## Single point of operation

`E:\Development\Projects\Solutions\Mathematics\Mathematics.slnx` contains every project in
the collection. Build and test from there.
