# Releasing a new version

1. **Version.** Set the same version in `unity/B52Tools/Editor/B52Builder.cs` (`Version`) and
   `plugin/B52Systems/Plugin.cs` (`[BepInPlugin(..., "x.y.z")]`). `tools/package.sh` refuses to package if they
   differ.
2. **Changelog.** Move the "in progress" notes in [CHANGELOG.md](CHANGELOG.md) under the new version, in player
   language. Update the compatibility table in the README if the game or Blueprinter version changed.
3. **Build** (game closed): `bash tools/build_all.sh`, or `--from-fbx` if no Blender script changed. Check the
   summary lines for `FAILED` or missing hardpoints.
4. **Install and test:** `bash tools/install.sh b52`, then `bash tests/ingame/run.sh tests/ingame/b52_flight.txt`.
   Every `[TEST]` line must be PASS, and `BepInEx/LogOutput.log` must have no exception from B52Systems. Look at the
   `BepInEx/b52_test_*.png` screenshots.
5. **Multiplayer** (for releases that change the plugin): host plus one client, both with the new build. Check the
   chute, gear and weapon release on the other screen.
6. **Package:** `bash tools/package.sh` (`--pre` for a pre-release). It builds the flat zip NOMM installs
   (`build/B-52J-Stratofortress_X.Y.Z.zip`), prints its contents and sha256, and writes the artifact into
   `nomnom/B52J.json`. Commit the manifest.
7. **Release on GitHub:** tag `vX.Y.Z` (a plain number, no `-rc`: NOMNOM rejects suffixes), attach the zip as the
   first asset, and paste the changelog section as the notes. Mark it Latest (not pre-release) once steps 4 and
   5 passed.
8. **Mod manager:** check `nomnom/B52J.json` (game version in `tools/nomnom_manifest.py`) and submit it to
   NOMNOM once (see [nomnom/README.md](nomnom/README.md)); after that NOMNOM updates it from the releases.
9. **Heavy Hangar:** if `plugin/HeavyHangar` or the hangar tools changed, copy the change to
   [NO-HeavyHangar](https://github.com/IornMan1213/NO-HeavyHangar) and release it there too; the two copies stay in
   step.
