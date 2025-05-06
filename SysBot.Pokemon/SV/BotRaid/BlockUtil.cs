using PKHeX.Core;
using System.Collections.Generic;

namespace SysBot.Pokemon.SV.BotRaid
{
    public static class BlockUtil
    {
        public static byte[] EncryptBlock(uint key, byte[] block) => DecryptBlock(key, block);

        public static byte[] DecryptBlock(uint key, byte[] block)
        {
            var rng = new SCXorShift32(key);
            for (var i = 0; i < block.Length; i++)
                block[i] = (byte)(block[i] ^ rng.Next());
            return block;
        }
    }

    public class DataBlock
    {
        public string? Name { get; set; }
        public uint Key { get; set; }
        public SCTypeCode Type { get; set; }
        public SCTypeCode SubType { get; set; }
        public IReadOnlyList<long>? Pointer { get; set; }
        public bool IsEncrypted { get; set; }
        public int Size { get; set; }
    }

    public static class Blocks
    {
        public static class RaidDataBlocks
        {
            public static readonly DataBlock KUnlockedTeraRaidBattles = new()
            {
                Name = "KUnlockedTeraRaidBattles",
                Key = 0x27025EBF,
                Type = SCTypeCode.Bool1,
                Pointer = PokeDataOffsetsSV.SaveBlockKeyPointer,
                IsEncrypted = true,
                Size = 1,
            };

            public static readonly DataBlock KUnlockedRaidDifficulty3 = new()
            {
                Name = "KUnlockedRaidDifficulty3",
                Key = 0xEC95D8EF,
                Type = SCTypeCode.Bool1,
                Pointer = PokeDataOffsetsSV.SaveBlockKeyPointer,
                IsEncrypted = true,
                Size = 1,
            };

            public static readonly DataBlock KUnlockedRaidDifficulty4 = new()
            {
                Name = "KUnlockedRaidDifficulty4",
                Key = 0xA9428DFE,
                Type = SCTypeCode.Bool1,
                Pointer = PokeDataOffsetsSV.SaveBlockKeyPointer,
                IsEncrypted = true,
                Size = 1,
            };

            public static readonly DataBlock KUnlockedRaidDifficulty5 = new()
            {
                Name = "KUnlockedRaidDifficulty5",
                Key = 0x9535F471,
                Type = SCTypeCode.Bool1,
                Pointer = PokeDataOffsetsSV.SaveBlockKeyPointer,
                IsEncrypted = true,
                Size = 1,
            };

            public static readonly DataBlock KUnlockedRaidDifficulty6 = new()
            {
                Name = "KUnlockedRaidDifficulty6",
                Key = 0x6E7F8220,
                Type = SCTypeCode.Bool1,
                Pointer = PokeDataOffsetsSV.SaveBlockKeyPointer,
                IsEncrypted = true,
                Size = 1,
            };

            public static readonly DataBlock KWildSpawnsEnabled = new()
            {
                Name = "KWildSpawnsEnabled",
                Key = 0xC812EDC7,
                Type = SCTypeCode.Bool1,
                Pointer = PokeDataOffsetsSV.SaveBlockKeyPointer,
                IsEncrypted = true,
                Size = 1,
            };

            public static void AdjustKWildSpawnsEnabledType(bool disableOverworldSpawns)
            {
                KWildSpawnsEnabled.Type = disableOverworldSpawns ? SCTypeCode.Bool1 : SCTypeCode.Bool2;
            }

            public static readonly DataBlock KCoordinates = new()
            {
                Name = "KCoordinates",
                Key = 0x708D1511,
                Type = SCTypeCode.Array,
                SubType = SCTypeCode.Single,
                Pointer = PokeDataOffsetsSV.SaveBlockKeyPointer,
                IsEncrypted = true,
                Size = 0x0000000C,
            };

            public static readonly DataBlock KPlayerRotation = new()
            {
                Name = "KPlayerRotation",
                Key = 0x31EF132C,
                Type = SCTypeCode.Array,
                SubType = SCTypeCode.Single,
                Pointer = PokeDataOffsetsSV.SaveBlockKeyPointer,
                IsEncrypted = true,
                Size = 0x00000010,
            };

            public static readonly DataBlock KPlayerCurrentFieldID = new()
            {
                Name = "KPlayerCurrentFieldID",
                Key = 0xF17EB014, // PlayerSave_CurrentFieldId (0 = Paldea, 1 = Kitakami, 2 = Blueberry)
                Type = SCTypeCode.SByte,
                Pointer = PokeDataOffsetsSV.SaveBlockKeyPointer,
                IsEncrypted = true,
                Size = 1,
            };
        }
    }
}