using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Base;

/// <summary>
/// Handles tracking and distribution of raid codes across projects
/// </summary>
public static class SharedRaidCodeHandler
{
    private static readonly Dictionary<ulong, RaidMessageInfo> _activeRaidMessages = new();
    private static readonly object _lock = new object();

    /// <summary>
    /// Determines whether Discord integration should be used
    /// </summary>
    public static bool UseDiscordIntegration { get; set; } = true;

    /// <summary>
    /// Callback function to update reactions on Discord messages
    /// Will be set by the Discord integration project if available
    /// </summary>
    public static Func<ulong, ulong, bool, Task>? UpdateMessageReactionCallback { get; set; }

    /// <summary>
    /// Adds a message to the active raid message tracking system
    /// </summary>
    /// <param name="messageId">Discord message ID</param>
    /// <param name="channelId">Discord channel ID</param>
    /// <param name="raidCode">Raid code to be distributed</param>
    /// <param name="raidInfoDict">Dictionary containing raid information details</param>
    public static void AddActiveRaidMessageWithInfoDict(ulong messageId, ulong channelId, string raidCode, Dictionary<string, string> raidInfoDict)
    {
        lock (_lock)
        {
            _activeRaidMessages[messageId] = new RaidMessageInfo
            {
                MessageId = messageId,
                ChannelId = channelId,
                RaidCode = raidCode,
                RaidInfoDict = raidInfoDict
            };
        }
    }

    /// <summary>
    /// Checks if a message is being tracked as an active raid message
    /// </summary>
    /// <param name="messageId">Discord message ID to check</param>
    /// <returns>True if the message is an active raid message</returns>
    public static bool IsActiveRaidMessage(ulong messageId)
    {
        lock (_lock)
        {
            return _activeRaidMessages.ContainsKey(messageId);
        }
    }

    /// <summary>
    /// Gets the raid code associated with a message
    /// </summary>
    /// <param name="messageId">Discord message ID</param>
    /// <returns>Raid code string, or empty string if not found</returns>
    public static string GetRaidCodeForMessage(ulong messageId)
    {
        lock (_lock)
        {
            return _activeRaidMessages.TryGetValue(messageId, out var info) ? info.RaidCode : string.Empty;
        }
    }

    /// <summary>
    /// Gets the raid information dictionary associated with a message
    /// </summary>
    /// <param name="messageId">Discord message ID</param>
    /// <returns>Dictionary containing raid information, or null if not found</returns>
    public static Dictionary<string, string>? GetRaidInfoDict(ulong messageId)
    {
        lock (_lock)
        {
            return _activeRaidMessages.TryGetValue(messageId, out var info) ? info.RaidInfoDict : null;
        }
    }

    /// <summary>
    /// Gets a list of all active raid messages
    /// </summary>
    /// <returns>List of RaidMessageInfo objects for all active raids</returns>
    public static List<RaidMessageInfo> GetAllActiveRaidMessages()
    {
        lock (_lock)
        {
            return _activeRaidMessages.Values.ToList();
        }
    }

    /// <summary>
    /// Clears all raid tracking data
    /// </summary>
    public static void ClearAllRaidTracking()
    {
        lock (_lock)
        {
            _activeRaidMessages.Clear();
        }
    }

    /// <summary>
    /// Number of times the callback was attempted but not found
    /// </summary>
    private static int _callbackMissingCount = 0;

    /// <summary>
    /// Updates reactions on all active raid messages
    /// </summary>
    /// <param name="isActive">Whether the raid is active (true) or ended (false)</param>
    /// <param name="token">Cancellation token</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public static async Task UpdateReactionsOnAllMessages(bool isActive, CancellationToken token = default)
    {
        if (!UseDiscordIntegration)
            return;

        if (UpdateMessageReactionCallback == null)
        {
            _callbackMissingCount++;

            // If we've tried multiple times and the callback is still missing,
            // assume Discord integration is not being used and disable it
            if (_callbackMissingCount >= 3)
            {
                UseDiscordIntegration = false;
                LogUtil.LogInfo("Discord integration appears to be unused. Disabling reaction updates automatically.", nameof(SharedRaidCodeHandler));
                return;
            }

            LogUtil.LogInfo("Discord reaction callback not registered, skipping reaction updates.", nameof(SharedRaidCodeHandler));
            return;
        }

        // Reset counter whenever a successful callback is found
        _callbackMissingCount = 0;

        var activeMessages = GetAllActiveRaidMessages();
        if (activeMessages.Count == 0)
            return;

        foreach (var message in activeMessages)
        {
            try
            {
                await UpdateMessageReactionCallback(message.MessageId, message.ChannelId, isActive);
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Error updating reaction for message {message.MessageId}: {ex.Message}", nameof(SharedRaidCodeHandler));
            }
        }
    }

    /// <summary>
    /// Contains information about a raid message being tracked
    /// </summary>
    public class RaidMessageInfo
    {
        /// <summary>
        /// Discord message ID
        /// </summary>
        public ulong MessageId { get; set; }

        /// <summary>
        /// Discord channel ID
        /// </summary>
        public ulong ChannelId { get; set; }

        /// <summary>
        /// Raid code to distribute to users
        /// </summary>
        public string RaidCode { get; set; } = string.Empty;

        /// <summary>
        /// Dictionary containing all raid information details
        /// </summary>
        public Dictionary<string, string> RaidInfoDict { get; set; } = new Dictionary<string, string>();
    }
}