# Brand Mark — `Minos.NET`

**Product:** `Minos.NET` · **Mark type:** `geometric` · **Generated:** `2026-10-09` · **Design system:** `absent — no docs/design/MASTER.md in the repository`

> The record for one mark, saved at `docs/design/LOGO.md`. `logo-concept` fills it in; `logo-review` audits against it. Every value the skill's reference files ask to be written down has a slot below, and an empty slot is a finding rather than a silence.
>
> Closed out at Step 7 items 1–3 in a scratch mirror of the repository layout, then copied into the repository by phase 6.1 Task 7, which exported the raster set, wired the NuGet icon and the README logo (Step 8), re-ran the structural self-verification against the repository copy and committed the set.

## Recording conventions

Tokens per the template: a value · `n/a — <why>` · `declined — <why>` · `UNRUN — <what would decide it>`. Citations name a reference file and a section. `grep UNRUN` over this file lists everything this record admits it does not know.

## Concept & rationale

The mark is a coil: one stroke wound one and a half times around itself, built from semicircles that alternate between two centres and widen by a fixed step every half-turn. It comes from Dante's Minos, who sentences each soul by coiling his tail around himself as many times as the circle it is sent to — the verdict is a count. For a library that turns a state and a few typed questions into verdicts, the mark is the act of judging itself, not the furniture of a courtroom.

