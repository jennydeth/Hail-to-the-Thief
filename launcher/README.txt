Foxhound (pre-release)
======================

A compatibility layer that runs one Xbox Series title on Windows.
No game files are included, and none will be: bring your own copy.

What you need
-------------
- Your own copy of the title (its folder with the .exe).
- Windows 10 or 11, 64-bit, a DirectX 12 graphics card with current drivers.
- 32 GB of RAM (the tested run has 20 GB in use), an SSD for the title's files.
- 12 GB of video memory for the Series X profile at 1440p. Less: choose the
  Series S profile ("1080p class").
- Only for DLSS: NVIDIA's Streamline SDK, downloaded by you from NVIDIA
  (github.com/NVIDIA-RTX/Streamline, a release .zip; its licence does not let
  it be shipped here).

Tested on one PC only: RTX 4070 SUPER 12 GB, Ryzen 7 7800X3D, 32 GB RAM,
Windows 11, 1440p, Series X profile, DLSS Quality.

Start
-----
1. Unpack the .zip anywhere. Keep Foxhound.exe and victory.exe together.
2. Start Foxhound.exe. Windows may warn about an unknown publisher (the
   programs are not signed): "More info" > "Run anyway". You can build both
   programs yourself from the source instead (see the repository's README).
3. First start: choose the folder that holds your copy of the title. This is
   asked once. A shortcut "Foxhound" is put on the desktop.
4. Choose your settings and press Play.
   - First time, and after an update of Foxhound: the title's shaders are
     translated behind a loading screen (2 to 5 minutes).
   - Every time: the pipelines earlier runs asked for are compiled (seconds;
     minutes after an update of Foxhound or of the graphics driver).
   - The first time you reach a scene, its pipelines are compiled while you
     play (short hitches). They are remembered; the next start compiles them
     ahead.
5. While playing, F1 opens the settings. Each one says whether it takes
   effect "at once" or "after restart". "Restart the game" and "Quit the game"
   are there too.

Settings
--------
Resolution       1080p class = Series S profile (lightest, no DLSS);
                 1440p or 4K = Series X profile.
DLSS             Off / DLAA / Quality / Balanced / Performance. NVIDIA only,
                 needs the Streamline folder.
Frame Generation RTX 40 or later, with DLSS.
Display          Fullscreen (borderless) / borderless window / window.
VRAM limit       The most video memory the layer plans with. Auto = what
                 Windows reports for your card.
Texture loading  All detail loaded (12 GB or more) / detail on demand.
Frame rate       The title's own 30 fps / a 60 fps target (experimental).
Process priority Normal / above normal / high.
Further switches One NAME=value a line; see the repository's README.

Where things are
----------------
Settings   %LOCALAPPDATA%\foxhound\launcher.ini
Logs       %LOCALAPPDATA%\foxhound\logs   (attach the newest to a bug report)
Caches     %LOCALAPPDATA%\victory\cache   (shaders and pipelines; safe to delete)
Saves      %LOCALAPPDATA%\victory\data

Known problems are listed in the release notes and the repository's README.
