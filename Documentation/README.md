# Documentation

> **Handing this repo to someone (or some agent) new? Start with [handoff-consumed.md](handoff-consumed.md).**
> Collection-wide context: [`Solutions/Mathematics/handoff-consumed.md`](../../Solutions/Mathematics/handoff-consumed.md)


This repository is part of the **RW Mathematics** project collection. The canonical
documentation lives in the modules repository and covers every repo in the collection:

| Document | Location | Covers |
|---|---|---|
| Licensing (GPL / Maxima / App Store) | `RW.Modules.Mathematics/Documentation/LICENSING.md` | **Read before touching Maxima source** |
| Mistakes register | `RW.Modules.Mathematics/Documentation/MISTAKES.md` | Changes that broke something, and the rule that prevents a repeat |
| Lessons learned | `RW.Modules.Mathematics/Documentation/LESSONS.md` | Architecture and CAS rationale |
| Improvements log | `RW.Modules.Mathematics/Documentation/IMPROVEMENTS.md` | Provenance and authorship record |
| iOS port briefing | `iOS.Matrix/Documentation/README.md` | For the agent doing the mobile port |
| Test catalog | `RW.Modules.Mathematics/docs/TEST-CATALOG.md` | Every math/EE case, with stable IDs |
| RPC reference | `RW.Modules.Mathematics/docs/RPC.md` | Driving the engine from other programs |

## This repository's role

Thin facade over AngouriMath (MIT). Wrapped by the `algebra` module. Predates the modular architecture.

## Single point of operation

`E:\Development\Projects\Solutions\Mathematics\Mathematics.slnx` contains every project in
the collection. Build and test from there.
