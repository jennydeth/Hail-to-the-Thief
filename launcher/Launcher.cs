// The launcher: one window for everything a player sets before the title starts.
//
//  - asks once for the folder of the player's own copy of the title (a folder is enough: the
//    executable in it is found);
//  - settings: resolution and console profile, DLSS, frame generation, display mode, the card's
//    memory limit, texture loading, frame-rate target, priority, log, further switches;
//  - before the title starts, behind a loading screen: the title's shaders are translated (once
//    per build of the layer and set of switches) and the pipelines earlier runs asked for are
//    compiled, with the very switches of the run;
//  - starts the title, makes its window what "Display" says, and stays in the background:
//    F1 while playing opens the settings (each marked "at once" or "after restart"), with
//    "Restart the game" and "Quit the game";
//  - makes a desktop shortcut on the first start.
//
// It only starts victory.exe (beside it) with environment switches; nothing of the title is in
// it. Settings: %LOCALAPPDATA%\foxhound\launcher.ini. Logs: %LOCALAPPDATA%\foxhound\logs.
//
// Built by scripts\build-launcher.ps1 with the C# compiler of the .NET Framework that every
// Windows 10/11 has (C# 5: no newer syntax here).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Foxhound launcher")]
[assembly: AssemblyProduct("Foxhound")]
[assembly: AssemblyVersion("0.2.0.0")]
[assembly: AssemblyFileVersion("0.2.0.0")]

