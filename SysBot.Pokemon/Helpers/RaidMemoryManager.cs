using PKHeX.Core;
using RaidCrawler.Core.Structures;
using System;
using System.Collections.Generic;
using SysBot.Base;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Helpers;
/// <summary>
/// Initializes a new instance of the RaidMemoryManager with the necessary memory pointers.
/// </summary>
/// <param name="connection">The Switch connection used for memory operations</param>
/// <param name="raidBlockPointerP">Memory pointer to Paldea raid data</param>
/// <param name="raidBlockPointerK">Memory pointer to Kitakami raid data</param>
/// <param name="raidBlockPointerB">Memory pointer to Blueberry raid data</param>
public class RaidMemoryManager(ISwitchConnectionAsync connection, ulong raidBlockPointerBase, ulong raidBlockPointerKitakami, ulong raidBlockPointerBlueberry)
{
    private readonly ISwitchConnectionAsync _connection = connection;
    private readonly ulong _raidBlockPointerBase = raidBlockPointerBase;
    private readonly ulong _raidBlockPointerKitakami = raidBlockPointerKitakami;
    private readonly ulong _raidBlockPointerBlueberry = raidBlockPointerBlueberry;

    /// <summary>
    /// Reads raid data for the specified map region.
    /// </summary>
    /// <param name="mapType">The region to read raid data from (Paldea, Kitakami, or Blueberry)</param>
    /// <param name="token">Cancellation token</param>
    /// <returns>Byte array containing raw raid data</returns>
    /// <exception cref="ArgumentException">Thrown when an invalid region is specified</exception>
    public async Task<byte[]> ReadRaidData(TeraRaidMapParent mapType, CancellationToken token)
    {
        return mapType switch
        {
            TeraRaidMapParent.Paldea => await _connection.ReadBytesAbsoluteAsync(_raidBlockPointerBase + RaidBlock.HEADER_SIZE, (int)RaidBlock.SIZE_BASE, token).ConfigureAwait(false),
            TeraRaidMapParent.Kitakami => await _connection.ReadBytesAbsoluteAsync(_raidBlockPointerKitakami, (int)RaidBlock.SIZE_KITAKAMI, token).ConfigureAwait(false),
            TeraRaidMapParent.Blueberry => await _connection.ReadBytesAbsoluteAsync(_raidBlockPointerBlueberry, (int)RaidBlock.SIZE_BLUEBERRY, token).ConfigureAwait(false),
            _ => throw new ArgumentException("Invalid region", nameof(mapType))
        };
    }

    /// <summary>
    /// Injects a raid seed and crystal type at the specified index in memory.
    /// </summary>
    /// <param name="index">Index location to inject the seed</param>
    /// <param name="seed">Raid seed value</param>
    /// <param name="crystalType">Type of crystal (Base, Black, Might, etc.)</param>
    /// <param name="token">Cancellation token</param>
    /// <returns>True if injection was successful, false otherwise</returns>
    public async Task<bool> InjectSeed(int index, uint seed, TeraCrystalType crystalType, CancellationToken token)
    {
        try
        {
            // Get the appropriate pointer
            List<long> ptr = DeterminePointer(index);

            // Inject the seed
            byte[] seedBytes = BitConverter.GetBytes(seed);
            await _connection.PointerPoke(seedBytes, ptr, token).ConfigureAwait(false);

            // Verify seed was written correctly
            byte[] verification = await _connection.PointerPeek(4, ptr, token).ConfigureAwait(false);
            uint verifiedSeed = BitConverter.ToUInt32(verification, 0);

            if (seed != verifiedSeed)
            {
                return false;
            }

            // Inject crystal type
            var crystalPtr = new List<long>(ptr);
            crystalPtr[3] = ptr[3] + 0x08;
            byte[] crystalBytes = BitConverter.GetBytes((int)crystalType);
            await _connection.PointerPoke(crystalBytes, crystalPtr, token).ConfigureAwait(false);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Determines the appropriate memory pointer for a raid at the specified index.
    /// </summary>
    /// <param name="index">Raid index</param>
    /// <returns>List of longs representing the memory pointer</returns>
    private static List<long> DeterminePointer(int index)
    {
        const int kitakamiDensCount = 25;
        int blueberrySubtractValue = kitakamiDensCount == 25 ? 93 : 94;

        if (index < 69)
        {
            return new List<long>(Offsets.RaidBlockPointerBase.ToArray())
            {
                [3] = 0x60 + index * 0x20
            };
        }
        else if (index < 94)
        {
            return new List<long>(Offsets.RaidBlockPointerKitakami.ToArray())
            {
                [3] = 0xCE8 + ((index - 69) * 0x20)
            };
        }
        else
        {
            return new List<long>(Offsets.RaidBlockPointerBlueberry.ToArray())
            {
                [3] = 0x1968 + ((index - blueberrySubtractValue) * 0x20)
            };
        }
    }
}
