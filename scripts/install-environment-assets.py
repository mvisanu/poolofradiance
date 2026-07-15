"""Install the four owned environment packs from Unity's local Asset Store cache.

Licensed source assets remain gitignored.  Small 3D packs are extracted in full (minus
demo scenes and nested render-pipeline packages); the 137 MB grass pack is deliberately
trimmed to the ground textures used by the procedural campaign sites.
"""
from __future__ import annotations

import os
import tarfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CACHE = Path(os.environ["APPDATA"]) / "Unity" / "Asset Store-5.x"

PACKS = {
    "RPG Poly Pack - Lite": "Assets/RPGPP_LT/",
    "Low-Poly Simple Nature Pack": "Assets/SimpleNaturePack/",
    "Low Poly Dungeons Lite": "Assets/LowPolyDungeonsLite/",
}
GRASS_PACK = "Handpainted Grass Ground Textures Free Top-Down RPG Terrain Tool"
GRASS_TEXTURES = {
    "Grass_normal_up.png",
    "Grass_darked_up.png",
    "Grass_corrupted_up.png",
    "Grass_overcorrupted_up.png",
    "Grass_swamp_dark_up.png",
    "Grass_bluetint_up.png",
    "dirt_corrupted_up.png",
    "dirt_claydarked_up.png",
}


def find_package(fragment: str) -> Path:
    matches = sorted(CACHE.rglob(f"*{fragment}*.unitypackage"))
    if not matches:
        raise FileNotFoundError(f"Unity cache has no package matching {fragment!r}")
    return matches[-1]


def records(package: Path):
    with tarfile.open(package, "r:gz") as archive:
        grouped: dict[str, dict[str, tarfile.TarInfo]] = {}
        for member in archive.getmembers():
            parts = member.name.split("/")
            if len(parts) == 2:
                grouped.setdefault(parts[0], {})[parts[1]] = member
        for files in grouped.values():
            path_member = files.get("pathname")
            asset_member = files.get("asset")
            if path_member is None or asset_member is None:
                continue
            source = archive.extractfile(path_member)
            if source is None:
                continue
            pathname = source.read().decode("utf-8", "replace").splitlines()[0]
            yield archive, pathname.replace("\\", "/"), files


def extract(package: Path, accept) -> int:
    count = 0
    for archive, pathname, files in records(package):
        if not accept(pathname):
            continue
        target = ROOT / "game" / pathname
        target.parent.mkdir(parents=True, exist_ok=True)
        source = archive.extractfile(files["asset"])
        if source is None:
            continue
        target.write_bytes(source.read())
        meta = files.get("asset.meta")
        if meta is not None:
            meta_source = archive.extractfile(meta)
            if meta_source is not None:
                Path(str(target) + ".meta").write_bytes(meta_source.read())
        count += 1
    return count


def main() -> None:
    if not CACHE.exists():
        raise FileNotFoundError(f"Unity Asset Store cache not found: {CACHE}")

    for fragment, prefix in PACKS.items():
        package = find_package(fragment)

        def wanted(pathname: str, prefix=prefix) -> bool:
            lower = pathname.lower()
            return (pathname.startswith(prefix)
                    and "/demo" not in lower and "/scene" not in lower
                    and not lower.endswith(".unitypackage"))

        print(f"{fragment}: extracted {extract(package, wanted)} assets")

    grass = find_package(GRASS_PACK)

    def wanted_grass(pathname: str) -> bool:
        return (pathname.startswith("Assets/Handpainted_Grass_and_Ground_Textures/")
                and Path(pathname).name in GRASS_TEXTURES)

    print(f"Handpainted Grass: extracted {extract(grass, wanted_grass)} textures")


if __name__ == "__main__":
    main()
