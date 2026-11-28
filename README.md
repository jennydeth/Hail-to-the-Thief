# Hail to the Thief

A build of [VIctory](https://github.com/Marvisak/VIctory) for **NVIDIA cards with 12 GB or less**,
measured on an RTX 3060. It is [trustno1-coder's VIctory-NVIDIA](https://github.com/trustno1-coder/VIctory-NVIDIA)
(built on [foxhound](https://github.com/readmet3xt-lab/foxhound)) with a few changes of our own on
top (below). That fork's own README, with every option and what it measured:
[docs/README-VIctory-NVIDIA.md](docs/README-VIctory-NVIDIA.md).

Runs the Xbox Series X|S build of the game natively on Windows, from your own copy of its files.
Nothing here contains game data.

## Status on an RTX 3060 12 GB

**Playable into chapter one, at about 29 frames a second on screen** with frame generation (AMD
FSR 3, on by default), about 14 rendered.

One machine so far: RTX 3060 12 GB (driver 621.40), Ryzen 5 5600X, 32 GB RAM, Windows 11, Series S
profile (the world drawn at 1280x720, shown at 1920x1080), DLSS Quality, no frame rate cap.

| Where | Rendered | Shown with frame generation | How it was measured |
|---|---|---|---|
| Opening room (prison cell) | 19 to 21 fps once settled | (not measured; about twice) | five 10-minute runs |
| Chapter one (from a save at the Keys house) | 14.4 fps | **about 29 fps** (26 to 32) | 8-minute run, FSR 3 on |
| Chapter one, played by hand | 13.5 fps (12 to 17, dips to 7 at scene loads) | (frame generation was off then) | 32 minutes in the world |

The story has been played by hand past the prologue into chapter one; the HUD, prompts, saves and
the pause menu work. No crash and no device removal in about 70 test runs and that session on
this tree and the ones before it here.

Frame generation doubles what is on screen; the game itself still runs, and answers the controls,
at the rendered rate. The limit is the processor: most of a frame is the game's own
code and the layer's work on the CPU, and the card waits.

How it got here on this PC (rendered, opening room): 2.8 fps on AvatarManz's tree, 6.4 on
hyphennn's, 7.8 with Series S, 12.6 on the NVIDIA megafork, 19 to 21 on this base.

Known problems:

- **No ray tracing** (the title's own is off: it costs most of the frame rate on this card). An
  estimate stands in for indirect light and reflections; tune it while playing (below).
- **Stutter when a scene loads or a cutscene cuts**: two to three times the usual memory to copy,
  and pipelines still being made (F1, PRELOAD SHADERS, makes them all ahead).
- **The window glass in the first room looks frosted**, and **eyes look washed out** in close-ups.
- **The water's own light** is left out on 32-lane cards here (a fix exists in hyphennn's tree).
- **DLSS frame generation needs an RTX 40 or 50**; on RTX 20 and 30 cards it is AMD's FSR 3
  (NVIDIA's also runs on an RTX 3060 with the MFG Unlocker mod; see below).

## What is different here

- **Frame generation on by default** in `scripts\play-nvidia.ps1` (`-FrameGen:$false` turns it
  off); trustno1's FSR 3 path on cards without DLSS frame generation.
- **The light estimate tuned for this card and live**: 1.1 for the light and 0.5,0.5 for
  reflections (1.5 and full reflections looked too bright and off in play, 0.8 too dark), read
  again from `lighting.txt` twice a second while the game runs.
- **Contact shadows and corners from ground-truth ambient occlusion** (horizon search, 3 slices of
  6 steps a side, light taken from the open side) in place of the earlier 12-sample crease shade:
  people, beds and furniture sit in their shade. No cost measured on the RTX 3060: 15.00 fps
  against 14.98, the two flipped live every 20 s in one run. `AMBIENT_OCCLUSION=creases` in
  `lighting.txt` brings back the earlier look.
- **Bounce light between nearby surfaces**: the surfaces the horizon search finds send the light
  the title lit them with (the frame before, at 320x180) back into the shade they cast, so walls
  and skin pick up the colour of what is next to them. No cost measured: 13.16 fps with it, 12.76
  without, flipped live every 15 s in one run. `AMBIENT_BOUNCE=0` turns it off.
- **Our own shaders are compiled with IEEE strictness.** Without it the compiler may fold away the
  NaN tests of the light estimate; on hyphennn's tree the shade then went black for good after a
  few minutes in half the runs.
- **foxhound's newest change** (`9f8838b1`, not yet in VIctory-NVIDIA): samplers made in one fixed
  order so the driver finds saved pipelines again, the cloud tile sort and violet light estimate
  fixes, a buffer pool, a VRAM limit, and a folder accepted for `-Exe`. Same frame rate here as
  without it (19.7 and 18.6 fps mean, 20.1 and 20.6 once settled, a cold and a warm run).
- **VIctory-NVIDIA up to `63b902fe`** (28 November): its crash fixes, the frame-time spread in the
  log, and skornuta's memory stack on in the launcher (`VICTORY_SLOT_EVICT`,
  `VICTORY_ALLOCATOR_TRIM`, `VICTORY_BUDGET_PACK`). From the chapter-one save: 14.73, 14.57 and
  14.67 fps once settled against 15.19, 13.06, 14.42 and 13.74 before it, the longest second 79 to
  83 ms against 85 to 94. Without the memory stack the merged tree was slower (12.81 on average
  over four runs), so keep it on.

## Tried on this PC, no gain

Each against the defaults above, 10-minute runs, Series S, DLSS Quality. Three runs of the
defaults gave 18.6 to 19.3 fps mean (20.4 to 20.7 once settled): a difference smaller than that
is noise.

| Tried | Mean fps | Once settled | |
|---|---|---|---|
| `VICTORY_THIN_THREADS=4 VICTORY_UNDER_THREADS=3 VICTORY_VIEW_THREADS=3 VICTORY_PIPELINE_WORKERS=4` (a six-core set measured on another base) | 15.2 | 16.8 | worse |
| `VICTORY_JOIN_PARTS=16 VICTORY_JOIN_LISTS=96` | 19.1 | 19.9 | no gain |
| `VICTORY_SUBMIT_FRAMES=2` (a frame more in flight) | 19.9 | 20.4 | no gain |
| `VICTORY_HEAP_RECHECK=0` | 19.6 | 20.1 | no gain |
| The submitting thread on a core of its own (branch `lane-core`, `VICTORY_LANE_CORE=1`) | 19.3 | 20.6 | no gain |

From the chapter-one save, 8-minute runs, FSR 3 frame generation on (rendered rates). The
defaults gave 14.4 and 14.8 fps mean (14.7 and 15.1 once settled).

| Tried | Mean fps | Once settled | |
|---|---|---|---|
| A build for Zen 3 (`RUSTFLAGS=-C target-cpu=znver3`, as RAZDA4A XEN builds) | 12.4 | 13.0 | no gain |
| `VICTORY_TEXTURE_ROOM_PERCENT=70` (the launcher uses 80 with frame generation) | 13.0 | 13.0 | no gain |
| `victory preload` (all 83,718 pipelines made ahead, 32 minutes once) | 14.8 | 15.1 | no change in a place already seen; it is for places not seen yet |

Measured on 28 November, against controls the same night: 13.16, 12.91 and 12.93 fps mean (13.80,
13.02 and 13.21 once settled).

| Tried | Mean fps | Once settled | |
|---|---|---|---|
| mimalloc as the allocator (from [ProjectLeonida/mandy](https://github.com/ProjectLeonida/mandy)'s runtime; branch `mimalloc`) | 11.09 | 14.47 | no gain (a cold start) |
| [tiadiff/VIctory-NVIDIA-LowStutter](https://github.com/tiadiff/VIctory-NVIDIA-LowStutter)'s four CPU changes: a slot bitset, packed heap ranges, fewer pipeline waits, no scratch allocations (branch `lowstutter-cpu`) | 12.79, 12.62 | 13.00, 13.09 | no gain |
| Copy constants snapshotted on the card, `VICTORY_PLACED_ORDER` on ([whiteh4cker-tr/VIctory_sm86](https://github.com/whiteh4cker-tr/VIctory_sm86) `be29e9c`; branch `placed-constants`) | 12.79 | 12.93 | no gain |
| `VICTORY_HELPER_PRIORITY=1` (helpers at the submitting thread's priority, as scarlet-fps sets it) | 12.58 | 13.06 | no gain |
| [clankenjoyer/scarlet-fps](https://github.com/clankenjoyer/scarlet-fps)'s settings that this tree knows (18, among them `VICTORY_ASYNC_SUBMIT`, `VICTORY_COPY_BESIDE`, `VICTORY_WIDE_MERGE`) | 13.19, 13.40 | 13.38, 13.92 | no gain; preparing a frame took 32.5 to 34 ms instead of 38 to 40 |
| Frame generation off (the same night) | 13.54, 12.97 | 13.60, 13.46 | it does not cost rendered frames here |
| `VICTORY_WINDOWS_BAR_MB=1024 VICTORY_CARD_BUFFERS_MB=1536` (Resizable BAR is on here; [Keel62155/VIctoryUnified](https://github.com/Keel62155/VIctoryUnified) measured 20 % on a card-bound PC) | 12.98, 12.82 | 12.99, 12.83 | no gain; the longest second 109 to 112 ms |
| `VICTORY_PIPELINE_WAIT_MS=80` (500 by default; [CHECK6960/Victory-Nvidia-optimized](https://github.com/CHECK6960/Victory-Nvidia-optimized)) | 12.72 | 12.78 | no change standing in one place; it is for the stalls when pipelines are still being made |
| Other runtimes, from the same save: scarlet-fps itself (warm) | 13.57, 14.83 | 14.75, 15.46 | faster, but faces come out black |
| HUMAN without ray tracing ([HumanlyDev/HUMAN](https://github.com/HumanlyDev/HUMAN)) | 2.6 | 2.7 | |
| mandy's `leonida.exe`, cold and warm | 4.2, 11.03 | 8.3, 13.65 | the same as this tree once warm |

Whatever the layer's part shrank to, a frame stayed at 66 to 76 ms: when preparing or waiting for
the card took less, the frame waited for the title's own threads instead. On this processor the
title's own code now sets the rendered rate.

Where the frame goes (defaults, once settled, about 48 ms a frame): preparing 33.6 ms (of it
copies of title memory 13.8 ms for 32 MB), copying back 15.6 ms (for about 1 MB), waiting for the
card 1.7 ms. The processor, not the card, sets the frame rate on this PC.

## How to run it

Windows 10/11 x64, 32 GB of RAM, an NVIDIA GeForce RTX card, Rust (pinned), the MSVC build tools,
and your own dump of the game.

```powershell
git clone https://github.com/jennydeth/Hail-to-the-Thief
cd Hail-to-the-Thief
copy victory.example.toml victory.toml      # then set game_dir to your dump's folder, outside this one
.\Get-NVIDIA-DLSS.cmd                        # once: NVIDIA's DLSS files (about 70 MB) from NVIDIA's GitHub
.\Get-AMD-FSR.cmd                            # once: AMD's FSR 3 frame generation (about 30 MB) from AMD's GitHub
cargo build --locked --release -p victory
scripts\play-nvidia.ps1 -Exe <path of your copy's executable> -SeriesS -FrameGen
```

`-SeriesS` is what was measured above (with DLSS Quality, the script's default once NVIDIA's files
are there). Without it the script runs the Series X profile at 2560x1440, heavier on the card.

**Frame generation on an RTX 30 card** (on by default; `-FrameGen:$false` turns it off) shows a generated frame between each two the
game renders. On a card without DLSS frame generation (RTX 20 and 30) it is AMD's FSR 3. RTX 3060,
from a chapter-one save, 8 minutes: 14.4 frames a second rendered (16.1 without it), about 28 to
32 shown, the interface not doubled, no crash. NVIDIA's own DLSS frame generation also runs on an
RTX 3060 with the MFG Unlocker mod (F1, MFG UNLOCKER, then restart: someone else's mod, fetched from
its author's release and checked): 13.7 rendered, about 26 to 30 shown. Input still answers at the
rendered rate.
The first run translates shaders as they are met: the first minutes in the world are slower.
F1 in the game opens the settings menu, F3 the stats panel.

**Lighting by eye:** copy `lighting.example.txt` to `lighting.txt` before starting, then edit and
save it while you play: the light that stands in for the title's ray tracing follows within half
a second, at no cost in frame rate. The defaults are 1.1 for the light and 0.5,0.5 for reflections (1.5 and full stand-in reflections looked too bright and off in play; 0.8 left the shade too dark).

## Credits

This is other people's work with a little on top; the history is kept.

- [trustno1-coder/VIctory-NVIDIA](https://github.com/trustno1-coder/VIctory-NVIDIA), the tree this
  is, and everyone it credits: foxhound, fxceless/hyphennn, AvatarManz, DexNom, Belobug, CGBJ's
  cool_fox, the megafork and more (see its README).
- [VIctory](https://github.com/Marvisak/VIctory) by Marvisak and its contributors.

## Licence

GPL-3.0-or-later, as VIctory. See [LICENSE](LICENSE). NVIDIA's DLSS files are NVIDIA's, under
NVIDIA's licences, and are not in this repository. Built with Claude Code (Claude Opus 5.5).
