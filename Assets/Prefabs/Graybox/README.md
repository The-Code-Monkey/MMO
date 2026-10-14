# Modular Graybox Kit

A set of reusable, grid-snapping primitive prefabs for rapid level prototyping. All pieces snap to a **1m or 2m grid**.

## Prefabs

### Floors
| Prefab | Dimensions (m) | Grid Size | Use Case |
|--------|---------------|-----------|----------|
| `Floor_1x1` | 1×0.1×1 | 1m | Small rooms, detail patches |
| `Floor_2x2` | 2×0.1×2 | 2m | Large rooms, corridors, outdoor areas |

### Walls
| Prefab | Dimensions (m) | Grid Size | Use Case |
|--------|---------------|-----------|----------|
| `Wall_1x2` | 1×2×0.2 | 1m (W) × 2m (H) | Standard wall segment |
| `Wall_2x2` | 2×2×0.2 | 2m (W) × 2m (H) | Wide wall segment |
| `Wall_1x3` | 1×3×0.2 | 1m (W) × 3m (H) | Tall walls, multi-story |

### Ramps & Stairs
| Prefab | Dimensions (m) | Grid Size | Use Case |
|--------|---------------|-----------|----------|
| `Ramp_1x2` | 1×1×2 | 1m (W) × 2m (L) | Accessibility, elevation changes |
| `Stairs_2x2` | 2×2×2 | 2m × 2m | Level transitions, multi-floor |

## Usage

1. Drag prefabs from `Assets/Prefabs/Graybox/` into the scene
2. Position using **Vertex Snapping** (hold `V`) for perfect grid alignment
3. All pieces have `BoxCollider` and are marked **Navigation Static** for NavMesh baking
4. Replace with final art assets when design is locked

## ProBuilder

ProBuilder (`com.unity.probuilder` 5.1.0) is installed for custom shape editing. To modify a piece:
1. Select the prefab in Project view
2. Open **Tools → ProBuilder → ProBuilder Window**
3. Click **Edit** to modify vertices/faces
4. Save as new prefab variant if needed

## Conventions

- **Pivot** at center-bottom for floors, center for walls/ramps/stairs
- **Scale** = grid units (1 = 1m)
- **Y-up** orientation
- **Layer**: Default (0)
- **Static Flags**: Navigation Static + Contribute GI