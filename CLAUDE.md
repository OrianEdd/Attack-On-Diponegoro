# Project Overview
- **Engine:** Unity 6 (2D Side-View Action Combat, Sekiro-inspired)
- **Namespace:** `PGIM.Sekiro2D`
- **Scripts Directory:** `Assets/Scripts/`

# Strict File & Token Rules
- **NEVER** read, search inside, modify, or touch `.unity`, `.prefab`, `.controller`, `.anim`, or `.meta` files (they are large serialized YAML files that waste tokens and risk scene corruption).
- Only read and edit `.cs` scripts inside `Assets/Scripts/` unless explicitly instructed otherwise.
- Do not scan `Library/`, `Temp/`, `Logs/`, or `Packages/` folders.

# Key Architecture (`Assets/Scripts/`)
- `PlayerController.cs` — Movement, jumping, facing direction, and physics.
- `PlayerCombat.cs` — Attack combos, hold-to-block, perfect parry window, guard break, deathblow, and hit-stop.
- `EnemyKlewang.cs` — Enemy AI state machine, attacks, posture/guard break, and damage handling.
- `CombatFeedback.cs` — Auto-instanced singleton for procedural SFX, spark VFX, and unscaled camera shake.

# Coding & Verification Standards
- Keep edits clean, modular, and free of compiler warnings.
- Whenever a component needs runtime setup without touching scenes/prefabs, initialize it procedurally in code (e.g., `Awake()` / `Start()`).
- Always summarize modified files and verify compilation with 0 errors and 0 warnings before finishing a task.