using System.Collections.Generic;

namespace SysBot.Pokemon;

/// <summary>
/// Pokémon Scarlet/Violet RAM offsets
/// </summary>
public class PokeDataOffsetsSV
{
    public const string SVGameVersion = "3.0.1";
    public const string ScarletID = "0100A3D008C5C000";
    public const string VioletID = "01008F6008C5E000";

    public IReadOnlyList<long> BoxStartPokemonPointer { get; } = [0x47350D8, 0xD8, 0x8, 0xB8, 0x30, 0x9D0, 0x0]; // 3.0.0
    public IReadOnlyList<long> MyStatusPointer { get; } = [0x47350D8, 0xD8, 0x8, 0xB8, 0x0, 0x40]; // KMyStatus - TeraFinder or Official 3.0.0
    public IReadOnlyList<long> ConfigPointer { get; } = [0x47350D8, 0xD8, 0x8, 0xB8, 0xD0, 0x40]; // 3.0.0
    public IReadOnlyList<long> CurrentBoxPointer { get; } = [0x47350D8, 0x1C0, 0x28, 0x570]; // 3.0.0
    public IReadOnlyList<long> LinkTradePartnerNIDPointer { get; } = [0x475EA28, 0xF8, 0x8]; // 3.0.0
    public IReadOnlyList<long> Trader2MyStatusPointer { get; } = [0x473A110, 0x48, 0xE0, 0x0]; // 3.0.0
    public IReadOnlyList<long> IsConnectedPointer { get; } = [0x4763E08, 0x10]; // 3.0.0
    public IReadOnlyList<long> OverworldPointer { get; } = [0x473ADE0, 0x160, 0xE8, 0x28]; // 3.0.0

    // RaidCrawler Offsets
    public IReadOnlyList<long> BlockKeyPointer { get; } = [0x47350D8, 0xD8, 0x0, 0x0, 0x30, 0x0]; // RaidCrawler Offsets.cs 3.0.0
    public static IReadOnlyList<long> SaveBlockKeyPointer { get; } = [0x47350D8, 0xD8, 0x0, 0x0, 0x30, 0x08]; // TeraFinder 3.0.0
    public IReadOnlyList<long> TeraRaidCodePointer { get; } = [0x475EA28, 0x10, 0x78, 0x10, 0x1A9]; // Zyro 3.0.0

    public ulong TeraLobbyIsConnected { get; } = 0x043DF430; // Zyro 3.0.0
    public ulong LoadedIntoDesiredState { get; } = 0x047D2020; // Zyro 3.0.0

    public const int BoxFormatSlotSize = 0x158;
    public const ulong LibAppletWeID = 0x010000000000100a; // One of the process IDs for the news.
}