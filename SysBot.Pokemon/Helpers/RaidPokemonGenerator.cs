using PKHeX.Core;
using RaidCrawler.Core.Structures;
using System;

namespace SysBot.Pokemon.Helpers;

/// <summary>
/// Helper class for generating Pokémon from Raid data
/// </summary>
public static class RaidPokemonGenerator
{
    /// <summary>
    /// Generates a PK9 instance from raid encounter data
    /// </summary>
    /// <param name="encounter">The raid encounter data</param>
    /// <param name="seed">The raid seed</param>
    /// <param name="isShiny">Whether the raid Pokémon is shiny</param>
    /// <param name="teraType">The Tera Type for this raid</param>
    /// <param name="level">The level of the raid Pokémon</param>
    /// <returns>A fully generated PK9 instance</returns>
    public static PK9 GenerateRaidPokemon(ITeraRaid encounter, uint seed, bool isShiny, int teraType, int level)
    {
        var param = encounter.GetParam();

        var pk = new PK9
        {
            Species = encounter.Species,
            Form = encounter.Form,
            Move1 = encounter.Move1,
            Move2 = encounter.Move2,
            Move3 = encounter.Move3,
            Move4 = encounter.Move4,
            TeraTypeOriginal = (MoveType)teraType,
            CurrentLevel = (byte)level
        };

        try
        {
            // Initialize RNG with seed
            var rng = new Xoroshiro128Plus(seed);

            // Generate all Pokémon attributes
            SetEncryptionAndPID(pk, ref rng, isShiny);
            SetIVs(pk, param, ref rng);
            SetAbility(pk, param, ref rng);
            SetGender(pk, param, ref rng);
            SetNature(pk, param, ref rng);
            SetScaleAndSize(pk, param, ref rng);

            return pk;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error generating raid Pokémon: {ex.Message}");
            // Return partially generated Pokémon rather than null
            return pk;
        }
    }

    /// <summary>
    /// Sets the Encryption Constant and PID with proper shiny handling
    /// </summary>
    private static void SetEncryptionAndPID(PK9 pk, ref Xoroshiro128Plus rng, bool forceShiny)
    {
        pk.EncryptionConstant = (uint)rng.NextInt(uint.MaxValue);

        uint fakeTID = (uint)rng.NextInt(uint.MaxValue);
        uint pid = (uint)rng.NextInt(uint.MaxValue);

        if (forceShiny)
        {
            // Calculate XOR value between TID, SID and PID
            uint pid_xor = (pid & 0xFFFF) ^ (pid >> 16) ^ (fakeTID & 0xFFFF) ^ (fakeTID >> 16);
            if ((pid_xor >> 4) != 0) // If not shiny
            {
                // Force shiny with matching PSV and TSV
                pid = ((pid & 0xFFFF0000) | ((((fakeTID >> 16) ^ (fakeTID & 0xFFFF) ^ (pid >> 16)) << 4) & 0xFFFF));
            }
        }
        else if (ShinyUtil.GetIsShiny(fakeTID, pid))
        {
            // Ensure not shiny if not meant to be
            pid ^= 0x10000000;
        }

        pk.PID = pid;
    }

    /// <summary>
    /// Sets the IVs based on encounter parameters
    /// </summary>
    private static void SetIVs(PK9 pk, GenerateParam9 param, ref Xoroshiro128Plus rng)
    {
        Span<int> ivs = stackalloc int[6];
        for (int i = 0; i < 6; i++)
            ivs[i] = -1; // Initialize as unfixed

        // Handle fixed IV spreads if specified
        if (param.IVs.IsSpecified)
        {
            param.IVs.CopyToSpeedLast(ivs);
        }
        else
        {
            // Set guaranteed flawless IVs
            int flawlessCount = Math.Min((int)param.FlawlessIVs, 6);
            for (int i = 0; i < flawlessCount; i++)
            {
                int index;
                do
                {
                    index = (int)rng.NextInt(6);
                } while (ivs[index] != -1);
                ivs[index] = 31;
            }
        }

        // Generate random IVs for remaining stats
        for (int i = 0; i < 6; i++)
        {
            if (ivs[i] == -1)
                ivs[i] = (int)rng.NextInt(32);
        }

        // Apply IVs to the Pokémon
        pk.IV_HP = ivs[0];
        pk.IV_ATK = ivs[1];
        pk.IV_DEF = ivs[2];
        pk.IV_SPA = ivs[3];
        pk.IV_SPD = ivs[4];
        pk.IV_SPE = ivs[5];
    }

