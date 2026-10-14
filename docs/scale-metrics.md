# MMO Scale Metrics

This document defines the canonical scale metrics for the MMO project. All measurements are in **Unity units = metres**.

> **Source of truth:** The authoritative constants live in [`ScaleMetrics.cs`](../Assets/Scripts/Gameplay/ScaleMetrics.cs). This document mirrors those values for human readers. When metrics change, update `ScaleMetrics.cs` and regenerate this table.

---

## Metrics Table

| Element | Metric | Notes |
|---------|--------|-------|
| Character capsule | 1.8 m tall, 0.5 m radius | Drives `CharacterController` + camera height |
| Doorway | 1.5 m wide, 2.5 m tall | Must clear a 1.8 m character with headroom |
| Stairs | max step 0.25 m | `CharacterController.stepOffset`, NavMesh step height |
| Combat grid | 1 unit = 1 m | Attack/ability ranges are metres |
| Modular grid | 1 m and 2 m | Kit snaps |

---

## Derived Constants

| Constant | Value | Purpose |
|----------|-------|---------|
| `CharacterClearance` | 0.2 m | Extra headroom above character for doorway checks |
| `GridSmall` | 1 m | Small modular grid snap |
| `GridLarge` | 2 m | Large modular grid snap |
| `Unit` | 1 m | Base unit for combat/ability ranges |

---

## Validation Rules

### Doorway Admission
A doorway admits a standing character when:
- Width ≥ CharacterDiameter (1.0 m)
- Height ≥ CharacterHeight + CharacterClearance (2.0 m)

The default doorway (1.5 m × 2.5 m) satisfies this.

### Grid Alignment
Modular kit pieces should have X/Z footprints that are whole multiples of:
- `GridSmall` (1 m) for small pieces
- `GridLarge` (2 m) for large pieces

### Step Height
Stairs and elevation changes must not exceed `MaxStepHeight` (0.25 m) so the `CharacterController` and NavMesh can traverse them.

---

## Code References

- **Constants:** `MMO.Gameplay.ScaleMetrics`
- **Doorway check:** `ScaleMetrics.AdmitsCharacter(width, height)`
- **Grid check:** `ScaleMetrics.OnGrid(size, grid)`
- **Editor validator:** `Assets/Editor/ScaleMetricsValidator.cs` (menu: **Tools → MMO → Validate Scale**)

---

## Changing Metrics

1. Edit `Assets/Scripts/Gameplay/ScaleMetrics.cs`
2. Run EditMode tests: `ScaleMetricsTests`
3. Run the editor validator: **Tools → MMO → Validate Scale**
4. This document will be updated automatically via the constants