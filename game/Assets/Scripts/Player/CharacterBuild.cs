using RadiantPool.Rules;

namespace RadiantPool.Game
{
    /// <summary>The character the local player designed in the launcher (3c). Sent to the
    /// server on spawn; the server re-validates everything (never trust the client).</summary>
    public struct CharacterBuild
    {
        public int ClassIndex;
        public int RaceIndex;
        public int Str, Dex, Con, Int, Wis, Cha;

        public static CharacterBuild Default(int classIndex) => classIndex switch
        {
            0 => new CharacterBuild { ClassIndex = 0, RaceIndex = 0, Str = 15, Dex = 13, Con = 14, Int = 8, Wis = 12, Cha = 10 },
            1 => new CharacterBuild { ClassIndex = 1, RaceIndex = 2, Str = 8, Dex = 14, Con = 13, Int = 15, Wis = 12, Cha = 10 },
            2 => new CharacterBuild { ClassIndex = 2, RaceIndex = 1, Str = 14, Dex = 8, Con = 13, Int = 10, Wis = 15, Cha = 12 },
            _ => new CharacterBuild { ClassIndex = 3, RaceIndex = 3, Str = 10, Dex = 15, Con = 13, Int = 14, Wis = 12, Cha = 8 },
        };

        public bool Validate(out string error)
        {
            if (ClassIndex is < 0 or > 3 || RaceIndex is < 0 or > 3)
            {
                error = "Invalid class or race.";
                return false;
            }
            return PointBuy.IsValid(Str, Dex, Con, Int, Wis, Cha, out error);
        }

        /// <summary>The build the local launcher UI is editing; consumed at spawn.</summary>
        public static CharacterBuild Local = Default(0);
    }
}