    /// <summary>
    /// Sets the ability based on encounter parameters
    /// </summary>
    private static void SetAbility(PK9 pk, GenerateParam9 param, ref Xoroshiro128Plus rng)
    {
        var pi = PersonalTable.SV.GetFormEntry(pk.Species, pk.Form);
        var abilityIndex = param.Ability switch
        {
            AbilityPermission.Any12H => (int)rng.NextInt(3),
            AbilityPermission.Any12 => (int)rng.NextInt(2),
            AbilityPermission.OnlyFirst => 0,
            AbilityPermission.OnlySecond => 1,
            AbilityPermission.OnlyHidden => 2,
            _ => 0,
        };

        // Apply ability with fallbacks for invalid slots
        pk.Ability = abilityIndex switch
        {
            0 => pi.Ability1,
            1 => pi.Ability2 != 0 ? pi.Ability2 : pi.Ability1,
            2 => pi.AbilityH != 0 ? pi.AbilityH : pi.Ability1,
            _ => pi.Ability1
        };

        // Set ability number to match
        pk.AbilityNumber = abilityIndex + 1;
    }

    /// <summary>
    /// Sets the gender based on species gender ratio and RNG
    /// </summary>
    private static void SetGender(PK9 pk, GenerateParam9 param, ref Xoroshiro128Plus rng)
    {
        byte genderRatio = param.GenderRatio;

        if (genderRatio == PersonalInfo.RatioMagicGenderless)
            pk.Gender = 2; // Genderless
        else if (genderRatio == PersonalInfo.RatioMagicFemale)
            pk.Gender = 1; // Female only
        else if (genderRatio == PersonalInfo.RatioMagicMale)
            pk.Gender = 0; // Male only
        else
        {
            // Use gender ratio for random determination
            int randGender = (int)rng.NextInt(100);
            int female = genderRatio;
            pk.Gender = (byte)(randGender < female ? 1 : 0);
        }
    }

    /// <summary>
    /// Sets the nature based on encounter parameters
    /// </summary>
    private static void SetNature(PK9 pk, GenerateParam9 param, ref Xoroshiro128Plus rng)
    {
        Nature nature;

        if (param.Nature != Nature.Random)
        {
            nature = param.Nature;
        }
        else if (pk.Species == (int)Species.Toxtricity)
        {
            // Special case for Toxtricity - nature determines form
            nature = (Nature)ToxtricityUtil.GetRandomNature(ref rng, pk.Form);
        }
        else
        {
            // Random nature (0-24)
            nature = (Nature)rng.NextInt(25);
        }

        pk.Nature = pk.StatNature = (Nature)(int)nature;
    }

    /// <summary>
    /// Sets the height, weight and scale based on encounter parameters
    /// </summary>
    private static void SetScaleAndSize(PK9 pk, GenerateParam9 param, ref Xoroshiro128Plus rng)
    {
        // Set height scalar
        pk.HeightScalar = param.Height != 0 ? param.Height : (byte)(rng.NextInt(0x81) + rng.NextInt(0x80));

        // Set weight scalar
        pk.WeightScalar = param.Weight != 0 ? param.Weight : (byte)(rng.NextInt(0x81) + rng.NextInt(0x80));

        // Set scale according to scale type
        pk.Scale = param.ScaleType switch
        {
            SizeType9.XS => (byte)(rng.NextInt(48) + 48), // XS: 48-95
            SizeType9.S => (byte)(rng.NextInt(48) + 96),  // S: 96-143
            SizeType9.M => (byte)(rng.NextInt(48) + 144), // M: 144-191
            SizeType9.L => (byte)(rng.NextInt(32) + 192), // L: 192-223
            SizeType9.XL => (byte)(rng.NextInt(32) + 224), // XL: 224-255
            SizeType9.VALUE => param.Scale, // Use exact scale value
            SizeType9.RANDOM or _ => (byte)(rng.NextInt(0x81) + rng.NextInt(0x80)) // Random (0-255)
        };
    }
}
