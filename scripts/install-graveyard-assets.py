"""Selectively install the owned PBR Graveyard and Nature Set 2.0 package.

The Asset Store archive is more than 3 GB. Importing it whole also brings demo scenes,
legacy effects, and hundreds of unused variants. This installer chooses the authored
prefabs Radiant Pool actually places, follows their Unity GUID references recursively,
and extracts only the required models, materials, PBR textures, and metadata.

Licensed files remain gitignored and must never be committed.
"""
from __future__ import annotations

import os
import re
import gzip
import tarfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
GAME = ROOT / "game"
CACHE = Path(os.environ["APPDATA"]) / "Unity" / "Asset Store-5.x"
PACKAGE_FRAGMENT = "PBR Graveyard and Nature Set 20"
PACKAGE_ROOT = "Assets/NatureManufacture Assets/PBR Graveyard/"
DEPENDENCY_ROOTS = (PACKAGE_ROOT, "Assets/NatureManufacture Assets/Foliage Trial/")

SEEDS = {
    # Church/ruin silhouettes.
    PACKAGE_ROOT + "Prefabs/Architecture/prefab_Building_01.prefab",
    PACKAGE_ROOT + "Prefabs/Architecture/prefab_church_tower.prefab",
    PACKAGE_ROOT + "Prefabs/Architecture/prefab_Wall_17_A_Gate.prefab",
    PACKAGE_ROOT + "Prefabs/Architecture/prefab_Wall_rock_4_Portal_Ivy.prefab",
    # Ground clutter and readable graveyard landmarks.
    *(PACKAGE_ROOT + f"Prefabs/Props/prefab_Grave_{i}.prefab" for i in range(1, 8)),
    PACKAGE_ROOT + "Prefabs/Props/prefab_stone_cross_01.prefab",
    PACKAGE_ROOT + "Prefabs/Props/prefab_stone_cross_02.prefab",
    PACKAGE_ROOT + "Prefabs/Props/prefab_coffin_broken_1.prefab",
    PACKAGE_ROOT + "Prefabs/Props/prefab_death_statue.prefab",
    PACKAGE_ROOT + "Prefabs/Props/prefab_rock_skull_01.prefab",
    PACKAGE_ROOT + "Prefabs/Props/prefab_cart.prefab",
    # Nature Set 2.0 surroundings.
    PACKAGE_ROOT + "Prefabs/Foliage and Enviro/prefab_Big_rock_1_01.prefab",
    PACKAGE_ROOT + "Prefabs/Foliage and Enviro/prefab_Big_rock_2_00.prefab",
    PACKAGE_ROOT + "Prefabs/Foliage and Enviro/prefab_small_rock_02.prefab",
    PACKAGE_ROOT + "Prefabs/Foliage and Enviro/prefab_dead_fern.prefab",
    PACKAGE_ROOT + "Prefabs/Foliage and Enviro/prefab_Ivy_02.prefab",
    PACKAGE_ROOT + "Trees/Prefabs/Prefab_Dead_Tree_1.prefab",
    PACKAGE_ROOT + "Trees/Prefabs/Prefab_Dead_Tree_2.prefab",
    PACKAGE_ROOT + "Trees/Prefabs/Prefab_Dead_Tree_3.prefab",
    PACKAGE_ROOT + "Trees/Prefabs/Prefab_Roots_2.prefab",
    PACKAGE_ROOT + "Trees/Prefabs/Prefab_Roots_3.prefab",
    PACKAGE_ROOT + "Trees/Prefabs/Prefab_Tree_1.prefab",
    PACKAGE_ROOT + "Trees/Prefabs/Prefab_Wild_Rose_1.prefab",
}

GUID = re.compile(rb"guid:\s*([0-9a-fA-F]{32})")
ALLOWED_SUFFIXES = {
    ".prefab", ".fbx", ".obj", ".mat", ".png", ".tga", ".jpg", ".jpeg",
    ".tif", ".tiff", ".shader", ".cginc", ".hlsl", ".asset",
}


