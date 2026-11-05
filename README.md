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
Pure geometric ricochet arenas (14 built-in arenas + unlimited custom saves), rotating-obstacle challenges, and custom mathematical billiards.
- **Drag & release LMB** to launch a ball in any direction (or click to drop / relocate a wave source in custom Rotor & Wavefront levels).
- **Slow-Motion & Neon Confetti Celebration** — hitting a green goal triggers dramatic `0.08×` bullet-time slow-motion with expanding shockwaves and geometric neon confetti, with zero intrusive win-banner overlays so you can keep launching balls uninterrupted.
- **One-Click Transfer to Editor (`В РЕДАКТОР`)** — bring a copy of any built-in or custom level straight into the Sandbox Editor to inspect, tweak, or save as your own variant.
- Loads both built-in arenas and **every custom level saved inside the game** in `levels/*.json`.

### 2. Universal Sandbox & Level Editor (`РЕДАКТОР`)
A sleek Glass-Neon studio capable of creating **every level type and gameplay mechanic in the entire game** — Classic & Free-play arenas, 200-ring Rotor spirals, rotating obstacle chambers, and diffracting Wavefront billiards — and testing them instantly (`Space` or **`► ТЕСТ`**):
- **In-Game Unlimited Save Manager (`СОХРАНЕНИЯ` / `Ctrl+O` & `Ctrl+S`):**
  - Save directly inside the game with **`Ctrl+S`** or **`+ СОХРАНИТЬ НОВЫМ`** — create as many save slots and snapshots as you like with live vector mini-previews, timestamps, instant loading, overwriting, and one-click deletion without ever opening Windows Explorer.
  - Import external `.json` save files (**`ДОБАВИТЬ ФАЙЛОМ...`**) or export any creation to `.json` (**`ВЫГРУЗИТЬ .JSON`**) to share.
- **16 Vector Authoring Tools:**
  - **Edit:** Select / Move (`V`, drag any object, `Ctrl+D` duplicate, `Del` delete) and Eraser (`E`).
  - **Walls:** Freehand brush (`W`, open or closed contour), Line segment (`L`), Rectangle (`R`), Ellipse (`O`), **Analytical Exact Circle** (`C`), **Regular N-gon** (`P`, 3–24 sides), **N-Point Star** (`S`, adjustable inner radius), and **Gapped Rotor Ring** (`A`, center-snapping circular arc with configurable gap width, angle, thickness, and per-ring spin multiplier).
  - **Goals:** Freehand (`F`), Rectangle (`T`), Ellipse (`Y`), and Regular N-gon (`G`) goal zones — plus one-click Wall ↔ Goal conversion.
  - **Objects:** Pre-placed Balls (`B`, custom color/spectrum, radius, initial velocity vector or `v = 0`) and **Wavefront Emitter** (`U`).
- **One-Click Concentric Spiral Generator (`СПИРАЛЬ` / `ГЕНЕРАТОР КОЛЕЦ`):** generate 1 to 200 concentric gapped Rotor rings with custom inner radius, ring spacing, gap angle, spiral twist, and spin pattern (**Sync**, **Counter-Spin**, or **Harmonic Vortex**).
- **Template Gallery (`ШАБЛОНЫ`) & Cross-Mode Import (`В РЕДАКТОР`):** open and remix any of the **14 Classic & Physics Levels**, **10 Rotor Stages**, **32 Mathematical Billiards**, or your own in-game saves with full `Ctrl+Z` / `Ctrl+Y` undo-redo history.
- **Contextual Dual-Tab Inspector (`СВОЙСТВА`: `СЦЕНА` / `ОБЪЕКТ`):** configure mode presets (`КЛАССИКА` / `РОТОР` / `ВОЛНА`), world border, gravity & `G` scale, elastic bounce, global spin speed, Rotor shattering, `v = 0` click-to-drop start, inward ring contraction, smart gap-freeze, reverse-on-break, overdrive acceleration-on-break, continuous Keller-diffracting wavefronts vs. discrete ray fans, player hints, and per-object properties.

### 3. Wavefront — Mathematical Billiards & Diffraction (`ВОЛНОВОЙ ФРОНТ`)
Emit a radial wavefront inside **32 mathematical billiard tables and optical cavities** and watch wavefront evolution, caustics, diffraction, and chaos unfold in real time:
- **Realistic Continuous Wave (`ВОЛНА`, enabled by default):** dynamically refines stretched segments with 4th-order Hermite curvature interpolation, merges compressed collinear segments so long-running waves stay crisp indefinitely, and emits secondary cylindrical **Keller diffraction waves** at sharp corners and barrier tips! Toggle **`ВОЛНА`** off anytime to unlock the **`ЛУЧИ`** slider (`10` to `100,000` discrete billiard particles).
- **Permanent Coverage Trace (`СЛЕД` / `T`):** leaves a non-fading luminous trace behind waves or particles, starkly revealing acoustic/optical **shadow zones** and phase-space islands where the wavefront has never reached.
- **32 Billiards & Optical Arenas:** Circle, Ellipse, Bunimovich Stadium, Bunimovich Mushroom, Sinai Billiard, Young's Double-Slit Optics, Cardioid, Focal Mirrors, Trefoil, Shadow Labyrinth, Gear Cavity, Superellipse, Heart, Crescent, Star, Dumbbell, Cross, Staircase, and more — plus one-click **`В РЕДАКТОР`** export.

