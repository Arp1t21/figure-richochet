# Figure Ricochet

An engaging, hypnotic physics game to play while listening to stories from Reddit, podcasts, or lo-fi playlists. There is also a full sandbox mode — built from the ground up for fans of simple, oddly satisfying physics toys, bouncing-ball arenas, mathematical billiards, and viral rotating-ring challenges.

Everything runs inside a single minimalist neon-dark window (`C#` / `.NET 8` WinForms) with custom CPU post-processing effects (bloom, chromatic aberration, vignette, subtle scanlines & film grain) and crisp analytical collision physics.

---

## Quick Start

Requires **.NET 8 SDK** (or newer) on Windows.

Double-click **`ЗАПУСК.bat`** or run from the terminal:

```powershell
dotnet run -c Release
```

Developer & verification flags:

```powershell
dotnet run -c Release -- --selftest     # headless physics, rendering, FX & mode self-tests
dotnet run -c Release -- --bench        # frame-time benchmark (post-FX, sim+render, 1440 & 5760 rays)
dotnet run -c Release -- --shots DIR    # render PNG screenshots of all modes (including 150% DPI)
```

---

## Four Modes in One Window

Switch modes anytime from the left navigation sidebar — each mode preserves its state while inactive:

### 1. Levels (`УРОВНИ`)
Pure geometric ricochet arenas (free-play circles & squares, obstacle chambers, triangle & rectangle bounce rooms, and open-air gravity).
- **Drag & release LMB** to launch a ball in any direction (fixed speed).
- **No intrusive win screens** — keep launching balls, experimenting with trajectories, or watching endless elastic ricochets uninterrupted.
- Loads both built-in arenas and any custom levels saved in `levels/*.json`.

### 2. Sandbox & Level Editor (`РЕДАКТОР`)
A full physics sandbox where you can sketch ideas and test them with a single keystroke (`Space`).
- **Draw anything:** freehand walls, line segments, rectangles, analytical/polygon ellipses, and goal zones.
- **Tune physics on the fly:** toggle grid, `20 px` snap, elastic world border, ball-to-ball collisions, gravity, ball radius (`R`), and launch speed (`V`).
- **Save & share:** `Ctrl+S` saves clean JSON files into `levels/`, immediately available in **Levels** mode.

### 3. Wavefront — Mathematical Billiards (`ВОЛНОВОЙ ФРОНТ`)
Emit a radial fan of `10` to `100,000` rays from any point inside **24 classic mathematical billiard tables** and watch wavefront evolution, caustics, and chaos unfold in real time:
- **Integrable & smooth:** Circle (astroid caustics), Ellipse (focal families), Superellipse, Heart, Crescent, Semicircle.
- **Chaotic & polygonal:** Bunimovich Stadium, Triangle, Square, Rhombus, Pentagon, Hexagon, Octagon, 10-Point Star, Dumbbell, Cross, Staircase, Partitioned Chambers.
- **Click anywhere** on the arena to move the wave source and fire a fresh pulse; choose solid neon colors or full **Spectrum** rainbow fans.

### 4. Rotor (`РОТОР`)
Inspired by satisfying "ball escaping rotating rings" simulations — powered by analytical circular-arc & rounded-lip collision physics and glowing shard dissolution.
- **Click-to-drop start:** every level begins with **all rings paused** and no ball on screen. Click **LMB** to drop a ball with **zero initial velocity** (no directional aiming) — the rings immediately begin spinning and gravity takes over.
- **6 built-in levels:**
  1. **`ВЫХОД` (Escape)** — a single large rotating ring that shatters into glowing shards once the ball slips through the gap into open space.
  2. **`200 КРУГОВ` (200 Rings)** — a dense 200-ring pink spiral with a live countdown in the center; every escaped ring shatters while outer rings smoothly contract inward.
  3. **`РЕВЕРС` (Reverse)** — every time you pass a ring, **all remaining rings instantly reverse their rotation direction**.
  4. **`ВСТРЕЧНЫЕ` (Counter-Spin)** — even and odd rings spin in opposite directions, forming shifting scissor gates.
  5. **`РАЗГОН` (Overdrive)** — starts calm and steadily accelerates rotation speed with every shattered ring.
  6. **`ВИХРЬ` (Vortex)** — 36 concentric rings, each with its own harmonic angular velocity and spin direction.

