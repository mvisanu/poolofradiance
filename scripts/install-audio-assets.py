"""Install the project's licensed audio subset from Unity's local Asset Store cache.

The source packages remain subject to the Unity Asset Store EULA and must not be
committed. This script extracts only the clips used by the game into gitignored
Resources folders, preserving Unity's importer metadata.
"""

from __future__ import annotations

import os
from pathlib import Path
import shutil
import sys
import tarfile


MUSIC = {
    "Chimera Forge Productions/AudioMusicOrchestral/Action RPG Battle Music.unitypackage": {
        "b6ee0a2b3bf7e466fa844150d1802a95": "Music/combat_01.wav",
        "ff2e15793a4c1418a9933b900a7e3079": "Music/combat_02.wav",
        "2731361de23d9461fb58721e2762235a": "Music/combat_03.wav",
        "bd3ea333bf1da4e73837d439966ae984": "Music/combat_04.wav",
    },
    "Sonus Novum/AudioMusic/Caves and Dungeons.unitypackage": {
        "c33f5527c5ed3824c8bdcff4fcee9309": "Music/explore.wav",
        "ee3894bf36d52e74c8fef56e9e1adb86": "Music/zone_old_docks.wav",
        "3986f616d4af52c44b1a4f3989a2ea57": "Music/zone_drowned_market.wav",
        "7f16f0bfddfd829458fce6192771efa7": "Music/zone_sunken_warcamp.wav",
        "2eb641c809b114b4893a268847dcf3c9": "Music/zone_glasslit_temple.wav",
        "d3509989bc982d249857def6d823671e": "Music/zone_ashen_ward.wav",
    },
}

SFX = {
    "fae1bfcf5f1ff4c459257a2828bb5a46": "weapon_swing_01.wav",
    "ace823064cc381b4d82a49f52c3988c5": "weapon_swing_02.wav",
    "c51ec70b37728de4097f0f21a37140fc": "weapon_swing_03.wav",
    "39a2e98acb02d3f498dda18b3f119fb1": "weapon_hit_01.wav",
    "0d7b0789a50e8f84bbdfe9f424bfc6f1": "weapon_hit_02.wav",
    "2ea29a14774844e429afdce07100b91b": "weapon_hit_03.wav",
    "91dfa4b33e7a2d246b6c971b9ae30b1d": "weapon_hit_04.wav",
    "1989786f256f80e41b6cd51fa96f3d53": "weapon_miss_01.wav",
    "0a95f8106486aee46b3705c4dd48af64": "weapon_bash_01.wav",
    "7d936358e13fbfb4397bd25d15f50df6": "weapon_bash_02.wav",
    "62f2e1fe83db61b4cbed8d2c35d2ae23": "weapon_crit_01.wav",
    "3e5467bc839cdd94c8efd265de1a7c14": "weapon_crit_02.wav",
    "4a4a761dd69096e43bd5f20453276d52": "weapon_bow_01.wav",
    "fb453a0c062535d47b60936eb5d9daa2": "weapon_bow_02.wav",
    "addcc3776782a074482b22f378990d69": "spell_fire_cast_01.wav",
    "3ad8540d53491a04db5c3ed06e09a4d9": "spell_fire_impact_01.wav",
    "3648efbba527a844282478c5427d408a": "spell_arcane_cast_01.wav",
    "b783f34cdcec1f446aa029f6929c3535": "spell_arcane_impact_01.wav",
    "70553f70e74a3a34ea4e537a54c8e942": "spell_radiant_cast_01.wav",
    "25548ace4e029334991133884b487f1e": "spell_radiant_impact_01.wav",
    "1b26e82f393744142b0824ee2a42a12a": "spell_heal_01.wav",
    "ec467adf79d221445af6bed15b51a9fe": "spell_heal_02.wav",
    "de4fb2b2c713a80499ec7f6a7ba32a53": "spell_control_01.wav",
    "156c390febf0cdf418e9d2ac261fc940": "spell_shield_01.wav",
}
SFX_PACKAGE = "AnyRPG/Complete ProjectsSystems/AnyMMORPG.unitypackage"


def extract(package: Path, mapping: dict[str, str], resources: Path) -> int:
    pending = set(mapping)
    written = 0
    with tarfile.open(package, "r:gz") as archive:
        members = {member.name: member for member in archive.getmembers()}
        for guid, relative in mapping.items():
            asset = members.get(f"{guid}/asset")
            meta = members.get(f"{guid}/asset.meta")
            if asset is None or meta is None:
                continue
            destination = resources / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            for member, target in ((asset, destination), (meta, Path(str(destination) + ".meta"))):
                source = archive.extractfile(member)
                if source is None:
                    raise RuntimeError(f"Cannot read {member.name} from {package}")
                with source, target.open("wb") as output:
                    shutil.copyfileobj(source, output)
            pending.remove(guid)
            written += 1
    if pending:
        raise RuntimeError(f"{package.name}: missing {len(pending)} expected assets: {sorted(pending)}")
    return written


def main() -> int:
    repo = Path(__file__).resolve().parents[1]
    resources = repo / "game" / "Assets" / "Resources"
    appdata = os.environ.get("APPDATA")
    if not appdata:
        raise RuntimeError("APPDATA is not set; the Unity Asset Store cache cannot be located")
    cache = Path(appdata) / "Unity" / "Asset Store-5.x"

    packages = dict(MUSIC)
    packages[SFX_PACKAGE] = {guid: f"Sfx/{name}" for guid, name in SFX.items()}
    installed = 0
    for relative, mapping in packages.items():
        package = cache / Path(relative)
        if not package.is_file():
            raise FileNotFoundError(
                f"Missing cached package: {package}\n"
                "Download it once in Unity Package Manager > My Assets, then rerun this script."
            )
        count = extract(package, mapping, resources)
        installed += count
        print(f"Installed {count:2d} clips from {package.name}")

    print(f"Audio install complete: {installed} licensed clips (local and gitignored).")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:
        print(f"Audio install FAILED: {error}", file=sys.stderr)
        sys.exit(1)
