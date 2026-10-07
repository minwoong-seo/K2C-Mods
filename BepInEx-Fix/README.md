# BepInEx fix for Kingdom Two Crowns v2.4.2 (Unity 6000.0.66)

The stock BepInEx 6.0.0-be.785 (IL2CPP) can't load **any** plugins on this game version. This folder has the fix,
the original files for undoing it, and the source change. It's the only change made to BepInEx itself.

## What's in this folder

```
BepInEx-Fix/
  README.md
  package/BepInEx/core/Cpp2IL.Core.dll    patched: copy into the game to apply the fix
  package/BepInEx/core/LibCpp2IL.dll
  original/BepInEx/core/Cpp2IL.Core.dll   stock be.785 files: copy into the game to undo the fix
  original/BepInEx/core/LibCpp2IL.dll
  src/cpp2il-property-fix.patch           the source change
```

`package/` and `original/` both use the game folder's layout, so applying or undoing is just copying.

## The problem

On first launch, BepInEx runs Cpp2IL to generate its interop assemblies (`BepInEx/interop`). On this game it
crashes:

```
[Error  :InteropManager] Failed to generate Il2Cpp interop assemblies
Failed to process type AndroidManager+<_InitiateSignIn>d__21_Server (Haglet-Assembly-CSharp-02)
NullReferenceException at LibCpp2IL.Metadata.Il2CppPropertyDefinition.get_RawPropertyType()
[Fatal  :   BepInEx] Unable to execute IL2CPP chainloader, no plugins will be loaded
```

The game's metadata contains properties with neither a getter nor a setter, and Cpp2IL assumes every property has at
least one. Upstream Cpp2IL `2022.1.0-pre-release.21` has the same bug.

## The fix

`src/cpp2il-property-fix.patch` changes two Cpp2IL files:
- `LibCpp2IL/Metadata/Il2CppPropertyDefinition.cs`: the property type and static checks return null/false instead
  of crashing when there's no accessor.
- `Cpp2IL.Core/Model/Contexts/TypeAnalysisContext.cs`: properties whose accessors and type can't be resolved are
  skipped. Mods can't call such a property anyway.

The patch is applied to Cpp2IL commit `558ddd9`, the exact version BepInEx be.785 ships, so the DLLs are drop-in
replacements. No other BepInEx files are changed.

## Apply

1. Install BepInEx 6.0.0-be.785 IL2CPP x64 in the game folder (if it isn't already).
2. Copy everything inside `package/` into the game folder, replacing the two files.
3. Start the game. If `BepInEx/interop` is empty, BepInEx regenerates it on that start, which takes about a minute.
   The log should end with `Chainloader startup complete` and no `Failed to generate` error.

These DLLs only match BepInEx **be.785**. A different BepInEx build needs its own matching Cpp2IL rebuilt with the
patch (see below), or a newer BepInEx release where this is fixed upstream.

## Undo

Copy everything inside `original/` into the game folder. Interop that has already been generated keeps working until
the game updates. When the game updates, BepInEx regenerates interop, which then fails again without the fix.

## Rebuild the DLLs

```
git clone https://github.com/SamboyCoding/Cpp2IL.git
cd Cpp2IL
git checkout 558ddd98642010897d54316b51fbaa7889fda093
git apply path/to/cpp2il-property-fix.patch
dotnet build Cpp2IL.Core/Cpp2IL.Core.csproj -c Release -f net6.0
```

The outputs are `Cpp2IL.Core/bin/Release/net6.0/Cpp2IL.Core.dll` and `LibCpp2IL/bin/Release/net6.0/LibCpp2IL.dll`.
The repo pins .NET SDK 9 in `global.json`. With a newer SDK, change `"rollForward"` to `"latestMajor"`.