| Field | Record |
|---|---|
| The brief, in one line | Product: "Minos.NET — a .NET library, being renamed from ZeroAlloc.Jev; repo MarcelRoozekrans/Minos.NET. Greek-themed sibling repos: Daedalus.NET, Thalos.NET. Named after Minos, king of Crete and judge of the dead." · Q1, the string set in type: "Minos.NET" (maintainer's choice from "Minos", "Minos.NET", "MINOS") · Q2, what it does: "A typed, provider-neutral decision client for .NET: hand it a state and a few typed questions, and a decision model returns a verdict for each, with a calibrated confidence." — proposed by the agent from the repository README, confirmed unchanged by the maintainer · Q3, mark type: "Geometric" — the maintainer's explicit choice, not "you choose" · Q4, where it must survive: favicon (16–32 px), NuGet / app icon (128 px tile), dark UI; not one-colour print or embroidery, not large format · Q5, must-avoid: "no scales of justice; no Greek-temple clichés — no columns, laurels, meanders or helmets" (the maintainer chose "Both of the above") · Colour: direction "Bronze / Cretan ochre" — "a warm Minoan bronze-ochre; ties to Crete and Talos's bronze, distinct from the blue .NET crowd" · Provenance: Steps 0, 1 and the Step 5 choice were answered by the maintainer through the controlling session and relayed verbatim to this run. |
| Why this candidate won | The maintainer chose B ("The coil"), no blend. On the sheet it was the only candidate whose story is carried by its form — the turns *are* the verdict — and it raised no collision with the sibling repos: A read as the Greek letter Θ (Thalos's initial), C as the generic ∴ glyph. B's one weakness, soft rendering at 16–24 px (C1/C2), is answered by the favicon redraw below. |
| Candidates rejected | A — Theta nigrum: reads as the letter Θ and the ⊖ glyph, a letter not in the name and the Greek initial of sibling Thalos.NET (C8). · C — The casting vote: reads as the generic ∴ "therefore" glyph and three-dot icons, lowest distinctiveness; centroid centring placed it visibly high (C5, C8). |
| Keywords present from anti-slop.md pattern 6's list | none — the scan over the whole brief above finds none of the eight |
| Type chosen because | Geometric was the maintainer's explicit Q3 answer, and also what SKILL.md § "You choose" is derived, not chosen reaches: Q1's string is long (8 characters ignoring the stop) and Q4 is size-hostile (favicon, app icon), so a mark-alone variant is mandatory, and the initials are not distinctive enough to carry a monogram — mark-types.md § Choosing the type, row 1. |
| Mark–name relationship | the mark does not reference the name — the derivation produced a mandatory mark-alone variant (geometric, or monogram had the initials been distinctive); the maintainer chose geometric explicitly, so no letter of "Minos.NET" is drawn. The name is carried by the wordmark and the two lockups, set in type. |

### Step 0 records

| Field | Record |
|---|---|
| Existing-mark guard | nothing at `assets/brand/`, `public/brand/`, `wwwroot/brand/`, `public/favicon.*`, `static/logo.*` → `logo-concept`, full flow. Noted, not a guard hit: `assets/icon.svg` / `assets/icon.png` — the inherited ZeroAlloc organisation icon (white "Z" on an indigo `linearGradient` tile), which this set replaces; Step 8 later deleted it (see Project files replaced). |
| Asset directory | `assets/brand/` — a .NET library, not a web app (SKILL.md Step 0 item 5) |
| Typeface (Step 0 item 8) | Bahnschrift, chosen from the installed faces; resolves on the render machine (see Production handoff → Typeface) |

### Question 4's consequences, taken before drawing

| Context | Consequence |
|---|---|
| favicon | reproduction.md § The counter floor, in aggregate, condition 3 unmet → every counter built to the target; `logo-favicon.svg` ships as a redraw |
| dark UI | condition 2 unmet → counters at `max(1.25 w, 32) + r · w`, r = 3%: 32.72 at the master's w 24, 40.96 at the favicon's w 32 |
| app icon | square-tile reading, route **composite at export** — no drawn container; correction 6 `n/a` |
| one-colour print / embroidery, large format | not named → no print rows, correction 4 judged at 64 / 256 only |

### Joint-satisfiability check (before Step 2)

`max(1.25 w, 32) + 0.03 w` = 32.48 / 32.72 / 40.96 at w 16 / 24 / 32. No letterform is drawn, so the two-stem width line does not bind; the space was not collapsed.

### Step 2 — the three directions, as written before drawing

Set aside before drawing: the labyrinth (a meander in plan, banned by Q5, and the dictionary-illustration reflex of anti-slop.md patterns 6 and 8); the labrys and the bull (pictorial — refusal gate); a gauge or open ring (pattern 1); nodes and links (pattern 5); a 2 × 2 grid (a recognisable operating-system mark).

| | Idea | Seed | Gives up |
|---|---|---|---|
| A — Theta nigrum | Θ, the verdict sign marked against the condemned: a ring cut by one bar | outer circle r 112 at (128,128) | reads as the letter Θ; crowded ring-and-bar family |
| **B — The coil (chosen)** | Dante's Minos coils his tail once per circle of the sentence — the verdict is a count | semicircle, centreline r 36, about (128,144), w 24 | spirals are a common swirl, labyrinth-adjacent to some readers; soft at 16 px |
| C — The casting vote | three judges, Minos with the deciding vote; also ∴ | equilateral triangle side 128, centroid (128,128), discs r 32 | weakest distinctiveness |

## Construction

### The rule, and the derivation chain

**Seed:** a semicircle of centreline radius 36 about A (128, 144), declared weight 24, upper half.

**Rule:** semicircles alternate between A (128, 144) and B (96, 144), 32 apart, each half-turn adding 32 to the centreline radius — applied three times (r 36, 68, 100), 1.5 turns. (Stated although the type is geometric, because the spiral is one.)

| Element | Derived from | Arithmetic | Result |
|---|---|---|---|
| Turn 1, upper, about A | seed | edges r ± 12; inner ry 48 − 23.04 = 24.96 (correction 4, outer held) | outer x 80 / 176, top y 96; inner top y 119.04 |
| Turn 2, lower, about B | turn 1's right end; B = A − 32 | r 36 + 32 = 68 | outer x 16 / 176, bottom y 224; inner bottom y 200.96 |
| Turn 3, upper, about A | turn 2's left end | r 68 + 32 = 100 | outer x 16 / 240, top y 32; inner top y 55.04 |
| Vertical placement | ink extents | yc = 128 + (112 − 80) / 2 = 144 | ink y 32 … 224, centred on 128 |
| Pitch and channel | the rule | pitch 2 × 32 = 64; channel 64 − 24 = 40 | channel 40 at y 144 left and right |
| End caps | butt, on y 144 | | x 80–104 (eye end), 216–240 (open end) |

### Optical exceptions

| Variant | Flag, verbatim | Distinct reason |
|---|---|---|
| `logo-mark.svg`, `logo-mono-black.svg`, `logo-mono-white.svg`, `logo-full.svg` | `OPTICAL: inner ry 24.96 / 56.96 / 88.96 (edges y 119.04 / 200.96 / 55.04) · horizontal strokes read heavier · 24 x 0.96 = 23.04 at every horizontal tangent; outer edges held on the grid, the inner (free) edge moves` | 1 |
| `logo-stacked.svg` | the same flag; x coordinates + 128 | 1 |
| `logo-favicon.svg` | none — every painted edge on a multiple of 16 | — |
| `logo-wordmark.svg` | none | — |

**Surviving flags:** 1 distinct reason against construction.md § Precedence's ceiling of six. **Flags in a separate dark master:** `n/a — no fork`.

### The nine corrections

| # | Correction | Status | How it was answered |
|---|---|---|---|
| 1 | Overshoot | n/a — no flat form shares an alignment edge with the curves | |
| 2 | Size matching | n/a — one form | |
| 3 | Apex centring | n/a — no apex | |
| 4 | Horizontal strokes | 23.04 | 24 × 0.96 at every horizontal tangent; outer edge held on the grid, inner edge moved (precedence rule 2); 24 at the vertical tangents |
| 4 (favicon) | Horizontal strokes | declined — correction 4 would take the favicon's 32 to 30.72 at its horizontal tangents, 1.92 px against 2.00 px at 16 px; the correction is below the resolution of every size this variant renders at |
| 5 | Perpendicular width | n/a — no diagonal outline | |
| 6 | Container | n/a — no drawn container; the app-icon tile is composited at export | |
| 7 | Joins | butt end caps at 90°; no ink join anywhere | |
| 8 | Rotation | n/a | |
| 9 | Sidebearings | n/a — no letterform drawn; the type is a `text` run | |

### Stroke and counters

| Field | Record | Source |
|---|---|---|
| Declared weight | 24 (master); 32 (favicon, reproduction.md § The favicon's own spec) | construction.md § Stroke discipline |
| Construction | filled outline, one contour | |
| Narrowest ink | 23.04 (horizontal tangents) | |
| Widest ink | 24 | |
| Thick–thin axis | n/a — 1.04×, under the ~2× threshold | |
| Counter count at 256 | 0 enclosed; 1 open channel (the eye, the turns and the mouth are one connected negative space) | |

| Counter | Family | Narrowest width | Clears | Source |
|---|---|---|---|---|
| Channel, left and right at y 144 | parallel straight | 40 | target 32.72 | pitch 64 − w 24 |
| Channel, top | concentric round about A | 40.96 | target | 88.96 − 48 |
| Eye | parallel straight | 48 | target | 104 … 152 |

### Silhouette

| Field | Record |
|---|---|
| Ink area, computed analytically | 15127.90 (three half-annuli: 2678.17 + 5042.63 + 7407.12) |
| Convex hull area | 31404.83 |
| Ratio | 0.482 |
| Deliberate primitive? | n/a — 0.482 is under reproduction.md § Mono collapse's M4 gate |

### Derivation answers for the anti-slop patterns

| Pattern | Applies? | Test A | Test B |
|---|---|---|---|
| 1 — circle with a gap | n/a — no two arcs share a radius about one centre; no interrupted ring | | |
| 9 — hexagon container | n/a — no hexagon | | |
| 6 — the lens | n/a — no lens, and no keyword in the brief | | n/a — Test B does not apply to this pattern |
| 8 — the loop | n/a — no equal-radius opposite-sweep pair, no self-intersecting subpath | | n/a — Test B does not apply to this pattern |

### Type-specific records

| Field | Applies to | Record |
|---|---|---|
| Base sidebearing `s`, container clearance | monogram | n/a — geometric |
| Wordmark model (for `logo-wordmark.svg`) | wordmark variant | single text run |
| Tracking | wordmark variant | letter-spacing = font-size × 0.05 → 37.45 × 0.05 = 1.87 |
| Fit | wordmark variant | font-size 37.45, measured off the ink edge. Render 1 (37.81): harness ink edge 225.87, fit 0.9904. Render 2 (37.45): harness ink edge 222.14, fit 1.0098 — the harness's trailing-step detection flipped between the two renders (a tracking step is 1.87 units). Settled by rasterising the file at 2048 px: true ink x 35.13 … 222.75, so a trailing step is applied and the fitted run's ink ends 1.25 inside 224. Not iterated further (Step 7 verify-once). |
| Row asymmetry | wordmark variant | fitting the right ink edge does not put the left ink edge on 32: the anchor is an origin and the M's ink starts 3.13 inside it (35.13) |
| The one custom detail | wordmark variant | declined — at the fitted size the type's stems are ~3.9 units (φ_ink 0.1445 × cap 26.92); a detail at the mark's declared weight 24 would be six times the type's stem. The coil carries the detail role in both lockups. |
| Rule applications | abstract | n/a — geometric (the spiral's own rule is applied 3 times; recorded above) |

### Step 4 — render and critique of the three candidates

Render 1 of 2: Playwright MCP, Chromium, viewport 1360, served over loopback (`file:` blocked in this MCP build — the template's documented fallback), `data-sheet-ready` awaited, full-page and `#readout` shots read back. Readout clean for all three (live area, `currentColor` only, M3 Δmax 0/255, no warn or bad rows). Findings: A — C8 (reads as Θ / ⊖). B — C1/C2 (soft at 16–24 px), C8 (a swirl, recalls the Debian swirl). C — C5 (centroid centring sits it visibly high at 256), C8 (∴, three-dot icons). No binary failures; no withdrawals; no re-render needed. C10 `n/a — geometric` for all three.

## Variants

| File | Intended use | `viewBox` | Aspect | Minimum size | Source of the minimum |
|---|---|---|---|---|---|
| `logo-mark.svg` | the mark alone, ≥ 64 px; app-icon tile at export (composited) | `0 0 256 256` | 1:1 | 13 px by formula; 64 px in use | formula: reproduction.md § Minimum sizes, `R = 256 × 2 / 40` (narrowest counter 40) = 12.8 px; in use, everything ≤ 48 px routes to `logo-favicon.svg` because the Step 4 sheet showed the master soft at 16–24 px (C1/C2) — 64 px is the smallest sheet column above that routing |
| `logo-full.svg` | horizontal lockup: README header, docs site header | `0 0 992 256` | 3.875:1 | 248 px wide | derived — see Lockup measurements |
| `logo-stacked.svg` | stacked lockup: square-ish placements, NuGet gallery-style cards | `0 0 512 368` | 1.39:1 | 128 px wide | derived — see Lockup measurements |
| `logo-wordmark.svg` | the name alone, and the φ source | `0 0 256 256` | 1:1 | 153 px (square) | measured: cap-height minimum 16 px (φ, below) ÷ cap 26.92 units × 256 = 152.2 → 153 px |
| `logo-mono-black.svg` | one-value use on light grounds | `0 0 256 256` | 1:1 | inherits logo-mark's | the `currentColor` binding — same geometry, `color="#000000"` |
| `logo-mono-white.svg` | one-value use on dark grounds | `0 0 256 256` | 1:1 | inherits; ships ≤ 355 px | same, `color="#ffffff"`; D1 state 1 below 356 px |
| `logo-favicon.svg` | favicon and every raster ≤ 48 px | `0 0 256 256` | 1:1 | 16 px | reproduction.md § The favicon redraw: counter 64, `256 × 2 / 64` = 8 px; ink 32, `256 / 32` = 8 px; specified for 16 px |

### Lockup measurements

| Field | Record |
|---|---|
| `k` — mark's rendered height ÷ lockup cap height | full: 256 / 96 = 2.67 · stacked: 256 / 64 = 4 |
| Cap-height minimum from the mark side | by formula: 12.8 / 2.67 = 4.8 px (full), 3.2 px (stacked); with the mark's in-use minimum of 64 px: **24 px** (full), **16 px** (stacked) |
| `φ_ink` | 37 / 256 = **0.1445** (narrowest ink: the s bowl, 37 px at a 256 px cap height; stems 39–42 px) |
| `φ_ctr` | 33 / 256 = **0.1289** (narrowest counter: the s's upper counter, 33 px). M's mid-height notch measures 18 px; it is an aperture tapering to its vertex and closes in every face at small sizes, so it is not taken as the counter. |
| How φ was measured | in the sheet's Chromium, on the declared face at 600, glyphs rendered at a 256 px cap height (capPx 255.88) and scanned along fixed rows and columns — **not** on the wordmark file's own 256 px render, where the cap height is 26.9 px and a 37-px-class stem would be ~4 px, too coarse to measure. Same face, same engine; the ratios are face properties. |
| Cap-height minimum from the type side | max(1 / 0.1445, 2 / 0.1289) = max(6.92, 15.52) → round up → **16 px** |
| Lockup `viewBox` width ÷ cap height inside the lockup | full: 992 / 96 = 10.33 · stacked: 512 / 64 = 8 |
| Lockup width minimum | full: max(24, 16) × 10.33 = **248 px** · stacked: max(16, 16) × 8 = **128 px** |
| Lockup's own size status | derived from its components — the mark side from `k`, the type side from the wordmark's `φ`. The lockup is not size-tested on the contact sheet. |
| Composition check (render) | full: type ink 298.7 … 966.24, cap 80 … 176 centred on the mark's 128, right margin 25.76; stacked: type ink 33.5 … 478.48, centred on 256 (255.99), 48 below the mark's ink. Read back from a render of both files: composition holds. |
| Typeface the `φ` values were measured against | Bahnschrift, 600 |

### Favicon

| Feature dropped | Why, in reproduction terms |
|---|---|
| the master's innermost half-turn (centreline r 36) | at 16 px its ink is 24 units = 1.5 px, its eye 48 = 3 px and the channel beside it 40 = 2.5 px — the Step 4 sheet showed exactly this greying (C1/C2). At the favicon's mandated w 32 with the dark-tab target 40.96, the pitch must be ≥ 72.96, and on the 16-unit grid the step is 48: a 1.5-turn spiral then needs an outer centreline radius of 48 + 2 × 48 = 144, plus 16, against a live half-width of 112. Only one turn fits. |
| the correction-4 thinning | declined — recorded under The nine corrections |

| Field | Record |
|---|---|
| Construction | lower semicircle, centreline r 48 about B (80,152), then upper r 96 about A (128,152); centres 48 apart; w 32; channel 96 − 32 = 64 (4 px at 16 px); eye 64 |
| Nodes — favicon / master | 6 / 8 (F2 pass) |
| Counters — favicon / master | 1 / 1 — one open channel each; F3 passes on its `counters(master) = 1` clause |
| Subpaths shared with the master | none (F1 pass) |
| Declared weight | 32 |
| Ink reaches the live-area bounds on its longer axis | x 16 … 240, graded on the geometry box — filled, the two boxes coincide |
| Silhouette | ink 14476.46 / hull 28456.40 = 0.509 |
| Uniform weight | recorded as a `declined` correction in The nine corrections |
| Favicon critique pass | render 1 of 2. Favicon in slot A, master in slot B. Readout: 6 nodes, reuse 0.53, M3 Δmax 0/255, ink x 16 … 240, y 40 … 216, no warn or bad rows. **C1** pass — at 16 px a crisp 2 px stroke, eye and channel open on both grounds; the master's softness is fixed. **C2** pass — holds at 20 / 24. **C3** pass — mono identical. **C9** pass — no rows. **C10** n/a — geometric. Judgement finding: with one turn it reads as a heavy curl or hook rather than an explicit spiral; the arithmetic above admits no permitted fix, so the second render was not spent. |

### Raster set

| File | px | Rasterised from | Intended slot | Status |
|---|---|---|---|---|
| `favicon-16.png` | 16 | bronze-resolved copy of `logo-favicon.svg` (`currentColor` → `#A86B24`) | `<link rel="icon" sizes="16x16">` | written — 16 × 16, corners transparent, ink `#A86B24` |
| `favicon-32.png` | 32 | bronze-resolved copy of `logo-favicon.svg` (`currentColor` → `#A86B24`) | `<link rel="icon" sizes="32x32">` | written — 32 × 32, corners transparent, ink `#A86B24` |
| `favicon-48.png` | 48 | bronze-resolved copy of `logo-favicon.svg` (`currentColor` → `#A86B24`) | legacy browser tab | written — 48 × 48, corners transparent, ink `#A86B24` |
| `favicon.ico` | 16/32/48 | bronze-resolved copy of `logo-favicon.svg` (`currentColor` → `#A86B24`) | site root | written — three PNG frames, 16 / 32 / 48 |
| `apple-touch-icon.png` | 180 | bronze-resolved copy of `logo-mark.svg` (`currentColor` → `#A86B24`) | iOS home screen | written — 180 × 180, corners transparent, ink `#A86B24` |
| `icon-128.png` | 128 | bronze-resolved copy of `logo-mark.svg` (`currentColor` → `#A86B24`) | **NuGet package icon**, packed as `icon.png` (NuGet recommends 128 × 128) | written — 128 × 128, corners transparent, ink `#A86B24`; the exporter's own `magick` argv, density `max(72, ⌈128 · 2 · 72 / 256⌉)` = 72 |
| `icon-192.png` | 192 | bronze-resolved copy of `logo-mark.svg` (`currentColor` → `#A86B24`) | web app manifest; **the README logo** — a bronze PNG reads on GitHub light and dark and on nuget.org, which renders no raw HTML | written — 192 × 192, corners transparent, ink `#A86B24` |
| `icon-512.png` | 512 | bronze-resolved copy of `logo-mark.svg` (`currentColor` → `#A86B24`) | web app manifest, splash | written — 512 × 512, corners transparent, ink `#A86B24` |
| `icon-1024.png` | 1024 | bronze-resolved copy of `logo-mark.svg` (`currentColor` → `#A86B24`) | app store listing | written — 1024 × 1024, corners transparent, ink `#A86B24` |

Why bronze-resolved copies: the masters paint in `currentColor`, which has no inherited `color` inside a rasteriser, an `<img>`, a NuGet gallery tile or a home-screen icon, so it renders black (see Misuse). Every raster therefore comes from a temporary copy of its source with `currentColor` replaced by the brand value `#A86B24`; the committed SVGs keep `currentColor`. `icon-128.png` is outside the exporter's fixed list and was produced with the exporter's own `magick` argv. Each PNG was read back: its size, transparent corners (`srgba(0,0,0,0)` at top-left and bottom-right) and ink `#A86B24` sampled on the mark (the 16 px favicon's sample is an edge pixel, `#A86B24` at alpha 0.9).

| Field | Record |
|---|---|
| Rasteriser used | ImageMagick 7.1.2-31 Q16-HDRI (`magick`), through `export-raster.mjs` (logo-design 1.22.1), exit 0 |
| Sizes at or below 48 px come from the favicon redraw | yes — the exporter's fixed routing; never the master |
| Files written | 8 by the exporter, plus `icon-128.png` with the same argv — 9 |

### Print minimums

`n/a — Q4 named no print or embroidery process`

## Colour

### Binding

`MASTER.md` is absent. The maintainer took the second legal answer — a direction to propose against ("Bronze / Cretan ochre"). **Proposed against the maintainer's direction, confirmed** as proposed: one value for both grounds. The masters ship in `currentColor` (construction.md § Colour binding); this is the value `color` resolves to at the call site. `ui-design-system` was suggested; it did not block.

| Role | Value | Provenance |
|---|---|---|
| Mark on light | `#A86B24` (Minoan bronze) | proposed, confirmed by the maintainer |
| Mark on dark | `#A86B24` — the same value | confirmed: one colour for both grounds |
| Accent | n/a — single-value mark | |
| Background pairings tested | `#ffffff` · `#0d1117` (GitHub dark) | |

The alternative dark-only value proposed alongside (`#D4A04A`, 8.04:1 on `#0d1117`, 2.35:1 on white) was not taken.

### Contrast

L(`#A86B24`) = 0.1897 · L(`#ffffff`) = 1 · L(`#0d1117`) = 0.00548.

| Variant | Against | Ratio | Clears 3:1 (WCAG 2.2 SC 1.4.11) |
|---|---|---|---|
| every variant, `color: #A86B24` | `#ffffff` | 1.05 / 0.2397 = **4.38** | yes |
| every variant, `color: #A86B24` | `#0d1117` | 0.2397 / 0.05548 = **4.32** | yes |
| `logo-mono-black.svg` | `#ffffff` | 21.00 | yes |
| `logo-mono-white.svg` | `#0d1117` | 18.93 | yes |

### One-colour print and mono

| Field | Record |
|---|---|
| M1 | yes — every paint in all seven files is `fill="currentColor"` (9 occurrences, no literal colour, alpha, blend or gradient) |
| M2 | n/a — the mark and favicon are one path each; the lockups pair one path with one `text` whose bounding boxes do not nest |
| M3 | 0/255 on `logo-mark.svg` (15728 ink px) and `logo-favicon.svg` (14902 ink px), from the verification render's readout. `logo-wordmark.svg`: UNRUN — type only, no drawn geometry; whole-file Δmax 80/255 on 1055 px is glyph rasterisation, which reproduction.md § Mono collapse records unrun |
| M4 | recorded in Construction § Silhouette (0.482; favicon 0.509) |
| One-colour print | n/a — not a named context |

### Dark inversion

| Field | Record | Source |
|---|---|---|
| `r` | default — the rate pinned in reproduction.md § r is pinned (3%) | |
| `r · w` | master 0.72 (w 24); favicon 0.96 (w 32) | |
| Fork threshold | master `256 / 0.72` = 355.56 → **356 px**; favicon `256 / 0.96` = 266.67 → 267 px | reproduction.md § When the dark variant forks |
| **D1** | **state 1 — no fork, below the threshold.** `logo-mono-white` is byte-identical to `logo-mono-black` apart from the resolved `color`, and both differ from `logo-mark` only in it (diffed). The dark variant is specified for ≤ 355 px; the favicon renders ≤ 48 px against its 267 px threshold. | |
| **D2** | master channel 40 − 0.72 = 39.28, top 40.96 − 0.72 = 40.24, eye 48 − 0.72 = 47.28, all ≥ 32; favicon 64 − 0.96 = 63.04 ≥ 40 | |
| Counters drawn at target plus compensation | yes — 40 ≥ 32.72 (master), 64 ≥ 40.96 (favicon) | |

## Clearspace & minimum sizes

### Clearspace

| Field | Record |
|---|---|
| Largest **enclosed** counter | none — the eye, the channel and the mouth are one open space |
| Widest axis of it | n/a — no enclosed counter |
| Rounded up to a whole grid unit | n/a — no enclosed counter |
| Floor applied? | yes — no qualifying enclosed counter |
| **Clearspace** | 64 units = 25 % of the mark's rendered size |
| Datum | the artboard edge, per reproduction.md § Clearspace — never the ink, and the live area's margin is not counted toward it |
| For the lockup | 64 ÷ 256 × 256 = 64 units on all four sides of the lockup's bounding box (the mark renders at its full 256 inside both lockups) — 6.5 % of the full lockup's width, 12.5 % of the stacked lockup's width |

### The counter floor

`n/a — every counter clears the target`

## Misuse

| Do not | What breaks, in this mark's terms |
|---|---|
| Render `logo-mark` below 64 px | its ink is 23.04 units — 1.44 px at 16 px — and the 40-unit channel is 2.5 px; the sheet showed it soft from 16 to 24 px. Use `logo-favicon.svg` at ≤ 48 px. |
| Scale the master down to favicon size | the favicon drops the innermost half-turn and runs at w 32; the master cannot carry its 48-unit eye and 24-unit ink at 16 px |
| Place ink closer than 64 units per 256 of the mark's size (25 %) | the coil's open mouth on the right is a 40-unit channel; neighbouring ink inside the clearspace reads as a further turn |
| Recolour, add a gradient, a filter or a shadow | the mark is one filled path in one value; anything carried by a second value vanishes on collapse |
| Use it on a ground under 3:1 | `#A86B24` is 4.38 on white and 4.32 on `#0d1117`; any ground with relative luminance between 0.030 and 0.669 drops below 3:1 against it (0.2397 / 3 − 0.05 and 3 × 0.2397 − 0.05) — check any grey ground |
| Reset the wordmark or lockups in another face | φ_ink 0.1445, φ_ctr 0.1289, the 16 px cap minimum and the 248 / 128 px lockup minimums were all measured against Bahnschrift 600 |
| Use the `text`-bearing wordmark or lockups where the webfont is not guaranteed | outline conversion has not been performed — see Production handoff |
| Embed a `currentColor` file through `<img>` | inside `<img>` there is no inherited `color`, so `currentColor` resolves to black — on `#0d1117` that is 1.11:1 (black rendered on dark was observed in the lockup composition render). Inline the SVG, or use `logo-mono-white.svg` / a colour-resolved copy on dark. |
| Show `logo-mono-white` above 355 px | that is D1 state 2 (compensation of 0.72 units reaches a device pixel at 356 px) and this record does not cover it |

## Production handoff

### Outline conversion — not performed

**This skill has no font engine and does not convert type to outlines. That is an explicit non-goal, not an oversight.** Where this mark ships a `text` element, the master resolves against the declared webfont at render time. **Outline conversion has NOT been performed here.** It must happen before the mark is used anywhere the webfont is not guaranteed — print, embroidery, a third party's site, an email client. Until it has, a `text`-bearing wordmark is not a finished asset.

This paragraph ships with the template and is not edited or removed.

### Typeface

| Field | Record |
|---|---|
| Family | Bahnschrift (variable; DIN-derived) |
| Weight | 600 |
| `letter-spacing` | wordmark 1.87 @ 37.45 · full lockup 6.68 @ 133.57 · stacked 4.45 @ 89.04 — `t` = 0.05 in all three |
| Fallback stack | `Bahnschrift, 'DIN Alternate', 'Segoe UI', sans-serif` |
| Resolves on the render machine | yes — `document.fonts.check('600 64px "Bahnschrift"')` true; probe width 1236.75 against 1266.75 for the monospace fallback; the sheet's readout: "available — Bahnschrift resolved" |
| Licence / where it is hosted | UNRUN — not established; Bahnschrift ships with Windows and is not available on GitHub's or NuGet's renderers, which is a further reason the outline conversion above must happen before the lockups are used there |

### Trademark clearance — not performed

**No trademark, design-mark or prior-art clearance has been performed, and nothing in this skill measures collision with an existing mark.** A vision pass over a contact sheet can notice a resemblance it happens to recognise; that is not a search, and it did not run as one here. Clearance is a step for counsel, before the mark is used commercially or filed.

This paragraph ships with the template and is not edited or removed.

### Checks recorded unrun

| Check | Why it could not be run | What would decide it |
|---|---|---|
| `text` element top and bottom extents (`logo-wordmark`, `logo-full`, `logo-stacked`) | `getBBox()` returns the layout box — ascent to descent — not tight ink | UNRUN — nothing available in this skill; outline conversion makes them computable (a raster read gives cap 117.38 … 144.25 on the wordmark, recorded as an observation, not a grade) |
| `text` element left extent | the anchor is an origin, not a painted edge | UNRUN — outline conversion; raster observation: the wordmark's ink starts at 35.13 |
| `text` element right extent | graded on the harness's `ink edge` | wordmark 222.14 (harness, render 2), confirmed by raster at 222.75 — inside 224 |
| M3, the type's share | glyph rasterisation is not symmetric under inversion in this engine | UNRUN — the type is not outlined; outline it and M3 decides the whole file |
| Stroked containment | | n/a — filled marks, the two boxes coincide |
| Typeface licence and hosting | not established | UNRUN — the face's licence terms for embedding or hosting |

### Still to do

| Step | Status |
|---|---|
| Outline conversion | **not performed** — see above |
| Trademark clearance | **not performed** — see above |
| Declared face installed where the sheet renders | yes — the measured rows carry values |
| Icon raster set exported | done — ImageMagick 7.1.2, from bronze-resolved copies; see Raster set |
| Dark-ground embedding | done — the README shows `icon-192.png`, a bronze raster, as a Markdown image: 4.38:1 on white and 4.32:1 on GitHub dark, with no raw HTML for nuget.org to drop |
| Step 8 — wire into the project | done by phase 6.1 Task 7, at the maintainer's request through the controller — see Project files replaced |

## Asset manifest

| Path | Role | `viewBox` | Nodes | Distinct `OPTICAL:` reasons | Notes |
|---|---|---|---|---|---|
| `assets/brand/logo-mark.svg` | the master | `0 0 256 256` | 8 | 1 | reuse 17/27 = 0.63 |
| `assets/brand/logo-mono-black.svg` | mono, light grounds | `0 0 256 256` | 8 | 1 | `color="#000000"`; otherwise identical to logo-mark (diffed) |
| `assets/brand/logo-mono-white.svg` | mono, dark grounds | `0 0 256 256` | 8 | 1 | `color="#ffffff"`; otherwise identical to logo-mark (diffed) |
| `assets/brand/logo-favicon.svg` | favicon redraw | `0 0 256 256` | 6 | 0 | reuse 10/19 = 0.53 |
| `assets/brand/logo-wordmark.svg` | wordmark | `0 0 256 256` | 0 | 0 | one `text` run, Bahnschrift 600 @ 37.45 |
| `assets/brand/logo-full.svg` | horizontal lockup | `0 0 992 256` | 8 | 1 | mark geometry identical to logo-mark; `text` @ 133.57 |
| `assets/brand/logo-stacked.svg` | stacked lockup | `0 0 512 368` | 8 | 1 | mark geometry = logo-mark with x + 128, baked in; `text` @ 89.04 |

| Field | Record |
|---|---|
| Files in the set | 7 |
| Every variant carries `role="img"` and an `aria-label` naming the product | yes, the favicon included — `aria-label="Minos.NET"` on all seven |
| No `width` or `height` on any root `svg` element | yes |
| No empty groups, unreferenced `defs`, or surviving construction geometry | yes — no `g`, no `defs`; one path and at most one `text` per file |
| Mono variants diffed against their source | identical apart from the root `color` attribute |
| Every variant's ink sits inside the live area | mark x 16 … 240 / y 32 … 224 and favicon x 16 … 240 / y 40 … 216, graded on the geometry box (filled; the boxes coincide); wordmark x 32 … 224.01 advance box with ink 35.13 … 222.75 by raster, vertical extents unrun (see Checks recorded unrun); lockups are non-square and checked for composition only |
| Raster files recorded in Raster files | yes — all nine |

### Raster files

| Path | px | Rasterised from | Bytes |
|---|---|---|---|
| `assets/brand/favicon-16.png` | 16 | bronze-resolved copy of `logo-favicon.svg` (`currentColor` → `#A86B24`) | 1417 |
| `assets/brand/favicon-32.png` | 32 | bronze-resolved copy of `logo-favicon.svg` (`currentColor` → `#A86B24`) | 3305 |
| `assets/brand/favicon-48.png` | 48 | bronze-resolved copy of `logo-favicon.svg` (`currentColor` → `#A86B24`) | 5014 |
| `assets/brand/favicon.ico` | 16/32/48 | bronze-resolved copy of `logo-favicon.svg` (`currentColor` → `#A86B24`) | 9790 |
| `assets/brand/apple-touch-icon.png` | 180 | bronze-resolved copy of `logo-mark.svg` (`currentColor` → `#A86B24`) | 28146 |
| `assets/brand/icon-128.png` | 128 | bronze-resolved copy of `logo-mark.svg` (`currentColor` → `#A86B24`) | 19754 |
| `assets/brand/icon-192.png` | 192 | bronze-resolved copy of `logo-mark.svg` (`currentColor` → `#A86B24`) | 30015 |
| `assets/brand/icon-512.png` | 512 | bronze-resolved copy of `logo-mark.svg` (`currentColor` → `#A86B24`) | 82930 |
| `assets/brand/icon-1024.png` | 1024 | bronze-resolved copy of `logo-mark.svg` (`currentColor` → `#A86B24`) | 173887 |

| Field | Record |
|---|---|
| Files in the raster set | 9 |
| Every raster's source appears in the SVG manifest above | yes — `logo-mark.svg` and `logo-favicon.svg`, each through a bronze-resolved temporary copy that is not committed |
| Rasteriser used | recorded once in Raster set |

### Project files replaced

| Field | Record |
|---|---|
| Files replaced | `assets/icon.svg` and `assets/icon.png` (ZeroAlloc.Rest's shared icon) deleted; `Directory.Build.props` packs `assets/brand/icon-128.png` as the NuGet `icon.png`; `README.md` shows `assets/brand/icon-192.png`; the pack tests pin the SHA-256 of `logo-mark.svg` and `icon-128.png` |
| Every replaced path was tracked and clean before the write | yes — the working tree was clean on `phase/6.1-rename` at 46c903a |
| Slots found but skipped | the docs site's logo and favicon — the site moves into this repository later in phase 6.1 and takes them from `assets/brand/` |

## Structural self-verification (Step 7 item 3)

Re-run by phase 6.1 Task 7 against the repository copy in `assets/brand/`, after the raster export; it supersedes the scratch run.

- [x] Every file in the Asset manifest exists at its path, and every SVG in the directory is in the manifest (7 and 7).
- [x] Each SVG parses (XML, root `{http://www.w3.org/2000/svg}svg`).
- [x] Each root `svg` carries `xmlns="http://www.w3.org/2000/svg"` — string check, all seven.
- [x] Each root carries a `viewBox`; the five square variants all carry `0 0 256 256`; the lockups declare `0 0 992 256` and `0 0 512 368`.
- [x] No `filter` element anywhere.
- [x] The mono variants bind every paint to `currentColor` and differ from their source only in the resolved `color`.
- [x] Every file the exporter reported written (8), and `icon-128.png`, exists in `assets/brand/` and has a row in Raster files naming the SVG it was rasterised from.
- [x] No raster file in the asset directory lacks a manifest row (9 on disk, 9 rows).
