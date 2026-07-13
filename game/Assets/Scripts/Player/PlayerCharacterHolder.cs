using FishNet.Object;
using FishNet.Object.Synchronizing;
using RadiantPool.Rules;
using UnityEngine;

namespace RadiantPool.Game
{
    /// <summary>Server-owned character sheet for a connected player. The owning client
    /// submits its designed build (3c) on spawn; the server validates it and either
    /// creates a sheet or hands back the roster entry with the same display name
    /// (rejoin support). Invalid builds fall back to a class-default array.</summary>
    public class PlayerCharacterHolder : NetworkBehaviour
    {
        public readonly SyncVar<int> ClassIndex = new SyncVar<int>(-1);
        public readonly SyncVar<string> CharacterName = new SyncVar<string>("");
        public readonly SyncVar<bool> IsCompanionSynced = new SyncVar<bool>(false);
        public readonly SyncVar<string> WeaponId = new SyncVar<string>("");
        public readonly SyncVar<string> ArmorId = new SyncVar<string>("");
        public readonly SyncVar<bool> ShieldEquipped = new SyncVar<bool>(false);

        // Derived sheet stats mirrored to clients: the sheet itself is server-only, but
        // the inventory/equipment screen must show AC, HP and to-hit on every client.
        public readonly SyncVar<int> LevelSynced = new SyncVar<int>(1);
        public readonly SyncVar<int> MaxHpSynced = new SyncVar<int>(0);
        public readonly SyncVar<int> ArmorClassSynced = new SyncVar<int>(10);
        public readonly SyncVar<int> ProficiencySynced = new SyncVar<int>(2);
        public readonly SyncVar<int> StrModSynced = new SyncVar<int>(0);
        public readonly SyncVar<int> DexModSynced = new SyncVar<int>(0);

        // Progression: the XP bar reads these, and the level-up screen spends the points.
        public readonly SyncVar<int> XpSynced = new SyncVar<int>(0);
        public readonly SyncVar<int> PendingPointsSynced = new SyncVar<int>(0);

        /// <summary>The six scores themselves (not just the modifiers): the level-up screen
        /// has to show what a point would be added TO, and whether it is already at 20.</summary>
        public readonly SyncVar<int> StrSynced = new SyncVar<int>(10);
        public readonly SyncVar<int> DexSynced = new SyncVar<int>(10);
        public readonly SyncVar<int> ConSynced = new SyncVar<int>(10);
        public readonly SyncVar<int> IntSynced = new SyncVar<int>(10);
        public readonly SyncVar<int> WisSynced = new SyncVar<int>(10);
        public readonly SyncVar<int> ChaSynced = new SyncVar<int>(10);

        /// <summary>Score by ability, on the client, in the enum's own order.</summary>
        public int ScoreOf(Ability a) => a switch
        {
            Ability.Str => StrSynced.Value,
            Ability.Dex => DexSynced.Value,
            Ability.Con => ConSynced.Value,
            Ability.Int => IntSynced.Value,
            Ability.Wis => WisSynced.Value,
            _ => ChaSynced.Value
        };

        private float _nextStatSync;

        /// <summary>Server: push the sheet's derived stats out to clients. Cheap — FishNet
        /// only sends SyncVars that actually changed — so a slow poll covers every source
        /// of change (equip, level-up, rest) without wiring each one up by hand.</summary>
        private void Update()
        {
            if (!IsServerStarted || Sheet == null || Time.time < _nextStatSync) return;
            _nextStatSync = Time.time + 0.5f;
            LevelSynced.Value = Sheet.Level;
            MaxHpSynced.Value = Sheet.MaxHp;
            ArmorClassSynced.Value = Sheet.ArmorClass;
            ProficiencySynced.Value = Sheet.ProficiencyBonus;
            StrModSynced.Value = Sheet.Abilities.Modifier(Ability.Str);
            DexModSynced.Value = Sheet.Abilities.Modifier(Ability.Dex);
            XpSynced.Value = Sheet.Xp;
            PendingPointsSynced.Value = Sheet.PendingAbilityPoints;
            StrSynced.Value = Sheet.Abilities[Ability.Str];
            DexSynced.Value = Sheet.Abilities[Ability.Dex];
            ConSynced.Value = Sheet.Abilities[Ability.Con];
            IntSynced.Value = Sheet.Abilities[Ability.Int];
            WisSynced.Value = Sheet.Abilities[Ability.Wis];
            ChaSynced.Value = Sheet.Abilities[Ability.Cha];
        }

