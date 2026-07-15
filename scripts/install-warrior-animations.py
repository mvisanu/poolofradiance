"""Install the licensed Warrior Pack Bundle 2 FREE combat-animation subset.

The Asset Store package also contains demo scenes, character controllers, input code,
and sample models that Radiant Pool neither needs nor should compile.  This selective
installer extracts only the four humanoid Attack1 FBXs (one-hand, two-hand, archer,
and mage) plus their Unity importer metadata into a gitignored local folder.

The source package and extracted FBXs remain subject to the Unity Asset Store EULA.
Never commit them; only the integration code belongs in the public repository.
"""

from __future__ import annotations

import os
from pathlib import Path
import shutil
import sys
import tarfile


PACKAGE = Path(
    "Explosive/AnimationBipedal/Warrior Pack Bundle 2 FREE.unitypackage"
)
TARGETS = {
    "2Handed@Attack1.FBX": "Warrior_2H_Attack.fbx",
    "Archer@Attack1.FBX": "Warrior_Ranged_Attack.fbx",
    "Knight@Attack1.FBX": "Warrior_1H_Attack.fbx",
    "Mage@Attack1.FBX": "Warrior_Cast.fbx",
}


def package_records(archive: tarfile.TarFile):
    """Yield (original path, asset member, meta member) records."""
    grouped: dict[str, dict[str, tarfile.TarInfo]] = {}
    for member in archive.getmembers():
        group, separator, leaf = member.name.partition("/")
        if not separator or leaf not in {"pathname", "asset", "asset.meta"}:
            continue
        grouped.setdefault(group, {})[leaf] = member

    for record in grouped.values():
        pathname = record.get("pathname")
        if pathname is None:
            continue
        source = archive.extractfile(pathname)
        if source is None:
            continue
        with source:
            # Asset Store path records may contain a trailing newline/"00" marker.
            lines = source.read().decode("utf-8", "replace").splitlines()
        if lines:
            yield lines[0].replace("\\", "/"), record.get("asset"), record.get("asset.meta")


def copy_member(archive: tarfile.TarFile, member: tarfile.TarInfo, target: Path) -> None:
    source = archive.extractfile(member)
    if source is None:
        raise RuntimeError(f"Cannot read {member.name}")
    target.parent.mkdir(parents=True, exist_ok=True)
    with source, target.open("wb") as output:
        shutil.copyfileobj(source, output)


def main() -> int:
    repo = Path(__file__).resolve().parents[1]
    appdata = os.environ.get("APPDATA")
    if not appdata:
        raise RuntimeError("APPDATA is not set; the Unity Asset Store cache cannot be located")
    package = Path(appdata) / "Unity" / "Asset Store-5.x" / PACKAGE
    if not package.is_file():
        raise FileNotFoundError(
            f"Missing cached package: {package}\n"
            "Download Warrior Pack Bundle 2 FREE once in Unity Package Manager > "
            "My Assets, then rerun this script."
        )

    destination = repo / "game" / "Assets" / "LocalLicensed" / "WarriorPack2"
    installed: set[str] = set()
    with tarfile.open(package, "r:gz") as archive:
        for original, asset, meta in package_records(archive):
            source_name = Path(original).name
            target_name = TARGETS.get(source_name)
            if target_name is None:
                continue
            if asset is None or meta is None:
                raise RuntimeError(f"Package record is incomplete: {original}")
            target = destination / target_name
            copy_member(archive, asset, target)
            copy_member(archive, meta, Path(str(target) + ".meta"))
            installed.add(source_name)
            print(f"Installed {source_name} -> {target.relative_to(repo)}")

    missing = sorted(set(TARGETS) - installed)
    if missing:
        raise RuntimeError(f"Package is missing expected animations: {missing}")
    print("Warrior Pack install complete: 4 licensed combat clips (local and gitignored).")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print(f"Warrior Pack install FAILED: {error}", file=sys.stderr)
        sys.exit(1)
