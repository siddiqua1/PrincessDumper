using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;

namespace PrincessDumper
{
    /// <summary>
    /// Read-only module dumper. Waits for each configured module to be loaded into the game
    /// process, then writes its mapped (decrypted/unpacked) image to BepInEx/dumps.
    /// No game code is patched or hooked.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BasePlugin
    {
        public const string PluginGuid = "siddiqua1.PrincessDumper";
        public const string PluginName = "PrincessDumper";
        public const string PluginVersion = "1.1.0";

        private ConfigEntry<string> _modules = null!;
        private ConfigEntry<int> _delaySeconds = null!;
        private ConfigEntry<int> _timeoutSeconds = null!;
        private ConfigEntry<bool> _writeUnmapped = null!;
        private ConfigEntry<bool> _watch = null!;
        private ConfigEntry<int> _watchIntervalMs = null!;
        private ConfigEntry<int> _watchSeconds = null!;

        public override void Load()
        {
            _modules = Config.Bind("Dump", "Modules", "coneshell.dll",
                "Comma-separated module file names to dump once they are loaded.");
            _delaySeconds = Config.Bind("Dump", "DelaySeconds", 20,
                "Seconds to wait after a module appears before dumping (lets it finish unpacking/initialising).");
            _timeoutSeconds = Config.Bind("Dump", "TimeoutSeconds", 600,
                "Give up waiting for a module after this many seconds.");
            _writeUnmapped = Config.Bind("Dump", "WriteUnmappedCopy", true,
                "Also write a copy with section headers rewritten for static analysis tools.");
            _watch = Config.Bind("Watch", "Enabled", false,
                "After dumping, keep polling the module's writable sections and log changed 16-byte chunks.");
            _watchIntervalMs = Config.Bind("Watch", "IntervalMs", 5,
                "Polling interval in milliseconds.");
            _watchSeconds = Config.Bind("Watch", "DurationSeconds", 600,
                "Stop watching after this many seconds (0 = until the game exits).");

            string outDir = Path.Combine(Paths.BepInExRootPath, "dumps");
            Directory.CreateDirectory(outDir);

            foreach (string name in _modules.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var thread = new Thread(() => DumpWhenLoaded(name, outDir)) { IsBackground = true, Name = $"PrincessDumper:{name}" };
                thread.Start();
            }
            Log.LogInfo($"Watching for: {_modules.Value} (output: {outDir})");
        }

        /// <summary>
        /// Polls the module's writable sections and logs every 16-byte chunk that changes,
        /// one line per chunk: "&lt;unix ms&gt; &lt;rva hex&gt; &lt;16 bytes hex&gt;". Read-only.
        /// </summary>
        private void WatchWritableSections(string moduleName, byte[] initialImage, string logPath)
        {
            var module = PrincessDumper.FindModule(moduleName);
            if (module is null) return;
            long baseAddr = module.BaseAddress.ToInt64();
            var sections = PrincessDumper.GetWritableSections(initialImage);
            var snapshots = sections.Select(s => initialImage.AsSpan((int)s.Rva, (int)s.Size).ToArray()).ToList();
            var current = sections.Select(s => new byte[s.Size]).ToList();
            var deadline = _watchSeconds.Value > 0 ? DateTime.UtcNow.AddSeconds(_watchSeconds.Value) : DateTime.MaxValue;

            Log.LogInfo($"{moduleName}: watching {string.Join(", ", sections.Select(s => $"{s.Name}@0x{s.Rva:X}+0x{s.Size:X}"))} -> {logPath}");
            using var log = new StreamWriter(logPath, append: false) { AutoFlush = false };
            long lines = 0, iterations = 0;
            var lastModuleCheck = DateTime.UtcNow;
            while (DateTime.UtcNow < deadline)
            {
                if ((DateTime.UtcNow - lastModuleCheck).TotalSeconds >= 1)
                {
                    if (PrincessDumper.FindModule(moduleName) is null) break;
                    lastModuleCheck = DateTime.UtcNow;
                }
                iterations++;
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                for (int i = 0; i < sections.Count; i++)
                {
                    if (!PrincessDumper.TryReadMemory((IntPtr)(baseAddr + sections[i].Rva), current[i])) continue;
                    byte[] prev = snapshots[i], cur = current[i];
                    for (int off = 0; off + 16 <= cur.Length; off += 16)
                    {
                        if (!cur.AsSpan(off, 16).SequenceEqual(prev.AsSpan(off, 16)))
                        {
                            log.WriteLine($"{now} {sections[i].Rva + off:x} {Convert.ToHexString(cur, off, 16).ToLowerInvariant()}");
                            lines++;
                        }
                    }
                    (snapshots[i], current[i]) = (cur, prev);
                }
                log.Flush();
                Thread.Sleep(_watchIntervalMs.Value);
            }
            Log.LogInfo($"{moduleName}: watch finished ({lines} changes logged over {iterations} polls)");
        }

        private void DumpWhenLoaded(string moduleName, string outDir)
        {
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(_timeoutSeconds.Value);
                while (PrincessDumper.FindModule(moduleName) is null)
                {
                    if (DateTime.UtcNow > deadline)
                    {
                        Log.LogWarning($"{moduleName}: not loaded within {_timeoutSeconds.Value}s, giving up");
                        return;
                    }
                    Thread.Sleep(1000);
                }

                Log.LogInfo($"{moduleName}: loaded, dumping in {_delaySeconds.Value}s");
                Thread.Sleep(TimeSpan.FromSeconds(_delaySeconds.Value));

                byte[]? image = PrincessDumper.ReadModuleImage(moduleName);
                if (image is null)
                {
                    Log.LogWarning($"{moduleName}: unloaded before dump");
                    return;
                }

                string stem = Path.Combine(outDir, $"{Path.GetFileNameWithoutExtension(moduleName)}-{DateTime.Now:yyyyMMdd-HHmmss}");
                File.WriteAllBytes(stem + ".mem.bin", image);
                if (_writeUnmapped.Value)
                {
                    byte[] copy = (byte[])image.Clone();
                    PrincessDumper.UnmapSectionHeaders(copy);
                    File.WriteAllBytes(stem + ".unmapped" + Path.GetExtension(moduleName), copy);
                }
                Log.LogInfo($"{moduleName}: wrote {stem}.mem.bin ({image.Length} bytes)");

                if (_watch.Value)
                {
                    WatchWritableSections(moduleName, image, stem + ".watch.log");
                }
            }
            catch (Exception e)
            {
                Log.LogError($"{moduleName}: dump failed: {e}");
            }
        }
    }
}