---

## Controls & Global Settings

### Mouse & Shortcuts
| Action | Control |
| :--- | :--- |
| Launch ball (`Levels` / `Sandbox`) | Hold **LMB**, drag direction, release |
| Drop zero-velocity ball (`Rotor`) | Click **LMB** |
| Relocate ray source (`Wavefront`) | Click **LMB** |
| Remove ball / erase object | **RMB** (or `Ctrl+Z` to undo last ball / edit) |
| Ball size | Toolbar **`R`** slider, mouse wheel, or `+` / `-` |
| Pause / Resume (or Test in Sandbox) | `Space` |
| Restart current level / wave | `R` |
| Toggle Gravity | `G` |
| Return to Levels (or stop Sandbox test) | `Esc` |

### Sidebar Toggles (Live Across Modes)
- **Halos (`ОРЕОЛЫ`)** — soft neon ring around balls.
- **Bloom (`СВЕЧЕНИЕ`)** — glow pass on bright balls, walls, and shards.
- **Gravity (`ГРАВИТАЦИЯ`)** — per-mode gravity toggle.
- **Trails (`ТРЕКИ`)** — fading motion trails for balls and rays.
- **Antialiasing (`СГЛАЖИВАНИЕ`)** — Wu subpixel line smoothing (turn off for ultra-high ray counts).
- **Sim Speed (`СКОРОСТЬ`)** — real-time `0.01×` to `5.0×` time-scale slider (click the number in any slider popup to type an exact value).

---

## Architecture & Performance Highlights

- **Analytical & Spatial-Grid Physics (`Core/Physics.cs`):** Adaptive sub-stepping prevents tunneling even for `r = 2 px` rays; walls are indexed in an allocation-free uniform spatial grid using squared-distance culling, while circles and rotating arcs use exact analytical normals and tangential lip velocities.
- **Zero-GDI Additive Ray Renderer (`UI/GameCanvas.cs`):** Wavefront rays and O(1) ring-buffer trails rasterize directly into `32bpp` pixel memory with Wu antialiasing and additive blending.
- **Single-Lock Parallel CPU Post-FX (`UI/Fx.cs`):** Direct `4×4` bright-pass downsample, cache-sequential separable box blur, radial chromatic aberration, vignette, and film grain in a single `LockBits` pass.

```text
Program.cs              Entry point + --selftest / --bench / --shots CLI modes
SelfTest.cs             Automated numerical self-tests (physics, JSON, GDI, FX, Wave, Rotor)
Core/
  Vec2.cs               2D vector math
  LevelData.cs          Level data model, fast deep clone & System.Text.Json persistence
  Physics.cs            Sub-stepped simulation, spatial grid, analytical circles & rotating arcs
  BuiltinLevels.cs      Built-in geometric arenas
UI/
  Theme.cs              Neon-dark palette, vector IconPainter, NeoButton, adaptive Toolbar & popups
  Fx.cs                 Parallel CPU post-processing pipeline (bloom, aberration, vignette, grain)
  GameCanvas.cs         1280×720 offscreen frame buffer, input, shard FX & additive wave renderer
  MainForm.cs           Single-window shell, DPI-aware sidebar & global live settings
  PlayView.cs           Levels mode
  EditorView.cs         Sandbox & level editor
  WaveView.cs           Wavefront in mathematical billiards (24 figures)
  RotorView.cs          Rotor mode (6 rotating-ring levels, click-to-drop start & shard physics)
```
