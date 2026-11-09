# NOMNOM

`B52J.json` is the B-52's manifest for [NOMNOM](https://github.com/KopterBuzz/NOMNOM), the mod registry the NOMM mod
manager reads. It is not submitted yet. `tools/package.sh` writes each release's artifact into it (zip name, sha256,
download URL); `--pre` marks it `preRelease`, matching a GitHub pre-release.

The release zip already has the layout NOMM installs (the same as the MiG-29 and Heavy Hangar mods): the files at the
top level, unpacked by NOMM into `BepInEx/plugins/B52J/`. Release on GitHub with tag `v<version>` (a plain number:
NOMNOM strips the `v` and rejects suffixes such as `-rc`) and the zip as the first asset (NOMNOM takes the first).

To list the B-52: fork KopterBuzz/NOMNOM, copy `B52J.json` into `modManifests/`, and open a pull request to `main`.
With `autoUpdateArtifacts`, NOMNOM then picks up later releases from this repository by itself.