        /// <summary>Hired AI party member (server-driven, no owning connection).</summary>
        public bool IsCompanion => IsCompanionSynced.Value;

        /// <summary>Authoritative sheet — exists only on the server.</summary>
        public CharacterSheet Sheet { get; private set; }

        /// <summary>Server-side init for a hired companion (no client submits a build).</summary>
        public void ServerInitCompanion(string name, int classIndex)
        {
            var build = CharacterBuild.Default(classIndex);
            Sheet = GameDirector.Instance != null
                ? GameDirector.Instance.ServerGetOrCreateSheet(name, build)
                : CreateSheetFromBuild(name, build);
            IsCompanionSynced.Value = true;
            ClassIndex.Value = (int)Sheet.Class;
            CharacterName.Value = Sheet.Name;
            ServerSetDefaultEquipment();
            Debug.Log($"[RadiantPool] companion hired: {Sheet.Name} the {Sheet.Class}");
        }

        public CharacterClass Class => (CharacterClass)Mathf.Max(0, ClassIndex.Value);

        /// <summary>Class → KayKit Adventurers model (CC0, Resources/Characters).</summary>
        private static readonly string[] ClassModels =
            { "Knight", "Barbarian", "Mage", "Rogue_Hooded" };

        private void Awake()
        {
            ClassIndex.OnChange += (_, next, _) => ApplyClassVisual(next);
            WeaponId.OnChange += (_, _, _) => ApplyHandVisuals();
            ShieldEquipped.OnChange += (_, _, _) => ApplyHandVisuals();
        }

        private void ApplyHandVisuals()
        {
            var weapon = GameItem.Get(WeaponId.Value);
            CharacterVisuals.SetHandItem(transform, "r", weapon?.HandModel ?? "");
            CharacterVisuals.SetHandItem(transform, "l",
                ShieldEquipped.Value ? "shield_badge" : "");
        }

        /// <summary>Server: equips an item onto the sheet and mirrors it to clients.
        /// Returns the previously equipped item id (goes back to the stash), or null.</summary>
        public string ServerEquip(GameItem item)
        {
            string previous = null;
            switch (item.Slot)
            {
                case ItemSlot.Weapon:
                    previous = WeaponId.Value;
                    WeaponId.Value = item.Id;
                    break;
                case ItemSlot.Armor:
                    previous = ArmorId.Value;
                    ArmorId.Value = item.Id;
                    Sheet.EquipArmor(item.Armor);
                    break;
                case ItemSlot.Shield:
                    if (ShieldEquipped.Value) previous = "shield";
                    ShieldEquipped.Value = true;
                    Sheet.SetShield(true);
                    break;
            }
            return string.IsNullOrEmpty(previous) ? null : previous;
        }

