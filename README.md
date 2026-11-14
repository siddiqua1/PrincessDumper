# PrincessDumper

Read-only BepInEx 6 (IL2CPP) plugin that dumps the in-memory image of loaded modules.
Packed modules such as `coneshell.dll` are only decrypted after the game loads them,
so dumping from inside the process yields the real code and data.

Nothing in the game is patched or hooked; the plugin only reads its own process memory.

## Usage

1. Build: `dotnet build -c Release` (set `-p:GameDir=<game folder>` if the game is not in `%USERPROFILE%\priconner`).
2. Copy `PrincessDumper/bin/Release/net6.0/PrincessDumper.dll` to `<game>/BepInEx/plugins/`.
3. Start the game. Dumps are written to `<game>/BepInEx/dumps/`:
   - `<module>-<timestamp>.mem.bin` — raw mapped image (offsets = RVAs)
   - `<module>-<timestamp>.unmapped.dll` — same, with section headers fixed for IDA/Ghidra

Settings live in `BepInEx/config/siddiqua1.PrincessDumper.cfg`:

| Key | Default | Meaning |
| --- | --- | --- |
| `Modules` | `coneshell.dll` | Comma-separated modules to dump (e.g. `coneshell.dll,GameAssembly.dll`) |
| `DelaySeconds` | `20` | Wait after the module loads before dumping |
| `TimeoutSeconds` | `600` | Stop waiting for a module after this long |
| `WriteUnmappedCopy` | `true` | Also write the header-fixed copy |

The original `PrincessDumper.RuntimeDump` API (GameAssembly + embedded global-metadata) is kept.
