# Hail to the Thief

A build of [VIctory](https://github.com/Marvisak/VIctory) for **NVIDIA cards with 12 GB or less**,
measured on an RTX 3060. It is [trustno1-coder's VIctory-NVIDIA](https://github.com/trustno1-coder/VIctory-NVIDIA)
(built on [foxhound](https://github.com/readmet3xt-lab/foxhound)) as it is, with one fix on top.
That fork's own README, with every option and what it measured:
[docs/README-VIctory-NVIDIA.md](docs/README-VIctory-NVIDIA.md).

Runs the Xbox Series X|S build of the game natively on Windows, from your own copy of its files.
Nothing here contains game data.

## Status on an RTX 3060 12 GB

One machine so far: RTX 3060 12 GB (driver 621.40), Ryzen 5 5600X, 32 GB RAM, Windows 11. Runs of
10 to 12 minutes in the same place (the first room of the opening), diag build, Series S profile
(the world drawn at 1280x720, shown at 1920x1080), no frame rate cap.

| Tree | Upscaling | Frame rate in the world (mean) | Once settled | Crashes / device removals |
|---|---|---|---|---|
| hyphennn's victory-fork | a stretch | 7.8 fps | - | 0 / 0 |
| rorodev6's NVIDIA megafork | the title's own | 12.3 fps | about 12.8 | 0 / 0 |
| this tree, cold shader cache | the title's own | 13.6 fps | 15 to 16.6 | 0 / 0 |
| **this tree** | **DLSS Quality** | **15.3 to 18.6 fps** (two runs) | **16 to 21** | 0 / 0 |

The gain over the megafork is on the processor: the thread that prepares each frame needs about
37 ms instead of 48, and copying the card's writes back about 9 ms instead of 16. On this PC the
processor, not the card, sets the frame rate.

**Not playable yet** on this card, but it holds 16 to 21 fps in the opening room.

Known problems:

- **No ray tracing** (the title's own is off): an estimate stands in for indirect light.
- **The window glass in the first room looks frosted** in both runs on this tree; not checked
  against the console.
- **DLSS frame generation needs an RTX 40 or 50**; on an RTX 30 card only DLSS upscaling is used.

## What is different here

- **Our own shaders are compiled with IEEE strictness.** Without it the compiler may fold away the
  NaN tests of the stand-in for the indirect light; on hyphennn's tree the shade then went black
  for good after a few minutes in half the runs. Not measured on this tree yet.
- **foxhound's newest change** (`9f8838b1`, not yet in VIctory-NVIDIA): samplers made in one fixed
  order so the driver finds saved pipelines again, the cloud tile sort and violet light estimate
  fixes, a buffer pool, a VRAM limit, and a folder accepted for `-Exe`. Same frame rate here as
  without it (19.7 and 18.6 fps mean, 20.1 and 20.6 once settled, a cold and a warm run).

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
cargo build --locked --release -p victory
scripts\play-nvidia.ps1 -Exe <path of your copy's executable> -SeriesS
```

`-SeriesS` is what was measured above (with DLSS Quality, the script's default once NVIDIA's files
are there). Without it the script runs the Series X profile at 2560x1440, heavier on the card.
The first run translates shaders as they are met: the first minutes in the world are slower.
F1 in the game opens the settings menu, F3 the stats panel.

## Credits

This is other people's work with a little on top; the history is kept.

- [trustno1-coder/VIctory-NVIDIA](https://github.com/trustno1-coder/VIctory-NVIDIA), the tree this
  is, and everyone it credits: foxhound, fxceless/hyphennn, AvatarManz, DexNom, Belobug, CGBJ's
  cool_fox, the megafork and more (see its README).
- [VIctory](https://github.com/Marvisak/VIctory) by Marvisak and its contributors.

## Licence

GPL-3.0-or-later, as VIctory. See [LICENSE](LICENSE). NVIDIA's DLSS files are NVIDIA's, under
NVIDIA's licences, and are not in this repository. Built with Claude Code (Claude Opus 5.5).
