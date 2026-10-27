#!/usr/bin/env bash
# Release zip from the current build: build/B-52J_Stratofortress-<version>.zip, holding the B-52J_Stratofortress
# folder players copy into BepInEx/plugins (aircraft, plugin, player readme, licences). Prints the sha256 the mod
# manager manifest needs. Run tools/build_all.sh first.
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$ROOT"

V="$(mod_version)"
PV="$(grep -oE 'BepInPlugin\("[^"]+", "[^"]+", "[^"]+"\)' plugin/B52Systems/Plugin.cs | grep -oE '"[0-9][0-9.]*"' | tr -d '"')"
[[ "$V" == "$PV" ]] || { echo "Version mismatch: builder says $V, plugin says $PV. Set both before packaging."; exit 1; }
NOBP="build/B-52J Stratofortress_$V.nobp"
DLL="build/plugin/B52Systems.dll"
[[ -f "$NOBP" ]] || { echo "Missing $NOBP (run tools/build_all.sh)"; exit 1; }
[[ -f "$DLL" ]] || { echo "Missing $DLL (run tools/build_all.sh)"; exit 1; }
[[ "$DLL" -nt plugin/B52Systems/Plugin.cs ]] || { echo "$DLL is older than the plugin source; rebuild it"; exit 1; }

STAGE="build/package"
DIR="$STAGE/B-52J_Stratofortress"
rm -rf "$STAGE"; mkdir -p "$DIR"
cp "$NOBP" "$DLL" "$DIR/"
cp LICENSE "$DIR/LICENSE-code-MIT.txt"
cp LICENSES/CC-BY-4.0.txt "$DIR/LICENSE-model-CC-BY-4.0.txt"
cp LICENSES/CREDITS-bohmerang-B52.txt "$DIR/CREDITS-bohmerang-B52.txt"
cp NOTICE "$DIR/NOTICE.txt"
sed -e "s/{VERSION}/$V/g" -e "s/{BLUEPRINTER}/2.0.1/g" tools/release_readme.txt > "$DIR/README.txt"
if [[ $IS_WIN == 1 ]]; then   # Windows readers expect CRLF in .txt files opened in Notepad
  for f in "$DIR"/*.txt; do sed -i 's/$/\r/' "$f"; done
fi

ZIP="build/B-52J_Stratofortress-$V.zip"
rm -f "$ZIP"
"$PYTHON" - "$STAGE" "$ZIP" <<'EOF'
import os, sys, zipfile
stage, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for base, _, files in os.walk(stage):
        for f in sorted(files):
            full = os.path.join(base, f)
            z.write(full, os.path.relpath(full, stage).replace(os.sep, "/"))
EOF
echo "Built $ZIP:"
"$PYTHON" -c "import zipfile,sys; [print(f'  {i.file_size:>10}  {i.filename}') for i in zipfile.ZipFile(sys.argv[1]).infolist()]" "$ZIP"
echo "sha256: $(sha256sum "$ZIP" | cut -d' ' -f1)"
