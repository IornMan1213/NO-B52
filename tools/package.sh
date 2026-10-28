#!/usr/bin/env bash
# Release zip from the current build, in the layout NOMNOM / NOMM expect (like the MiG-29 and Heavy Hangar mods):
# build/B-52J-Stratofortress_<version>.zip with the files at the top level (aircraft, plugin, player readme,
# licences). NOMM unpacks it into BepInEx/plugins/B52J/; players doing it by hand make that folder. Also writes the
# artifact into nomnom/B52J.json (sha256, download URL). Run tools/build_all.sh first.
#   bash tools/package.sh            a release
#   bash tools/package.sh --pre      a GitHub pre-release (NOMNOM category preRelease)
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$ROOT"

CATEGORY=release; [[ "${1:-}" == --pre ]] && CATEGORY=preRelease
V="$(mod_version)"
PV="$(grep -oE 'BepInPlugin\("[^"]+", "[^"]+", "[^"]+"\)' plugin/B52Systems/Plugin.cs | grep -oE '"[0-9][0-9.]*"' | tr -d '"')"
[[ "$V" == "$PV" ]] || { echo "Version mismatch: builder says $V, plugin says $PV. Set both before packaging."; exit 1; }
grep -q "^## $V" CHANGELOG.md || { echo "CHANGELOG.md has no '## $V' section"; exit 1; }
NOBP="build/B-52J Stratofortress_$V.nobp"
DLL="build/plugin/B52Systems.dll"
[[ -f "$NOBP" ]] || { echo "Missing $NOBP (run tools/build_all.sh)"; exit 1; }
[[ -f "$DLL" ]] || { echo "Missing $DLL (run tools/build_all.sh)"; exit 1; }
[[ "$DLL" -nt plugin/B52Systems/Plugin.cs ]] || { echo "$DLL is older than the plugin source; rebuild it"; exit 1; }

STAGE="build/package"
rm -rf "$STAGE"; mkdir -p "$STAGE"
cp "$NOBP" "$DLL" "$STAGE/"
cp LICENSE "$STAGE/LICENSE-code-MIT.txt"
cp LICENSES/CC-BY-4.0.txt "$STAGE/LICENSE-model-CC-BY-4.0.txt"
cp LICENSES/CREDITS-bohmerang-B52.txt "$STAGE/CREDITS-bohmerang-B52.txt"
cp NOTICE "$STAGE/NOTICE.txt"
sed -e "s/{VERSION}/$V/g" -e "s/{BLUEPRINTER}/2.0.1/g" tools/release_readme.txt > "$STAGE/README.txt"
if [[ $IS_WIN == 1 ]]; then   # Windows readers expect CRLF in .txt files opened in Notepad
  for f in "$STAGE"/*.txt; do sed -i 's/$/\r/' "$f"; done
fi

ZIP="build/B-52J-Stratofortress_$V.zip"
rm -f build/B-52J-Stratofortress_*.zip build/B-52J_Stratofortress-*.zip
"$PYTHON" - "$STAGE" "$ZIP" <<'PY'
import os, sys, zipfile
stage, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for f in sorted(os.listdir(stage)):
        z.write(os.path.join(stage, f), f)            # flat: NOMM extracts the archive as-is into plugins/<id>/
PY
SHA="$(sha256sum "$ZIP" | cut -d' ' -f1)"
echo "Built $ZIP:"
"$PYTHON" -c "import zipfile,sys; [print(f'  {i.file_size:>10}  {i.filename}') for i in zipfile.ZipFile(sys.argv[1]).infolist()]" "$ZIP"
echo "sha256: $SHA"
"$PYTHON" tools/nomnom_manifest.py "$V" "$(basename "$ZIP")" "$SHA" "$CATEGORY"
