# testing charter

Gates and per-feature risk contracts. `README.md` is the hub and the only owner of the lane
table; per-feature files state what can break and which tests cover it, as prose plus a test
directory — no duplicated lane blocks, no per-file ownership tables.

Only report a gate as passing if it ran against the current working tree.