### 4. Rotor (`РОТОР`)
Inspired by satisfying "ball escaping rotating rings" simulations — powered by Velocity-Verlet energy-conserving bounces, analytical circular-arc physics, and glowing shard dissolution.
- **Click-to-drop start & smart gap freeze:** every level begins with **all rings paused** and no ball on screen. Click **LMB** to drop a ball with **zero initial velocity** (no directional aiming) — the rings immediately begin spinning, automatically **pause rotation while the ball passes through any ring's empty cutout**, and resume smoothly once cleared.
- **10 built-in levels (all transferable via `В РЕДАКТОР`):**
  1. **`ВЫХОД` (Escape)** — a single spacious rotating ring that shatters into glowing shards once the ball slips through the gap.
  2. **`200 КРУГОВ` (200 Rings)** — a dense 200-ring pink spiral with a live countdown in the center.
  3. **`РЕВЕРС` (Reverse)** — every shattered ring instantly reverses the rotation direction of all remaining rings.
  4. **`ВСТРЕЧНЫЕ` (Counter-Spin)** — even and odd rings spin in opposite directions, forming shifting scissor gates.
  5. **`РАЗГОН` (Overdrive)** — starts calm and steadily accelerates rotation speed with every shattered ring.
  6. **`ВИХРЬ` (Vortex)** — 36 concentric rings, each with its own harmonic angular velocity and spin direction.
  7. **`50 КРУГОВ · ПРОСТОР` (50 Rings · Wide Open)** — 50 concentric rings surrounding a huge open central chamber just like Level 1.
  8. **`УЗКИЙ ШЛЮЗ` (Narrow Airlock)** — heavy thick rings with tight `50°` airlock cutouts.
  9. **`МАЯТНИК` (Pendulum)** — counter-rotating layers combined with direction reversal and progressive speed-up on every break.
  10. **`ОРБИТА` (Orbit)** — a spacious 24-ring chamber containing three orbiting hexagonal moons that deflect the ball toward the gaps.

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

### Sidebar Toggles & UI Features (Live Across Modes)
- **Floating Overlay Menu (`☰ МЕНЮ`)** — opens directly on top of the arena and top header bar without shifting or resizing a single button or game element; dismiss via `|<`, `Esc`, or clicking anywhere outside the drawer.
- **Visual Level Picker (`⊞ ... ▾`)** — click the level/figure title pill in the top capsule bar to open an interactive card grid with live vector mini-previews of every arena, billiard table, or rotor stage.
- **Theme Presets (`ТЕМА`)** — hot-switch between **4 neon color atmospheres** on the fly: **Cyber Cyan (`КИБЕР`)**, **Synthwave (`СИНТВЕЙВ`)**, **AMOLED Minimal (`АМОЛЕД`)**, and **Warm Amber (`ЯНТАРЬ`)**.
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
SelfTest.cs             Automated numerical & UI layout self-tests (physics, JSON, GDI, FX, Wave, Rotor, Editor)
Core/
  Vec2.cs               2D vector math
  LevelData.cs          Universal level model, shape generators, deep clone & JSON persistence
  Physics.cs            Velocity-Verlet simulation, spatial grid, analytical circles & rotating arcs
  WaveFrontSim.cs       Continuous adaptive wavefront refinement & Keller vertex diffraction engine
  BuiltinLevels.cs      Built-in geometric arenas
UI/
  Theme.cs              Neon-dark palettes, vector IconPainter, NeoButton, NeoTextBox & gallery popups
  Fx.cs                 Parallel CPU post-processing pipeline (bloom, aberration, vignette, grain)
  GameCanvas.cs         1280×720 offscreen frame buffer, 16 editor tools, shard FX & wave renderer
  LevelRuntime.cs       Unified gameplay controller (Classic, Rotor & Wavefront) for Levels & Editor Test
  MainForm.cs           Single-window shell, DPI-aware overlay sidebar & global live settings
  PlayView.cs           Levels mode (plays built-in & universal custom JSON levels)
  EditorView.cs         Universal Glass-Neon level editor, spiral generator & dual-tab inspector
  WaveView.cs           Wavefront in mathematical billiards (24 figures)
  RotorView.cs          Rotor mode (6 rotating-ring levels, click-to-drop start & shard physics)
```