        /// <summary>Server: mirror the starting gear CreateSheetFromBuild equipped.</summary>
        public void ServerSetDefaultEquipment()
        {
            switch (Sheet.Class)
            {
                case CharacterClass.Fighter:
                    WeaponId.Value = "longsword"; ArmorId.Value = "chain_mail";
                    ShieldEquipped.Value = true; break;
                case CharacterClass.Cleric:
                    WeaponId.Value = "mace"; ArmorId.Value = "scale_mail";
                    ShieldEquipped.Value = true; break;
                case CharacterClass.Wizard:
                    WeaponId.Value = "quarterstaff"; break;
                default:
                    WeaponId.Value = "shortsword"; ArmorId.Value = "leather_armor"; break;
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyClassVisual(ClassIndex.Value);
            if (!IsOwner) return;
            var b = CharacterBuild.Local;
            CmdSubmitBuild(SessionLauncher.LocalDisplayName, b.ClassIndex, b.RaceIndex,
                b.Str, b.Dex, b.Con, b.Int, b.Wis, b.Cha);
        }

        private void ApplyClassVisual(int classIndex)
        {
            if (classIndex < 0 || classIndex >= ClassModels.Length) return;
            CharacterVisuals.Attach(transform, ClassModels[classIndex]);
            ApplyHandVisuals();
        }

        [ServerRpc]
        private void CmdSubmitBuild(string name, int classIndex, int raceIndex,
            int str, int dex, int con, int intl, int wis, int cha)
        {
            if (Sheet != null) return;   // one submission per spawn

            name = (name ?? "").Trim();
            if (name.Length == 0) name = $"Adventurer {OwnerId}";
            if (name.Length > 20) name = name.Substring(0, 20);

            var build = new CharacterBuild
            {
                ClassIndex = classIndex, RaceIndex = raceIndex,
                Str = str, Dex = dex, Con = con, Int = intl, Wis = wis, Cha = cha
            };
            if (!build.Validate(out _))
                build = CharacterBuild.Default(Mathf.Clamp(classIndex, 0, 3));

            Sheet = GameDirector.Instance != null
                ? GameDirector.Instance.ServerGetOrCreateSheet(name, build)
                : CreateSheetFromBuild(name, build);
            ClassIndex.Value = (int)Sheet.Class;
            CharacterName.Value = Sheet.Name;
            ServerSetDefaultEquipment();
            Debug.Log($"[RadiantPool] character ready: {Sheet.Name} the {Sheet.Class}" +
                      $" (owner {OwnerId}, HP {Sheet.MaxHp}, AC {Sheet.ArmorClass})");
        }

        public static CharacterSheet CreateSheetFromBuild(string name, CharacterBuild b)
        {
            var cls = (CharacterClass)b.ClassIndex;
            var sheet = new CharacterSheet(
                $"pc_{name.ToLowerInvariant().Replace(' ', '_')}", name, (Race)b.RaceIndex,
                cls, new AbilityScores(b.Str, b.Dex, b.Con, b.Int, b.Wis, b.Cha));
            switch (cls)
            {
                case CharacterClass.Fighter:
                    sheet.EquipArmor(ArmorDefinition.ChainMail);
                    sheet.SetShield(true);
                    break;
                case CharacterClass.Cleric:
                    sheet.EquipArmor(ArmorDefinition.ScaleMail);
                    sheet.SetShield(true);
                    sheet.KnownSpells.AddRange(new[]
                        { "sacred_flame", "cure_wounds", "healing_word", "bless", "guiding_bolt" });
                    break;
                case CharacterClass.Wizard:
                    sheet.KnownSpells.AddRange(new[]
                        { "fire_bolt", "magic_missile", "burning_hands", "shield", "sleep" });
                    break;
                case CharacterClass.Rogue:
                    sheet.EquipArmor(ArmorDefinition.Leather);
                    break;
            }
            return sheet;
        }

        /// <summary>The equipped weapon as an SRD attack. Finesse and ranged weapons use
        /// the better of Str/Dex; negative modifiers render "1d6-1" (never "1d6+-1").</summary>
        public AttackDefinition BasicAttack()
        {
            var sheet = Sheet;
            var item = GameItem.Get(WeaponId.Value)
                       ?? GameItem.Get("dagger");   // bare minimum fallback
            int strMod = sheet.Abilities.Modifier(Ability.Str);
            int dexMod = sheet.Abilities.Modifier(Ability.Dex);
            int mod = item.Finesse || item.RangeFeet > 5
                ? System.Math.Max(strMod, dexMod) : strMod;
            string dmg = mod == 0 ? item.Damage
                : mod > 0 ? $"{item.Damage}+{mod}" : $"{item.Damage}{mod}";
            return new AttackDefinition(item.Name, sheet.ProficiencyBonus + mod,
                dmg, item.DamageType, item.RangeFeet);
        }
    }
}