def package_path() -> Path:
    matches = sorted(CACHE.rglob(f"*{PACKAGE_FRAGMENT}*.unitypackage"))
    if not matches:
        raise FileNotFoundError(
            "PBR Graveyard and Nature Set 2.0 is not downloaded. In Unity Package "
            "Manager > My Assets, select it and click Download."
        )
    return matches[-1]


def groups(package: Path, include_assets: bool):
    """Stream each Unity package record once; memory is bounded to one archive record."""
    # Unity Asset Store packages carry a large vendor JSON field in the gzip extra
    # header. Python's tarfile streaming gzip wrapper misreads this particular archive,
    # while gzip.GzipFile handles it correctly; feed that decoded stream to tarfile.
    with gzip.open(package, "rb") as decoded_stream, \
            tarfile.open(fileobj=decoded_stream, mode="r|") as archive:
        current_id = None
        current: dict[str, bytes] = {}
        for member in archive:
            group_id, _, leaf = member.name.partition("/")
            if current_id is not None and group_id != current_id:
                yield decoded(current)
                current = {}
            current_id = group_id
            if not member.isfile() or leaf not in {"pathname", "asset.meta", "asset"}:
                continue
            if leaf == "asset" and not include_assets:
                continue
            source = archive.extractfile(member)
            if source is not None:
                current[leaf] = source.read()
        if current_id is not None:
            yield decoded(current)


def decoded(record: dict[str, bytes]):
    pathname = record.get("pathname", b"").decode("utf-8", "replace").splitlines()
    path = pathname[0].replace("\\", "/") if pathname else ""
    return path, record.get("asset", b""), record.get("asset.meta", b"")


def allowed(path: str) -> bool:
    return path.startswith(DEPENDENCY_ROOTS) and Path(path).suffix.lower() in ALLOWED_SUFFIXES


def main() -> None:
    package = package_path()
    print(f"Cataloguing {package.name} ...")

    guid_to_path: dict[str, str] = {}
    package_paths: set[str] = set()
    for path, _asset, meta in groups(package, include_assets=False):
        if not path:
            continue
        package_paths.add(path)
        own_guid = GUID.search(meta)
        if own_guid:
            guid_to_path[own_guid.group(1).decode("ascii").lower()] = path

    missing = sorted(SEEDS - package_paths)
    if missing:
        raise RuntimeError("Package version is missing selected prefabs:\n" + "\n".join(missing))

    needed = set(SEEDS)
    for pass_index in range(8):
        before = len(needed)
        for path, asset, meta in groups(package, include_assets=True):
            if path not in needed:
                continue
            for reference in GUID.findall(asset + b"\n" + meta):
                dependency = guid_to_path.get(reference.decode("ascii").lower())
                if dependency and allowed(dependency):
                    needed.add(dependency)
        print(f"Dependency pass {pass_index + 1}: {len(needed)} assets")
        if len(needed) == before:
            break
    else:
        raise RuntimeError("Graveyard dependency closure did not converge")

    installed = 0
    installed_bytes = 0
    game_root = GAME.resolve()
    for path, asset, meta in groups(package, include_assets=True):
        if path not in needed or not asset:
            continue
        target = (GAME / path).resolve()
        if os.path.commonpath((game_root, target)) != str(game_root):
            raise RuntimeError(f"Package path escapes Unity project: {path}")
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(asset)
        if meta:
            Path(str(target) + ".meta").write_bytes(meta)
        installed += 1
        installed_bytes += len(asset)

    missing_installed = sorted(path for path in SEEDS if not (GAME / path).exists())
    if missing_installed:
        raise RuntimeError("Selected prefabs were not installed:\n" + "\n".join(missing_installed))
    print(f"PBR Graveyard: installed {installed} dependency-closed assets "
          f"({installed_bytes / 1024 / 1024:.1f} MB) from {len(SEEDS)} selected prefabs")


if __name__ == "__main__":
    main()
