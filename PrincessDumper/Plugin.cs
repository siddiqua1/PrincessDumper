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

            string outDir = Path.Combine(Paths.BepInExRootPath, "dumps");
            Directory.CreateDirectory(outDir);

            foreach (string name in _modules.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var thread = new Thread(() => DumpWhenLoaded(name, outDir)) { IsBackground = true, Name = $"PrincessDumper:{name}" };
                thread.Start();
            }
            Log.LogInfo($"Watching for: {_modules.Value} (output: {outDir})");
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
            }
            catch (Exception e)
            {
                Log.LogError($"{moduleName}: dump failed: {e}");
            }
        }
    }
}