namespace Foxhound
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern bool ClipCursor(IntPtr rect);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();

        public const int GWL_STYLE = -16;
        public const long WS_POPUP_VISIBLE = 0x90000000L;
        public const uint SWP_FRAMECHANGED_SHOW = 0x0060;

        // Puts a window in front even when this program is not in front itself.
        public static void Front(IntPtr h)
        {
            uint pid;
            uint fg = GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            uint me = GetCurrentThreadId();
            AttachThreadInput(me, fg, true);
            BringWindowToTop(h);
            SetForegroundWindow(h);
            AttachThreadInput(me, fg, false);
        }

        public static uint ProcessOf(IntPtr h)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            return pid;
        }
    }

    // launcher.ini: key=value lines; in a value a line break is written as \n, a backslash as two.
    class Store
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "foxhound");
        public static readonly string LogDir = Path.Combine(Dir, "logs");
        readonly Dictionary<string, string> values = new Dictionary<string, string>();
        static string File_ { get { return Path.Combine(Dir, "launcher.ini"); } }

        public static Store Load()
        {
            Store s = new Store();
            try
            {
                foreach (string line in File.ReadAllLines(File_, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) s.values[line.Substring(0, eq)] = Unescape(line.Substring(eq + 1));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return s;
        }

        // \\ is a backslash, \n a line break (a path may hold "\n" itself).
        static string Unescape(string v)
        {
            StringBuilder b = new StringBuilder(v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i] == '\\' && i + 1 < v.Length) { i++; b.Append(v[i] == 'n' ? '\n' : v[i]); }
                else b.Append(v[i]);
            }
            return b.ToString();
        }

        public string Get(string key, string otherwise)
        {
            string v;
            return values.TryGetValue(key, out v) ? v : otherwise;
        }

        public bool Has(string key) { return values.ContainsKey(key); }

        public void Set(string key, string value) { values[key] = value ?? ""; }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, string> p in values) lines.Add(p.Key + "=" + p.Value.Replace("\r", "").Replace("\\", "\\\\").Replace("\n", "\\n"));
                lines.Sort(StringComparer.Ordinal);
                File.WriteAllLines(File_, lines.ToArray(), new UTF8Encoding(false));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // The graphics card with the most memory of its own (the one a run uses).
    class Card
    {
        public string Name = "";
        public double Gb;   // 0: not found
        public bool Nvidia { get { return Name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0; } }
        public bool Amd { get { return Name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 || Name.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0; } }

        public static Card Detect()
        {
            Card best = new Card();
            try
            {
                using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (cls == null) return best;
                    foreach (string sub in cls.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey k = cls.OpenSubKey(sub))
                            {
                                if (k == null) continue;
                                object size = k.GetValue("HardwareInformation.qwMemorySize");
                                object name = k.GetValue("DriverDesc");
                                if (size == null || name == null) continue;
                                double gb = Convert.ToInt64(size) / 1073741824.0;
                                if (gb > best.Gb) { best.Gb = gb; best.Name = name.ToString(); }
                            }
                        }
                        catch (Exception) { }   // subkeys this user may not open
                    }
                }
            }
            catch (Exception) { }
            return best;
        }
    }

    class Opt
    {
        public string Key, Label, Otherwise, Hint;
        public string[] Values, Names;
        public bool Live;   // takes effect at once, in a running game
        public Opt(string key, string label, bool live, string otherwise, string[] values, string[] names, string hint)
        {
            Key = key; Label = label; Live = live; Otherwise = otherwise; Values = values; Names = names; Hint = hint;
        }
    }

    static class Settings
    {
        public static Opt[] Options(Card card)
        {
            string cardText = card.Gb > 0 ? string.Format("{0}, {1:0.#} GB", card.Name, card.Gb) : "not found";
            return new Opt[] {
                new Opt("resolution", "Resolution", false, "1440",
                    new string[] { "1080", "1440", "4k" },
                    new string[] { "1080p class: Series S profile (lightest)", "1440p: Series X profile (tested)", "4K: Series X profile" },
                    "The console profile the title is told it runs on, and its output size. Series S: the title's own low-resolution scene, no DLSS; for cards under 12 GB. 1440p on the Series X profile is the tested choice. 4K is the profile's stock output (scene 1440p unless DLSS sets it)."),
                new Opt("dlss", "DLSS", false, "quality",
                    new string[] { "off", "dlaa", "quality", "balanced", "performance" },
                    new string[] { "Off (the title's own upscaling)", "DLAA (scene at full size)", "Quality (scene at 67%)", "Balanced (scene at 58%)", "Performance (scene at 50%)" },
                    "NVIDIA cards, Series X profile only. Needs the folder of NVIDIA's Streamline SDK (set above; NVIDIA's licence does not let it be shipped here). A smaller scene is less work for the card; where the processor is the limit it changes nothing."),
                new Opt("framegen", "Frame Generation", false, "0",
                    new string[] { "0", "1" },
                    new string[] { "Off", "On (RTX 40 or later, needs DLSS)" },
                    "DLSS Frame Generation: every second frame shown is made by the card (smoother, not more responsive). Needs DLSS on and hardware-accelerated GPU scheduling in Windows."),
                new Opt("display", "Display", true, "fullscreen",
                    new string[] { "fullscreen", "borderless", "windowed" },
                    new string[] { "Fullscreen (borderless, fills the monitor)", "Borderless window", "Window with a title bar (shows fps)" },
                    "How the game's window is shown. Changes at once, also while playing. Exclusive fullscreen is not available."),
                new Opt("vram", "VRAM limit", false, "auto",
                    new string[] { "auto", "6", "8", "10", "12", "16", "20", "24" },
                    new string[] { "Auto (what Windows gives; card: " + cardText + ")", "6 GB", "8 GB", "10 GB", "12 GB", "16 GB", "20 GB", "24 GB" },
                    "The most of the card's memory the layer plans with. Auto: the budget Windows reports. A lower limit leaves memory to other programs and keeps a small card from overflowing into the PC's memory (which is slow). The tested setup uses 11.6 GB at 1440p; under 12 GB choose the Series S profile."),
                new Opt("textures", "Texture loading", false, "auto",
                    new string[] { "auto", "sharp", "ondemand" },
                    new string[] { "Auto (by VRAM)", "All detail loaded (sharp)", "Detail on demand (less VRAM, can blur)" },
                    "All detail loaded: every texture level is put on the card (tested on 12 GB). On demand: levels are loaded as they are needed; less memory, textures can stay blurry for a moment. Auto: on demand under 11 GB."),
                new Opt("ambient", "Dark areas", false, "0.8",
                    new string[] { "0.6", "0.8", "1.0", "1.3", "1.6" },
                    new string[] { "Darker (0.6)", "As tested (0.8)", "Brighter (1.0)", "Bright (1.3)", "Brightest (1.6)" },
                    "Ray tracing is off: an estimate stands in for the light that reaches rooms and shadows indirectly. This is the share of that estimate that is used; a higher value lifts dark rooms and shadows."),
                new Opt("fps", "Frame rate", false, "title",
                    new string[] { "title", "40", "60" },
                    new string[] { "30 fps (the title's own)", "40 fps", "60 fps" },
                    "The rate the title is told to run at (its own setting is 30; its frame time, its limiter and its simulation step follow, so the game is not faster, only smoother). The layer needs about 26 ms a frame in gameplay on the tested PC, so 40 is near what it reaches and 60 is not reached yet; frames then come as fast as they are made. Frame Generation doubles what is shown."),
                new Opt("priority", "Process priority", true, "normal",
                    new string[] { "normal", "above", "high" },
                    new string[] { "Normal", "Above normal", "High" },
                    "The priority Windows gives the game's process. Changes at once. Higher can steady frame times when other programs are busy."),
                new Opt("build", "Layer build", false, "release",
                    new string[] { "release", "diag" },
                    new string[] { "Release", "Diagnostics (slower; F2 saves the frame)" },
                    "Diagnostics: for finding faults. Needs victory-diag.exe beside the launcher (built from the source with the diagnostics feature; not in the release). While playing, F2 saves the frame being drawn, with everything needed to look at it later, under %LOCALAPPDATA%\\foxhound\\captures (several GB each)."),
                new Opt("log", "Log", false, "full",
                    new string[] { "full", "warn" },
                    new string[] { "Full (for bug reports)", "Warnings only" },
                    "What a run writes to its log file (about 50 MB an hour when full; the last 8 logs are kept)."),
            };
        }

        // The switch set tested on an RTX 4070 SUPER (see scripts\play-foxhound.ps1 for what each is).
        static readonly string[] Base = {
            "VICTORY_THIN=1", "VICTORY_JOIN_PARTS=8", "VICTORY_RELAY=2", "VICTORY_RELAY_COPY=1", "VICTORY_WIDE_BACK=1",
            "VICTORY_CARD_REHASH_RUNS=1", "VICTORY_JOIN_COPIES=1", "VICTORY_CARD_STEPS=2", "VICTORY_CARD_AWAKE=1",
            "VICTORY_CARD_BUFFERS_MIN_KB=64", "VICTORY_INDIRECT_WINDOW=1", "VICTORY_INDIRECT_BATCH=2", "VICTORY_UPLOAD_AHEAD=1",
            "VICTORY_SHOW_AFTER=1", "VICTORY_MAP_THREADS=1", "VICTORY_WC_NO_BACK=1", "VICTORY_WC_NO_BACK_MIN_KB=1024",
            "VICTORY_SOFT_VIEWS=copies", "VICTORY_TEXTURE_POOL=1", "VICTORY_RIPE_BLOCKS=0", "VICTORY_TEXTURE_NO_WAIT=1",
            "VICTORY_TEXTURE_CATCHUP=0", "VICTORY_TEXTURE_STAGE_MS=16", "VICTORY_LEVELS_ON_DEMAND=0", "VICTORY_PRELUDE_UPLOADS=1",
            "VICTORY_SHOW_NEW_LATER=1", "VICTORY_POOL_SMALL=0", "VICTORY_TABLED_REREAD=1", "VICTORY_QUEUE_HOST_TEXTURES=0",
            "VICTORY_HEAP_SYNC_FIRST=1", "VICTORY_SYNC_IN_PLACE=1", "VICTORY_SPANS_QUICK=1", "VICTORY_SYNC_FINE=1",
            "VICTORY_HEAP_SYNC_AT_SUBMIT=1", "VICTORY_HELPER_PRIORITY=1", "VICTORY_AMBIENT=0.8", "VICTORY_AMBIENT_AIR=1.5",
            "VICTORY_AMBIENT_SPECULAR=1", "VICTORY_EARLY_DEPTH=1", "VICTORY_LEAVE_OUT=02384a60fda6f2d0,731a39171b568298",
            "VICTORY_PIPELINE_BOOK=1",
        };

        // Switches that do not change a shader's translation: left out of the stamp that decides
        // whether the shaders are gone through again.
        public static readonly string[] NotOfShaders = {
            "VICTORY_DLSS", "VICTORY_DLSS_MODE", "VICTORY_FRAMEGEN", "VICTORY_STREAMLINE_DIR", "VICTORY_VRAM_BUDGET_MB",
            "VICTORY_FRAME_INTERVAL_MS", "VICTORY_RECORD", "VICTORY_LOG", "VICTORY_GAME_DIR", "VICTORY_LEVELS_ON_DEMAND", "VICTORY_RIPE_BLOCKS",
            "VICTORY_AMBIENT", "VICTORY_AMBIENT_AIR", "VICTORY_AMBIENT_SPECULAR",
        };

        public static bool SeriesS(Store s) { return s.Get("resolution", "1440") == "1080"; }

        public static readonly string CaptureDir = Path.Combine(Store.Dir, "captures");

        public static bool DiagThere() { return File.Exists(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "victory-diag.exe")); }

        public static bool Diag(Store s) { return s.Get("build", "release") == "diag" && DiagThere(); }

        public static string StreamlineDir(Store s)
        {
            string dir = s.Get("streamline_dir", "");
            return dir.Length > 0 && File.Exists(Path.Combine(dir, "sl.interposer.dll")) ? dir : "";
        }

        public static bool DlssUsable(Store s, Card card)
        {
            return !SeriesS(s) && StreamlineDir(s).Length > 0 && (card.Nvidia || card.Gb == 0);
        }

        // The card's memory the run plans with, in GB (0: unknown).
        public static double EffectiveGb(Store s, Card card)
        {
            double limit;
            if (double.TryParse(s.Get("vram", "auto"), out limit)) return card.Gb > 0 ? Math.Min(card.Gb, limit) : limit;
            return card.Gb;
        }

        public static Dictionary<string, string> Environment_(Store s, Card card)
        {
            Dictionary<string, string> env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string kv in Base) { int eq = kv.IndexOf('='); env[kv.Substring(0, eq)] = kv.Substring(eq + 1); }
            string res = s.Get("resolution", "1440");
            if (res == "1440") env["VICTORY_NATIVE_1440P"] = "1";
            string dlss = s.Get("dlss", "quality");
            if (dlss != "off" && DlssUsable(s, card))
            {
                env["VICTORY_DLSS"] = "1";
                env["VICTORY_DLSS_MODE"] = dlss;
                env["VICTORY_STREAMLINE_DIR"] = StreamlineDir(s);
                if (s.Get("framegen", "0") == "1") env["VICTORY_FRAMEGEN"] = "1";
            }
            double limit;
            if (double.TryParse(s.Get("vram", "auto"), out limit)) env["VICTORY_VRAM_BUDGET_MB"] = ((int)(limit * 1024)).ToString();
            double gb = EffectiveGb(s, card);
            string textures = s.Get("textures", "auto");
            if (textures == "ondemand" || (textures == "auto" && gb > 0 && gb < 11)) env["VICTORY_LEVELS_ON_DEMAND"] = "1";
            env["VICTORY_AMBIENT"] = s.Get("ambient", "0.8");
            string fps = s.Get("fps", "title");
            if (fps == "40") env["VICTORY_FRAME_INTERVAL_MS"] = "25";
            else if (fps == "60") env["VICTORY_FRAME_INTERVAL_MS"] = "16.667";
            if (s.Get("log", "full") == "warn") env["VICTORY_LOG"] = "warn";
            if (Diag(s)) env["VICTORY_RECORD"] = CaptureDir;
            foreach (string raw in s.Get("extra", "").Split('\n'))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0 || line.StartsWith("#")) continue;
                string name = line.Substring(0, eq).Trim();
                if (!name.StartsWith("VICTORY_", StringComparison.OrdinalIgnoreCase)) name = "VICTORY_" + name;
                env[name.ToUpperInvariant()] = line.Substring(eq + 1).Trim();
            }
            return env;
        }

        // The title's executable in a folder (or one folder below it); null: none or several.
        public static string FindExecutable(string folder)
        {
            try
            {
                string found = OneExecutable(folder);
                if (found != null) return found;
                foreach (string sub in Directory.GetDirectories(folder))
                {
                    found = OneExecutable(sub);
                    if (found != null) return found;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        static string OneExecutable(string folder)
        {
            string[] exes;
            try { exes = Directory.GetFiles(folder, "*.exe"); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            if (exes.Length == 1) return exes[0];
            string game = null;
            int games = 0;
            foreach (string e in exes)
                if (Path.GetFileName(e).StartsWith("game", StringComparison.OrdinalIgnoreCase)) { game = e; games++; }
            return games == 1 ? game : null;
        }
    }

    static class Theme
    {
        public static readonly Color Back = Color.FromArgb(24, 26, 31);
        public static readonly Color Panel = Color.FromArgb(34, 37, 44);
        public static readonly Color Text = Color.FromArgb(232, 234, 238);
        public static readonly Color Dim = Color.FromArgb(160, 166, 178);
        public static readonly Color Accent = Color.FromArgb(232, 142, 48);
        public static readonly Color Good = Color.FromArgb(120, 200, 130);
        public static readonly Font Body = new Font("Segoe UI", 9.5f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font Head = new Font("Segoe UI Semibold", 17f);

        public static Button Button(string text, bool accent)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = accent ? Accent : Color.FromArgb(70, 75, 88);
            b.BackColor = accent ? Accent : Panel;
            b.ForeColor = accent ? Color.Black : Text;
            b.Font = accent ? new Font("Segoe UI Semibold", 10.5f) : Body;
            b.Height = 34;
            return b;
        }

        public static Label Label(string text, Font font, Color color)
        {
            Label l = new Label();
            l.Text = text; l.Font = font; l.ForeColor = color; l.BackColor = Color.Transparent; l.AutoSize = true;
            return l;
        }
    }

    // The rows of options: a label, a list, and (in the F1 menu) when a change takes effect.
    class OptionsPanel : Panel
    {
        readonly Store store;
        readonly Card card;
        readonly Opt[] options;
        readonly Dictionary<string, ComboBox> boxes = new Dictionary<string, ComboBox>();
        readonly Dictionary<string, string> atStart = new Dictionary<string, string>();
        bool filling;
        public Label Hint;
        public event Action<Opt> Changed;

        public OptionsPanel(Store store, Card card, bool menu, int width)
        {
            this.store = store; this.card = card;
            options = Settings.Options(card);
            BackColor = Theme.Back;
            Width = width;
            int y = 0, labelWidth = 130, tagWidth = menu ? 96 : 0;
            foreach (Opt o in options)
            {
                Label l = Theme.Label(o.Label, Theme.Body, Theme.Text);
                l.Location = new Point(0, y + 5);
                Controls.Add(l);
                ComboBox c = new ComboBox();
                c.DropDownStyle = ComboBoxStyle.DropDownList;
                c.FlatStyle = FlatStyle.Flat;
                c.BackColor = Theme.Panel; c.ForeColor = Theme.Text; c.Font = Theme.Body;
                c.Location = new Point(labelWidth, y);
                c.Width = width - labelWidth - tagWidth - (menu ? 10 : 0);
                c.Items.AddRange(o.Names);
                c.Tag = o;
                c.SelectedIndexChanged += OnPick;
                c.Enter += delegate(object sender, EventArgs e) { ShowHint((Opt)((ComboBox)sender).Tag); };
                Controls.Add(c);
                boxes[o.Key] = c;
                if (menu)
                {
                    Label tag = Theme.Label(o.Live ? "at once" : "after restart", Theme.Small, o.Live ? Theme.Good : Theme.Accent);
                    tag.Location = new Point(width - tagWidth, y + 6);
                    Controls.Add(tag);
                }
                y += 32;
            }
            Hint = new Label();
            Hint.Font = Theme.Small; Hint.ForeColor = Theme.Dim; Hint.BackColor = Theme.Back;
            Hint.Location = new Point(0, y + 4);
            Hint.Size = new Size(width, 64);
            Controls.Add(Hint);
            Height = y + 72;
            Fill();
            foreach (Opt o in options) atStart[o.Key] = store.Get(o.Key, o.Otherwise);
        }

        // Selections from the store; lists that cannot apply are greyed.
        public void Fill()
        {
            filling = true;
            foreach (Opt o in options)
            {
                ComboBox c = boxes[o.Key];
                int at = Array.IndexOf(o.Values, store.Get(o.Key, o.Otherwise));
                c.SelectedIndex = at < 0 ? Array.IndexOf(o.Values, o.Otherwise) : at;
            }
            bool dlss = Settings.DlssUsable(store, card);
            boxes["dlss"].Enabled = dlss;
            boxes["framegen"].Enabled = dlss && store.Get("dlss", "quality") != "off";
            boxes["build"].Enabled = Settings.DiagThere();
            filling = false;
        }

        // The options changed since this panel was made that need a restart.
        public int WaitingForRestart()
        {
            int n = 0;
            foreach (Opt o in options)
                if (!o.Live && atStart[o.Key] != store.Get(o.Key, o.Otherwise)) n++;
            return n;
        }

        void ShowHint(Opt o)
        {
            string more = "";
            if ((o.Key == "dlss" || o.Key == "framegen") && !Settings.DlssUsable(store, card))
                more = Settings.SeriesS(store) ? " (Not with the Series S profile.)"
                    : !card.Nvidia && card.Gb > 0 ? " (No NVIDIA card found.)" : " (Set the Streamline folder in the launcher window first.)";
            Hint.Text = o.Hint + more;
        }

        void OnPick(object sender, EventArgs e)
        {
            if (filling) return;
            ComboBox c = (ComboBox)sender;
            Opt o = (Opt)c.Tag;
            if (c.SelectedIndex < 0) return;
            store.Set(o.Key, o.Values[c.SelectedIndex]);
            store.Save();
            Fill();
            ShowHint(o);
            if (Changed != null) Changed(o);
        }
    }

    class LoadingForm : Form
    {
        readonly Label stage, detail;
        readonly ProgressBar bar;
        readonly Button skip;
        public event Action Cancelled, Skipped;

        public LoadingForm()
        {
            Text = "Foxhound";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ControlBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Back;
            AutoScaleDimensions = new SizeF(96f, 96f); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 236);
            Label head = Theme.Label("Getting ready", Theme.Head, Theme.Text);
            head.Location = new Point(24, 18);
            stage = Theme.Label("", Theme.Body, Theme.Text);
            stage.Location = new Point(26, 66);
            bar = new ProgressBar();
            bar.Location = new Point(26, 94); bar.Size = new Size(508, 14);
            bar.Style = ProgressBarStyle.Marquee; bar.MarqueeAnimationSpeed = 25;
            detail = new Label();
            detail.Font = Theme.Small; detail.ForeColor = Theme.Dim;
            detail.Location = new Point(26, 114); detail.Size = new Size(508, 66);
            Button cancel = Theme.Button("Cancel", false);
            cancel.Location = new Point(444, 186); cancel.Width = 90;
            cancel.Click += delegate { if (Cancelled != null) Cancelled(); };
            skip = Theme.Button("Skip: compile while playing", false);
            skip.Location = new Point(236, 186); skip.Width = 198;
            skip.Visible = false;
            skip.Click += delegate { skip.Enabled = false; if (Skipped != null) Skipped(); };
            Controls.AddRange(new Control[] { head, stage, bar, detail, skip, cancel });
        }

        // done < 0 or total <= 0: no known end (the bar runs to and fro).
        // The pipelines can be left to the run (it compiles what is missing while playing).
        public void CanSkip(bool can) { skip.Visible = can; skip.Enabled = can; }

        public void Show_(string stageText, string detailText, int done, int total)
        {
            stage.Text = stageText;
            detail.Text = detailText;
            if (total > 0 && done >= 0)
            {
                bar.Style = ProgressBarStyle.Continuous;
                bar.Maximum = total; bar.Value = Math.Min(done, total);
            }
            else bar.Style = ProgressBarStyle.Marquee;
        }
    }

    // The settings while playing (F1).
    class MenuForm : Form
    {
        readonly OptionsPanel panel;
        readonly Label waiting;
        public event Action Back, Restart, Quit;
        public event Action<Opt> Changed;

        public MenuForm(Store store, Card card)
        {
            Text = "Foxhound settings";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true; KeyPreview = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Back;
            AutoScaleDimensions = new SizeF(96f, 96f); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(680, 520);
            Padding = new Padding(1);
            Label head = Theme.Label("Settings", Theme.Head, Theme.Text);
            head.Location = new Point(28, 20);
            Label note = Theme.Label("F1 or Esc: back to the game. The game keeps running behind this menu.", Theme.Small, Theme.Dim);
            note.Location = new Point(30, 58);
            panel = new OptionsPanel(store, card, true, 624);
            panel.Location = new Point(28, 88);
            panel.Changed += delegate(Opt o) { Count(); if (Changed != null) Changed(o); };
            waiting = Theme.Label("", Theme.Small, Theme.Accent);
            waiting.Location = new Point(30, 88 + panel.Height + 2);
            Label other = Theme.Label("Game folder, Streamline folder and further switches: in the launcher window, after quitting.", Theme.Small, Theme.Dim);
            other.Location = new Point(30, 88 + panel.Height + 22);
            int by = 88 + panel.Height + 52;
            Button back = Theme.Button("Back to the game", true);
            back.Location = new Point(28, by); back.Width = 190;
            back.Click += delegate { if (Back != null) Back(); };
            Button restart = Theme.Button("Restart the game", false);
            restart.Location = new Point(232, by); restart.Width = 190;
            restart.Click += delegate
            {
                if (Ask("Restart the game now?\n\nSettings marked \"after restart\" take effect. Progress since the title's last save is lost.") && Restart != null) Restart();
            };
            Button quit = Theme.Button("Quit the game", false);
            quit.Location = new Point(436, by); quit.Width = 216;
            quit.Click += delegate
            {
                if (Ask("Quit the game?\n\nProgress since the title's last save is lost.") && Quit != null) Quit();
            };
            Controls.AddRange(new Control[] { head, note, panel, waiting, other, back, restart, quit });
            ClientSize = new Size(680, by + 34 + 26);
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape && Back != null) Back(); };
            Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Accent)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            };
            Count();
        }

        bool Ask(string text)
        {
            return MessageBox.Show(this, text, "Foxhound", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        void Count()
        {
            int n = panel.WaitingForRestart();
            waiting.Text = n == 0 ? "" : n == 1 ? "1 changed setting takes effect after a restart of the game." : n + " changed settings take effect after a restart of the game.";
        }

        public void Refill() { panel.Fill(); Count(); }
    }

    class MainForm : Form
    {
        readonly Store store = Store.Load();
        readonly Card card = Card.Detect();
        readonly string here = Path.GetDirectoryName(Application.ExecutablePath);
        readonly TextBox gameBox, streamlineBox, extraBox;
        readonly Label status;
        readonly OptionsPanel panel;
        readonly System.Windows.Forms.Timer watch = new System.Windows.Forms.Timer();

        LoadingForm loading;
        MenuForm menu;
        Process game;          // the running title
        Process tool;          // a preparing tool
        volatile bool cancelled;
        StreamWriter log;
        string logPath = "";
        IntPtr window = IntPtr.Zero;
        DateTime windowSeen;
        int displayApplied;    // how often "Display" was applied to this window
        bool haveOriginal;
        Native.RECT originalRect, originalClient;
        IntPtr originalStyle;
        bool f1Down, f2Down;
        bool quitting;

        string Victory { get { return Path.Combine(here, Settings.Diag(store) ? "victory-diag.exe" : "victory.exe"); } }

        readonly bool playAtOnce;

        // Arguments: --game <folder or executable> sets the game folder; --play starts at once
        // with the saved settings (for a shortcut that skips this window).
        public MainForm(string[] args)
        {
            for (int n = 0; n < args.Length; n++)
            {
                if (args[n] == "--play") playAtOnce = true;
                else if (args[n] == "--game" && n + 1 < args.Length)
                {
                    string given = args[++n];
                    string exe = File.Exists(given) ? given : Directory.Exists(given) ? Settings.FindExecutable(given) : null;
                    if (exe != null) { store.Set("game_exe", Path.GetFullPath(exe)); store.Save(); }
                }
            }
            Text = "Foxhound";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Back;
            AutoScaleDimensions = new SizeF(96f, 96f); AutoScaleMode = AutoScaleMode.Dpi;
            const int w = 600, x = 28;
            Label head = Theme.Label("Foxhound", Theme.Head, Theme.Text);
            head.Location = new Point(x, 18);
            Label sub = Theme.Label("Runs your own copy of the title. No game files are included.", Theme.Small, Theme.Dim);
            sub.Location = new Point(x + 2, 56);
            int y = 88;
            gameBox = PathRow("Game folder", x, y, w, PickGame);
            y += 34;
            streamlineBox = PathRow("Streamline folder", x, y, w, PickStreamline);
            y += 42;
            if (!store.Has("resolution") && card.Gb > 0 && card.Gb < 11) store.Set("resolution", "1080");
            if (!store.Has("dlss") && Settings.StreamlineDir(store).Length == 0) FindStreamline();
            panel = new OptionsPanel(store, card, false, w);
            panel.Location = new Point(x, y);
            y += panel.Height + 4;
            Label extraLabel = Theme.Label("Further switches", Theme.Body, Theme.Text);
            extraLabel.Location = new Point(x, y + 4);
            extraBox = new TextBox();
            extraBox.Multiline = true; extraBox.ScrollBars = ScrollBars.Vertical;
            extraBox.BackColor = Theme.Panel; extraBox.ForeColor = Theme.Text; extraBox.BorderStyle = BorderStyle.FixedSingle;
            extraBox.Font = new Font("Consolas", 9f);
            extraBox.Location = new Point(x + 130, y); extraBox.Size = new Size(w - 130, 50);
            extraBox.Text = store.Get("extra", "").Replace("\n", "\r\n");
            extraBox.Enter += delegate { panel.Hint.Text = "One NAME=value a line, for example AMBIENT=0.6 (VICTORY_ is put in front). The switches are described in the README and in the code where each is read. They override the lists above."; };
            extraBox.Leave += delegate { store.Set("extra", extraBox.Text.Replace("\r", "")); store.Save(); };
            y += 60;
            status = new Label();
            status.Font = Theme.Small; status.ForeColor = Theme.Accent;
            status.Location = new Point(x, y); status.Size = new Size(w, 34);
            y += 38;
            Button play = Theme.Button("Play", true);
            play.Location = new Point(x, y); play.Size = new Size(210, 40);
            play.Click += delegate { Play(); };
            Button shortcut = Theme.Button("Desktop shortcut", false);
            shortcut.Location = new Point(x + 224, y + 3); shortcut.Width = 130;
            shortcut.Click += delegate { status.Text = MakeShortcut() ? "A shortcut to this launcher is on the desktop." : "The shortcut could not be made."; };
            Button logs = Theme.Button("Logs", false);
            logs.Location = new Point(x + 364, y + 3); logs.Width = 100;
            logs.Click += delegate { Directory.CreateDirectory(Store.LogDir); Process.Start("explorer.exe", "\"" + Store.LogDir + "\""); };
            Button quit = Theme.Button("Close", false);
            quit.Location = new Point(x + 474, y + 3); quit.Width = 126;
            quit.Click += delegate { Close(); };
            Controls.AddRange(new Control[] { head, sub, panel, extraLabel, extraBox, status, play, shortcut, logs, quit });
            ClientSize = new Size(w + 2 * x, y + 62);
            AcceptButton = play;
            watch.Interval = 40;
            watch.Tick += delegate { Watch(); };
            Shown += delegate { FirstStart(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                store.Set("extra", extraBox.Text.Replace("\r", ""));
                store.Save();
                if (game != null && !quitting) { e.Cancel = true; Hide(); }
            };
            ShowPaths();
            panel.Fill();
        }

        TextBox PathRow(string label, int x, int y, int w, Action pick)
        {
            Label l = Theme.Label(label, Theme.Body, Theme.Text);
            l.Location = new Point(x, y + 5);
            TextBox t = new TextBox();
            t.ReadOnly = true; t.BackColor = Theme.Panel; t.ForeColor = Theme.Text; t.BorderStyle = BorderStyle.FixedSingle;
            t.Font = Theme.Body;
            t.Location = new Point(x + 130, y + 1); t.Width = w - 130 - 96;
            Button b = Theme.Button("Change...", false);
            b.Location = new Point(x + w - 88, y - 1); b.Size = new Size(88, 28);
            b.Click += delegate { pick(); };
            Controls.AddRange(new Control[] { l, t, b });
            return t;
        }

        void ShowPaths()
        {
            string exe = store.Get("game_exe", "");
            gameBox.Text = exe.Length > 0 ? Path.GetDirectoryName(exe) : "(not set)";
            string sl = Settings.StreamlineDir(store);
            streamlineBox.Text = sl.Length > 0 ? sl : "(not set: no DLSS)";
        }

        void FirstStart()
        {
            if (!File.Exists(Victory))
            {
                status.Text = "victory.exe is not beside this launcher. Keep the files of the release together.";
                return;
            }
            string exe = store.Get("game_exe", "");
            if (exe.Length == 0 || !File.Exists(exe))
            {
                MessageBox.Show(this, "Welcome.\n\nChoose the folder that holds your own copy of the title (the folder with its .exe). It is asked once.", "Foxhound", MessageBoxButtons.OK, MessageBoxIcon.Information);
                PickGame();
            }
            if (store.Get("shortcut_made", "0") != "1" && File.Exists(store.Get("game_exe", "")))
            {
                if (MakeShortcut()) status.Text = "A shortcut to this launcher was put on the desktop.";
                store.Set("shortcut_made", "1");
                store.Save();
            }
            if (playAtOnce && File.Exists(store.Get("game_exe", ""))) Play();
        }

        void PickGame()
        {
            while (true)
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    d.Description = "The folder that holds your own copy of the title (the folder with its .exe).";
                    d.ShowNewFolderButton = false;
                    string now = store.Get("game_exe", "");
                    if (now.Length > 0) d.SelectedPath = Path.GetDirectoryName(now);
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    string exe = Settings.FindExecutable(d.SelectedPath);
                    if (exe != null)
                    {
                        store.Set("game_exe", exe);
                        store.Save();
                        ShowPaths();
                        status.Text = "";
                        return;
                    }
                    if (MessageBox.Show(this, "The title's executable was not found in\n" + d.SelectedPath + "\n(nor one folder below). Choose another folder?", "Foxhound", MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) != DialogResult.Retry) return;
                }
            }
        }

        // The folder of sl.interposer.dll: the folder given, or below it (an SDK's bin\x64).
        static string InterposerFolder(string folder, int depth)
        {
            try
            {
                if (File.Exists(Path.Combine(folder, "sl.interposer.dll"))) return folder;
                if (depth == 0) return null;
                foreach (string sub in Directory.GetDirectories(folder))
                {
                    string name = Path.GetFileName(sub).ToLowerInvariant();
                    if (name == "development" || name == "arm64" || name == "x86") continue;   // unsigned or other builds
                    string found = InterposerFolder(sub, depth - 1);
                    if (found != null) return found;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        void FindStreamline()
        {
            foreach (string guess in new string[] { Environment.GetEnvironmentVariable("VICTORY_STREAMLINE_DIR"), Path.Combine(here, "streamline") })
            {
                if (string.IsNullOrEmpty(guess) || !Directory.Exists(guess)) continue;
                string found = InterposerFolder(guess, 3);
                if (found != null) { store.Set("streamline_dir", found); return; }
            }
            if (!store.Has("dlss")) store.Set("dlss", "off");
        }

        void PickStreamline()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "The folder of NVIDIA's Streamline SDK (the one you downloaded from NVIDIA and unpacked; its bin\\x64 holds sl.interposer.dll).";
                d.ShowNewFolderButton = false;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string found = InterposerFolder(d.SelectedPath, 3);
                if (found == null)
                {
                    MessageBox.Show(this, "No sl.interposer.dll in that folder or below it.", "Foxhound", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                store.Set("streamline_dir", found);
                if (store.Get("dlss", "off") == "off") store.Set("dlss", "quality");
                store.Save();
                ShowPaths();
                panel.Fill();
            }
        }

        bool MakeShortcut()
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Foxhound.lnk");
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                Type t = link.GetType();
                t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { Application.ExecutablePath });
                t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { here });
                t.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Foxhound launcher" });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                return true;
            }
            catch (Exception) { return false; }
        }

        // ---- preparing and starting ----

        void Play()
        {
            if (game != null || loading != null) return;
            store.Set("extra", extraBox.Text.Replace("\r", ""));
            store.Save();
            if (!File.Exists(Victory)) { status.Text = "victory.exe is not beside this launcher."; return; }
            string exe = store.Get("game_exe", "");
            if (exe.Length == 0 || !File.Exists(exe)) { PickGame(); exe = store.Get("game_exe", ""); if (exe.Length == 0 || !File.Exists(exe)) return; }
            Dictionary<string, string> env = Settings.Environment_(store, card);
            env["VICTORY_GAME_DIR"] = Path.GetDirectoryName(exe);
            string console = Settings.SeriesS(store) ? "series-s" : "series-x";
            cancelled = false;
            loading = new LoadingForm();
            loading.Skipped += delegate { KillTree(tool); };
            loading.Cancelled += delegate { cancelled = true; KillTree(tool); KillTree(game); };
            loading.Show();
            Hide();
            Thread t = new Thread(delegate() { Prepare(exe, console, env); });
            t.IsBackground = true;
            t.Start();
        }

        void Ui(Action a)
        {
            try { if (IsHandleCreated) BeginInvoke(a); }
            catch (InvalidOperationException) { }
        }

        void Stage(string stage, string detail, int done, int total)
        {
            Ui(delegate { if (loading != null) loading.Show_(stage, detail, done, total); });
        }

        // (Worker thread.) Shaders, pipelines, then the title.
        void Prepare(string exe, string console, Dictionary<string, string> env)
        {
            try
            {
                Directory.CreateDirectory(Store.LogDir);
                Dictionary<string, string> toolEnv = new Dictionary<string, string>(env, StringComparer.OrdinalIgnoreCase);
                // The tools cannot ask the card as a run does: NVIDIA and Intel cards run waves of
                // 32 lanes, AMD cards the console's 64.
                if (!card.Amd) toolEnv["VICTORY_HOST_WAVE"] = "32";
                FileInfo layer = new FileInfo(Victory);
                List<string> parts = new List<string>();
                foreach (KeyValuePair<string, string> p in toolEnv)
                    if (Array.IndexOf(Settings.NotOfShaders, p.Key.ToUpperInvariant()) < 0) parts.Add(p.Key + "=" + p.Value);
                parts.Sort(StringComparer.Ordinal);
                string stamp = layer.Length + " " + layer.LastWriteTimeUtc.Ticks + " " + Path.GetDirectoryName(exe) + " " + string.Join(" ", parts.ToArray());
                string stampFile = Path.Combine(Store.Dir, "prepared.txt");
                string before = File.Exists(stampFile) ? File.ReadAllText(stampFile) : "";
                string prepLog = Path.Combine(Store.LogDir, "prepare.log");
                File.WriteAllText(prepLog, "");
                DateTime began = DateTime.Now;
                if (before != stamp)
                {
                    int total = 0;
                    int.TryParse(store.Get("shader_total", "0"), out total);
                    int last = 0;
                    Stage("Translating the title's shaders", "Once per version of Foxhound and set of switches: a few minutes.", -1, 0);
                    int code = RunTool("shaders \"" + Path.GetDirectoryName(exe) + "\"", toolEnv, prepLog, delegate(string line)
                    {
                        Match m = Regex.Match(line, @"^(\d+) shaders in the cache");
                        if (!m.Success) return;
                        last = int.Parse(m.Groups[1].Value);
                        Stage("Translating the title's shaders", string.Format("{0:N0} shaders done, {1:0} s. Once per version of Foxhound and set of switches.", last, (DateTime.Now - began).TotalSeconds), last, total);
                    });
                    if (cancelled) { Ui(delegate { BackToLauncher("Cancelled."); }); return; }
                    if (code != 0) { Ui(delegate { BackToLauncher("The shaders could not be translated (see prepare.log under Logs). Is the game folder right?"); }); return; }
                    File.WriteAllText(stampFile, stamp);
                    if (last > 0) { int t = last; Ui(delegate { store.Set("shader_total", t.ToString()); store.Save(); }); }
                }
                began = DateTime.Now;
                Stage("Compiling pipelines", "Those earlier runs asked for. Seconds; much longer after an update of Foxhound or of the graphics driver.", -1, 0);
                int known = -1;
                Ui(delegate { if (loading != null) loading.CanSkip(true); });
                RunTool("warm", toolEnv, prepLog, delegate(string line)
                {
                    Match k = Regex.Match(line, @"^(\d+) pipelines known");
                    if (k.Success) known = int.Parse(k.Groups[1].Value);
                    Match m = Regex.Match(line, @"^(\d+) of (\d+), ");
                    if (!m.Success) return;
                    int done = int.Parse(m.Groups[1].Value), all = int.Parse(m.Groups[2].Value);
                    double spent = (DateTime.Now - began).TotalSeconds;
                    double left = done > 0 ? spent * (all - done) / done : 0;
                    string rest = left >= 90 ? string.Format("about {0:0} minutes left", left / 60) : string.Format("about {0:0} s left", left);
                    string slow = left >= 600 ? " The graphics driver is compiling them anew (after an update, or the first time from this folder). Skip starts the game now; they are then compiled as scenes need them, with a slow picture at first." : "";
                    Stage("Compiling pipelines", string.Format("{0:N0} of {1:N0}, {2}.", done, all, rest) + slow, done, all);
                });
                Ui(delegate { if (loading != null) loading.CanSkip(false); });
                if (cancelled) { Ui(delegate { BackToLauncher("Cancelled."); }); return; }
                Stage("Starting the title", known == 0
                    ? "First run on this PC: every scene compiles its pipelines the first time it is shown. For about a minute each the picture is slow and objects are missing. This is remembered; the next start compiles them here, ahead. F1 while playing: settings."
                    : "The window appears in about half a minute. F1 while playing: settings, restart, quit.", -1, 0);
                Ui(delegate { Start(exe, console, env); });
            }
            catch (Exception e)
            {
                string text = e.Message;
                Ui(delegate { BackToLauncher("Could not prepare: " + text); });
            }
        }

        static ProcessStartInfo Info(string exe, string arguments, string folder, Dictionary<string, string> env)
        {
            ProcessStartInfo i = new ProcessStartInfo(exe, arguments);
            i.UseShellExecute = false; i.CreateNoWindow = true;
            i.RedirectStandardOutput = true; i.RedirectStandardError = true;
            i.StandardOutputEncoding = Encoding.UTF8; i.StandardErrorEncoding = Encoding.UTF8;
            i.WorkingDirectory = folder;
            // Switches left over in this session do not reach the run.
            List<string> old = new List<string>();
            foreach (System.Collections.DictionaryEntry e in i.EnvironmentVariables)
                if (e.Key.ToString().StartsWith("VICTORY_", StringComparison.OrdinalIgnoreCase)) old.Add(e.Key.ToString());
            foreach (string k in old) i.EnvironmentVariables.Remove(k);
            foreach (KeyValuePair<string, string> p in env) i.EnvironmentVariables[p.Key] = p.Value;
            return i;
        }

        int RunTool(string arguments, Dictionary<string, string> env, string logFile, Action<string> line)
        {
            object gate = new object();
            using (StreamWriter w = new StreamWriter(logFile, true, new UTF8Encoding(false)))
            using (Process p = new Process())
            {
                w.AutoFlush = true;
                p.StartInfo = Info(Victory, arguments, here, env);
                DataReceivedEventHandler on = delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (gate) { w.WriteLine(e.Data); }
                    line(e.Data);
                };
                p.OutputDataReceived += on;
                p.ErrorDataReceived += on;
                lock (gate) { w.WriteLine("> victory.exe " + arguments); }
                p.Start();
                tool = p;
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                p.WaitForExit();
                tool = null;
                return p.ExitCode;
            }
        }

        static void KillTree(Process p)
        {
            if (p == null) return;
            try
            {
                if (p.HasExited) return;
                ProcessStartInfo i = new ProcessStartInfo("taskkill.exe", "/PID " + p.Id + " /T /F");
                i.UseShellExecute = false; i.CreateNoWindow = true;
                using (Process k = Process.Start(i)) k.WaitForExit(5000);
            }
            catch (Exception) { }
        }

        void Start(string exe, string console, Dictionary<string, string> env)
        {
            if (cancelled) { BackToLauncher("Cancelled."); return; }
            try
            {
                // The last 8 logs are kept.
                string[] old = Directory.GetFiles(Store.LogDir, "foxhound-*.log");
                Array.Sort(old, StringComparer.Ordinal);
                for (int n = 0; n < old.Length - 7; n++) { try { File.Delete(old[n]); } catch (IOException) { } }
                logPath = Path.Combine(Store.LogDir, "foxhound-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                log = new StreamWriter(logPath, false, new UTF8Encoding(false));
                log.AutoFlush = true;
                List<string> parts = new List<string>();
                foreach (KeyValuePair<string, string> p in env) if (p.Key != "VICTORY_GAME_DIR") parts.Add(p.Key + "=" + p.Value);
                parts.Sort(StringComparer.Ordinal);
                log.WriteLine("# launcher " + Application.ProductVersion + "; card: " + card.Name + string.Format(" {0:0.#} GB", card.Gb) + "; console=" + console + "; display=" + store.Get("display", "fullscreen") + "; switches: " + string.Join(" ", parts.ToArray()));
                Process p2 = new Process();
                p2.StartInfo = Info(Victory, "run \"" + exe + "\" --console " + console + " --execute", here, env);
                StreamWriter w = log;
                DataReceivedEventHandler on = delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) return;
                    lock (w) { try { w.WriteLine(e.Data); } catch (ObjectDisposedException) { } }
                };
                p2.OutputDataReceived += on;
                p2.ErrorDataReceived += on;
                p2.Start();
                p2.BeginOutputReadLine(); p2.BeginErrorReadLine();
                game = p2;
                SetPriority();
                window = IntPtr.Zero; displayApplied = 0; haveOriginal = false; f1Down = false;
                watch.Start();
            }
            catch (Exception e)
            {
                BackToLauncher("The title could not be started: " + e.Message);
            }
        }

        void BackToLauncher(string text)
        {
            watch.Stop();
            if (loading != null) { loading.Close(); loading = null; }
            if (menu != null) { menu.Close(); menu = null; }
            if (game != null) { try { game.Dispose(); } catch (Exception) { } game = null; }
            if (log != null) { lock (log) { log.Dispose(); } log = null; }
            window = IntPtr.Zero;
            status.Text = text;
            panel.Fill();
            Show();
            Activate();
        }

        // ---- while the title runs (every 40 ms) ----

        void Watch()
        {
            if (game == null) return;
            if (game.HasExited)
            {
                int code = 0;
                try { code = game.ExitCode; } catch (InvalidOperationException) { }
                if (quitting) { Application.Exit(); return; }
                BackToLauncher(window == IntPtr.Zero
                    ? "The title ended before its window appeared (code " + code + "). The reason is at the end of the newest log (Logs)."
                    : "The title ended (code " + code + "). Log: " + Path.GetFileName(logPath));
                return;
            }
            if (window == IntPtr.Zero)
            {
                game.Refresh();
                IntPtr h = game.MainWindowHandle;
                if (h == IntPtr.Zero) return;
                window = h; windowSeen = DateTime.Now;
                if (loading != null) { loading.Close(); loading = null; }
            }
            if (!Native.IsWindow(window)) { window = IntPtr.Zero; return; }
            // The window is given its size by the title in its first seconds: "Display" is
            // applied 3 s after it appeared, and once more at 10 s.
            double age = (DateTime.Now - windowSeen).TotalSeconds;
            if ((displayApplied == 0 && age >= 3) || (displayApplied == 1 && age >= 10))
            {
                ApplyDisplay(displayApplied == 0);
                displayApplied++;
            }
            bool down = (Native.GetAsyncKeyState(0x70) & 0x8000) != 0;   // F1
            if (down && !f1Down)
            {
                uint front = Native.ProcessOf(Native.GetForegroundWindow());
                if (menu != null && menu.Visible) CloseMenu();
                else if (front == (uint)game.Id) OpenMenu();
            }
            f1Down = down;
            // F2 (diagnostics build): the frame being drawn is saved.
            bool save = (Native.GetAsyncKeyState(0x71) & 0x8000) != 0;
            if (save && !f2Down && Settings.Diag(store) && Native.ProcessOf(Native.GetForegroundWindow()) == (uint)game.Id)
            {
                try
                {
                    Directory.CreateDirectory(Settings.CaptureDir);
                    File.WriteAllText(Path.Combine(Settings.CaptureDir, "go"), "go");
                    File.AppendAllText(Path.Combine(Settings.CaptureDir, "notes.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " F2, log " + Path.GetFileName(logPath) + "\r\n");
                }
                catch (IOException) { }
            }
            f2Down = save;
        }

        Native.RECT Monitor(out Native.RECT work)
        {
            Native.MONITORINFO info = new Native.MONITORINFO();
            info.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO));
            Native.GetMonitorInfo(Native.MonitorFromWindow(window, 2), ref info);
            work = info.rcWork;
            return info.rcMonitor;
        }

        void ApplyDisplay(bool front)
        {
            if (window == IntPtr.Zero || !Native.IsWindow(window)) return;
            if (!haveOriginal)
            {
                Native.GetWindowRect(window, out originalRect);
                Native.GetClientRect(window, out originalClient);
                originalStyle = Native.GetWindowLongPtr(window, Native.GWL_STYLE);
                haveOriginal = true;
            }
            Native.RECT work;
            Native.RECT m = Monitor(out work);
            string mode = store.Get("display", "fullscreen");
            if (mode == "fullscreen")
            {
                Native.SetWindowLongPtr(window, Native.GWL_STYLE, new IntPtr(Native.WS_POPUP_VISIBLE));
                Native.SetWindowPos(window, IntPtr.Zero, m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top, Native.SWP_FRAMECHANGED_SHOW);
            }
            else
            {
                bool border = mode == "windowed";
                // The picture's own size, made smaller (same shape) where the monitor is smaller.
                int cw = Math.Max(originalClient.Right, 320), ch = Math.Max(originalClient.Bottom, 180);
                int frameW = border ? (originalRect.Right - originalRect.Left) - originalClient.Right : 0;
                int frameH = border ? (originalRect.Bottom - originalRect.Top) - originalClient.Bottom : 0;
                Native.RECT area = border ? work : m;
                double fit = Math.Min(1.0, Math.Min((area.Right - area.Left - frameW) / (double)cw, (area.Bottom - area.Top - frameH) / (double)ch));
                int ww = (int)(cw * fit) + frameW, wh = (int)(ch * fit) + frameH;
                Native.SetWindowLongPtr(window, Native.GWL_STYLE, border ? originalStyle : new IntPtr(Native.WS_POPUP_VISIBLE));
                Native.SetWindowPos(window, IntPtr.Zero, area.Left + (area.Right - area.Left - ww) / 2, area.Top + (area.Bottom - area.Top - wh) / 2, ww, wh, Native.SWP_FRAMECHANGED_SHOW);
            }
            if (front) Native.Front(window);
        }

        void SetPriority()
        {
            if (game == null) return;
            try
            {
                string p = store.Get("priority", "normal");
                game.PriorityClass = p == "high" ? ProcessPriorityClass.High : p == "above" ? ProcessPriorityClass.AboveNormal : ProcessPriorityClass.Normal;
            }
            catch (Exception) { }
        }

        void OpenMenu()
        {
            if (menu == null)
            {
                menu = new MenuForm(store, card);
                menu.Back += CloseMenu;
                menu.Changed += delegate(Opt o)
                {
                    if (o.Key == "display") { ApplyDisplay(false); PlaceMenu(); Native.Front(menu.Handle); }
                    if (o.Key == "priority") SetPriority();
                };
                menu.Restart += delegate { RestartGame(); };
                menu.Quit += delegate { quitting = true; EndGame(); };
            }
            menu.Refill();
            PlaceMenu();
            Native.ClipCursor(IntPtr.Zero);
            menu.Show();
            Native.Front(menu.Handle);
        }

        void PlaceMenu()
        {
            Native.RECT work;
            Native.RECT m = Monitor(out work);
            menu.Location = new Point(m.Left + (m.Right - m.Left - menu.Width) / 2, m.Top + (m.Bottom - m.Top - menu.Height) / 2);
        }

        void CloseMenu()
        {
            if (menu != null) menu.Hide();
            if (window != IntPtr.Zero && Native.IsWindow(window)) Native.Front(window);
        }

        // Asks the window to close, then ends the process (the title has no way out of its own).
        void EndGame()
        {
            if (menu != null) menu.Hide();
            Process p = game;
            if (p == null) return;
            try
            {
                if (!p.HasExited) { p.CloseMainWindow(); if (!p.WaitForExit(2500)) KillTree(p); p.WaitForExit(5000); }
            }
            catch (Exception) { }
        }

        void RestartGame()
        {
            EndGame();
            BackToLauncher("");
            Play();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool first;
            using (Mutex one = new Mutex(true, "FoxhoundLauncher-7c1e", out first))
            {
                if (!first)
                {
                    MessageBox.Show("Foxhound is running already (press F1 in the game for its settings).", "Foxhound", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.Run(new MainForm(args));
            }
        }
    }
}
