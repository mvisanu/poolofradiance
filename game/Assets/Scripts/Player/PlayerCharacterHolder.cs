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
            Debug.Log($"[RadiantPool] companion hired: {Sheet.Name} the {Sheet.Class}");
        }

        public CharacterClass Class => (CharacterClass)Mathf.Max(0, ClassIndex.Value);

        /// <summary>Class → KayKit Adventurers model (CC0, Resources/Characters).</summary>
        private static readonly string[] ClassModels =
            { "Knight", "Barbarian", "Mage", "Rogue_Hooded" };

        private void Awake()
        {
            ClassIndex.OnChange += (_, next, _) => ApplyClassVisual(next);
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

        /// <summary>The weapon used for the basic Attack action (inventory arrives at 3e-full).</summary>
        public AttackDefinition BasicAttack()
        {
            var sheet = Sheet;
            int str = sheet.ProficiencyBonus + sheet.Abilities.Modifier(Ability.Str);
            int dex = sheet.ProficiencyBonus + sheet.Abilities.Modifier(Ability.Dex);
            // Negative modifiers must render "1d6-1", never "1d6+-1" (parser kick bug).
            string Dmg(string die, Ability ability)
            {
                int mod = sheet.Abilities.Modifier(ability);
                return mod == 0 ? die : mod > 0 ? $"{die}+{mod}" : $"{die}{mod}";
            }
            return Class switch
            {
                CharacterClass.Fighter => new AttackDefinition("Longsword", str,
                    Dmg("1d8", Ability.Str), DamageType.Slashing, 5),
                CharacterClass.Cleric => new AttackDefinition("Mace", str,
                    Dmg("1d6", Ability.Str), DamageType.Bludgeoning, 5),
                CharacterClass.Wizard => new AttackDefinition("Quarterstaff", str,
                    Dmg("1d6", Ability.Str), DamageType.Bludgeoning, 5),
                _ => new AttackDefinition("Shortsword", dex,
                    Dmg("1d6", Ability.Dex), DamageType.Piercing, 5),
            };
        }
    }
}
