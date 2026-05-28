using AQuestReborn.CustomNpc;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Glamourer.Api.Enums;
using SamplePlugin;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace AQuestReborn
{
    /// <summary>
    /// Handles paired/coordinated animations between Custom NPCs and their partners
    /// (either the player or another NPC). When a trigger phrase is matched in chat,
    /// the NPC walks to the partner, both face each other, and both play their
    /// respective animations simultaneously.
    /// </summary>
    public class PairedAnimationManager
    {
        private readonly Plugin _plugin;
        private readonly AQuestReborn _aqr;

        /// <summary>
        /// Distance in yalms at which the NPC stops walking and begins the paired animation.
        /// </summary>
        private const float PairDistance = 1.2f;

        /// <summary>
        /// Maximum distance in yalms from the partner that the NPC will react to a trigger phrase.
        /// If the NPC is further away, it will be ignored.
        /// </summary>
        private const float MaxTriggerRange = 30f;

        /// <summary>
        /// Tracks which NPCs are currently performing a paired animation to prevent re-triggering.
        /// </summary>
        private readonly HashSet<string> _activeAnimations = new HashSet<string>();

        /// <summary>
        /// Pending switch configs: when a switch is requested, the new config is stored here
        /// and picked up by the monitoring loop within 100ms.
        /// </summary>
        private readonly ConcurrentDictionary<string, PairedAnimationConfig> _pendingSwitchConfigs
            = new ConcurrentDictionary<string, PairedAnimationConfig>();

        internal PairedAnimationManager(Plugin plugin, AQuestReborn aqr)
        {
            _plugin = plugin;
            _aqr = aqr;
        }

        /// <summary>
        /// Returns true if the named NPC is currently performing a paired animation.
        /// </summary>
        public bool IsNpcInPairedAnimation(string npcName)
        {
            return _activeAnimations.Contains(npcName);
        }

        /// <summary>
        /// Request an instant switch to a different paired animation for an NPC
        /// that is already in an active paired animation. The monitoring loop will
        /// pick this up within 100ms and swap emotes/glamour inline.
        /// </summary>
        public void SwitchAnimation(string npcName, PairedAnimationConfig newConfig)
        {
            if (!_activeAnimations.Contains(npcName)) return;
            _pendingSwitchConfigs[npcName] = newConfig;
        }

        /// <summary>
        /// Stops any active paired animation for the specified NPC.
        /// </summary>
        public void StopAnimation(string npcName)
        {
            _activeAnimations.Remove(npcName);
        }

        /// <summary>
        /// Check a player message for paired animation triggers across all summoned Custom NPCs.
        /// Returns true if a paired animation was triggered (caller should skip AI chat).
        /// </summary>
        public bool TryTriggerPairedAnimation(string message, IPlayerCharacter sender)
        {
            if (string.IsNullOrWhiteSpace(message) || sender == null) return false;

            string lowerMessage = message.ToLower().Trim();

            foreach (var npcData in _plugin.Configuration.CustomNpcCharacters)
            {
                if (npcData.PairedAnimations == null || npcData.PairedAnimations.Count == 0)
                    continue;

                // Check if this NPC is summoned
                if (!_aqr.InteractiveNpcDictionary.ContainsKey(npcData.NpcName))
                    continue;
                if (!_aqr.CustomNpcCharacters.ContainsKey(npcData.NpcName))
                    continue;

                // If this NPC is already performing a paired animation, try to switch via trigger
                if (_activeAnimations.Contains(npcData.NpcName))
                {
                    foreach (var config in npcData.PairedAnimations)
                    {
                        if (string.IsNullOrWhiteSpace(config.TriggerPhrase) || config.NpcEmoteId == 0)
                            continue;
                        if (!lowerMessage.Contains(config.TriggerPhrase.ToLower().Trim()))
                            continue;

                        SwitchAnimation(npcData.NpcName, config);
                        return true;
                    }
                    continue;
                }

                var interactiveNpc = _aqr.InteractiveNpcDictionary[npcData.NpcName];
                var npcCharacter = _aqr.CustomNpcCharacters[npcData.NpcName];

                foreach (var config in npcData.PairedAnimations)
                {
                    if (string.IsNullOrWhiteSpace(config.TriggerPhrase))
                        continue;
                    if (config.NpcEmoteId == 0)
                        continue;

                    // Case-insensitive phrase match: the message must contain the trigger phrase
                    if (!lowerMessage.Contains(config.TriggerPhrase.ToLower().Trim()))
                        continue;

                    // Determine partner
                    if (string.IsNullOrEmpty(config.PartnerNpcName))
                    {
                        // Partner is the player
                        float distToPlayer = Vector3.Distance(interactiveNpc.CurrentPosition, sender.Position);
                        if (distToPlayer > MaxTriggerRange) continue;

                        // Trigger the paired animation (NPC walks to player, then both animate)
                        ExecutePlayerPairedAnimation(interactiveNpc, npcData, npcCharacter, config, sender);
                        return true;
                    }
                    else
                    {
                        // Partner is another NPC
                        if (!_aqr.InteractiveNpcDictionary.ContainsKey(config.PartnerNpcName))
                            continue;
                        if (!_aqr.CustomNpcCharacters.ContainsKey(config.PartnerNpcName))
                            continue;

                        var partnerNpc = _aqr.InteractiveNpcDictionary[config.PartnerNpcName];
                        var partnerCharacter = _aqr.CustomNpcCharacters[config.PartnerNpcName];

                        float distBetween = Vector3.Distance(interactiveNpc.CurrentPosition, partnerNpc.CurrentPosition);
                        if (distBetween > MaxTriggerRange) continue;

                        ExecuteNpcPairedAnimation(interactiveNpc, npcData, npcCharacter,
                            partnerNpc, config.PartnerNpcName, partnerCharacter, config);
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Execute a paired animation between an NPC and the player.
        /// The NPC walks to the player, both face each other, and both play their emotes.
        /// </summary>
        private void ExecutePlayerPairedAnimation(
            InteractiveNpc npc, CustomNpcCharacter npcData, ICharacter npcCharacter,
            PairedAnimationConfig config, IPlayerCharacter player)
        {
            _activeAnimations.Add(npcData.NpcName);

            Task.Run(async () =>
            {
                try
                {
                    bool wasFollowing = npc.IsFollowingPlayer;

                    // Stop following so the NPC doesn't fight our movement commands
                    npc.StopFollowingPlayer();

                    // Turn to face the player before speaking
                    Vector3 facePlayerRot = CoordinateUtility.LookAt(npc.CurrentPosition, player.Position).QuaternionToEuler();
                    _plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        npc.SetDefaults(npc.CurrentPosition, facePlayerRot, 5f);
                    });

                    // Show a speech bubble so it looks like the NPC is addressing the player
                    _plugin.SpeechBubbleManager?.ShowBubble(npcCharacter, npcData.NpcName, GetApproachBubble());

                    // Wait for the approach delay so the player can read the line
                    if (config.ApproachDelayMs > 0)
                        await Task.Delay(config.ApproachDelayMs);

                    // Walk to the player's exact position for paired animation alignment.
                    Vector3 targetPos = player.Position;
                    Vector3 walkFaceRot = CoordinateUtility.LookAt(npc.CurrentPosition, player.Position).QuaternionToEuler();

                    _plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        npc.WalkToTarget(targetPos, 5f);
                        npc.SetDefaults(targetPos, walkFaceRot, 5f);
                    });

                    // Wait for the NPC to arrive
                    var arrivalTimeout = Stopwatch.StartNew();
                    while (arrivalTimeout.ElapsedMilliseconds < 15000)
                    {
                        float dist = Vector3.Distance(npc.CurrentPosition, targetPos);
                        if (dist < 0.15f) break;
                        await Task.Delay(50);
                    }

                    // Match the player's rotation so the NPC faces the same direction
                    // player.Rotation is in radians, but SetDefaults expects degrees
                    float playerRotDegrees = player.Rotation * (180f / MathF.PI);
                    Vector3 playerRotation = new Vector3(0, playerRotDegrees, 0);
                    _plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        npc.SetDefaults(targetPos, playerRotation, 5f);
                    });

                    // Let the idle Lerp pull the NPC to final position and rotation
                    await Task.Delay(100);

                    // Stop the walk and break any idle emote
                    _plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        npc.ShouldBeMoving = false;
                        _plugin.AnamcoreManager.ForceStopEmote(npcCharacter.Address);
                    });

                    await Task.Delay(200);

                    // Lock the NPC's animation so idle emotes don't override it
                    string savedNpcState = null;
                    string savedPlayerState = null;
                    Dictionary<Guid, List<string>> disabledPenumbraMods = new Dictionary<Guid, List<string>>();

                    _plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        npc.AnimationLocked = true;

                        // Enable Penumbra mod if configured (before glamourer to avoid conflicts)
                        disabledPenumbraMods = EnablePenumbraMod(config.PenumbraModFilter, npcCharacter, player);

                        // Save current states before applying designs
                        if (!string.IsNullOrEmpty(config.NpcGlamourerDesign))
                            savedNpcState = SaveGlamourerState(npcCharacter);
                        if (!string.IsNullOrEmpty(config.PartnerGlamourerDesign))
                            savedPlayerState = SaveGlamourerState(player);

                        // Apply Glamourer designs if configured
                        ApplyGlamourerDesign(config.NpcGlamourerDesign, npcCharacter);
                        ApplyGlamourerDesign(config.PartnerGlamourerDesign, player);

                        // Resolve Emote RowIds → ActionTimeline RowIds, applying cpose variant if set
                        ushort npcTimelineId = ResolveEmoteToTimeline(config.NpcEmoteId);
                        if (config.NpcCposeIndex > 0)
                            npcTimelineId = ResolveCposeTimeline(npcTimelineId, config.NpcCposeIndex);
                        ushort partnerTimelineId = ResolveEmoteToTimeline(config.PartnerEmoteId);
                        if (config.PartnerCposeIndex > 0)
                            partnerTimelineId = ResolveCposeTimeline(partnerTimelineId, config.PartnerCposeIndex);

                        // NPC plays their animation
                        if (npcTimelineId > 0)
                            _plugin.AnamcoreManager.TriggerEmote(npcCharacter.Address, npcTimelineId, config.LoopAnimation);

                        // Player plays their animation (if configured)
                        if (partnerTimelineId > 0 && player.Address != nint.Zero)
                            _plugin.AnamcoreManager.TriggerEmote(player.Address, partnerTimelineId, config.LoopAnimation);
                    });

                    // Set emotion and animation context overrides if configured
                    if (!string.IsNullOrEmpty(config.EmotionOverride))
                    {
                        npcData.TemporaryMoodOverride = config.EmotionOverride;
                    }
                    if (!string.IsNullOrEmpty(config.AnimationContext))
                    {
                        npcData.TemporaryAnimationContext = config.AnimationContext;
                    }

                    // Open the one-on-one chat window if configured (player-partner only)
                    if (config.OpenChatOnStart && string.IsNullOrEmpty(config.PartnerNpcName))
                    {
                        _plugin.Framework.RunOnFrameworkThread(() =>
                        {
                            try
                            {
                                if (_aqr.CustomNpcConversationManagers.ContainsKey(npcData.NpcName))
                                {
                                    // Build greeting: prefer explicit ChatGreeting, fall back to AnimationContext
                                    string greeting = null;
                                    if (!string.IsNullOrWhiteSpace(config.ChatGreeting))
                                        greeting = config.ChatGreeting;
                                    else if (!string.IsNullOrWhiteSpace(config.AnimationContext))
                                        greeting = $"*is {config.AnimationContext} with you*";

                                    _plugin.NpcChatWindow.OpenConversation(
                                        npcData.NpcName,
                                        _aqr.CustomNpcConversationManagers[npcData.NpcName],
                                        npcCharacter, npcData, greeting);
                                }
                            }
                            catch (Exception ex)
                            {
                                _plugin.PluginLog.Warning(ex, "[PairedAnimation] Failed to open chat window");
                            }
                        });
                    }

                    // Monitor for player movement or duration expiry.
                    // If the player moves, cancel the animation immediately.
                    var animTimer = Stopwatch.StartNew();
                    int maxDurationMs = config.UseDuration
                        ? (config.LoopAnimation ? config.DurationMs : Math.Max(config.DurationMs, 5000))
                        : int.MaxValue;

                    // Capture start position after a brief grace period so emotes that shift
                    // the player position don't immediately trigger the movement threshold.
                    await Task.Delay(500);
                    Vector3 animStartPos = player.Position;
                    _plugin.PluginLog.Information($"[PairedAnimation] Monitoring started for {npcData.NpcName}. StartPos={animStartPos}, maxDurationMs={maxDurationMs}");

                    while (animTimer.ElapsedMilliseconds < maxDurationMs)
                    {
                        if (!_activeAnimations.Contains(npcData.NpcName))
                        {
                            _plugin.PluginLog.Information($"[PairedAnimation] {npcData.NpcName} stopped explicitly.");
                            break;
                        }

                        // Check if player moved
                        float movedDist = Vector3.Distance(player.Position, animStartPos);
                        if (movedDist > 0.5f)
                        {
                            _plugin.PluginLog.Information($"[PairedAnimation] {npcData.NpcName} stopped: player moved {movedDist:F3} yalms after {animTimer.ElapsedMilliseconds}ms");
                            break;
                        }

                        // Check for pending animation switch
                        if (_pendingSwitchConfigs.TryRemove(npcData.NpcName, out var switchConfig))
                        {
                            // Swap Penumbra mods, glamour, and emotes on the framework thread
                            _plugin.Framework.RunOnFrameworkThread(() =>
                            {
                                // 1. Stop old emotes first — characters return to idle pose
                                _plugin.AnamcoreManager.ForceStopEmote(npcCharacter.Address);
                                if (player.Address != nint.Zero)
                                    _plugin.AnamcoreManager.ForceStopEmote(player.Address);
                            });

                            // 2. Pause so the idle pose settles before mod swap
                            await Task.Delay(100);

                            // 3. Swap mods and glamour
                            _plugin.Framework.RunOnFrameworkThread(() =>
                            {
                                // Enable new Penumbra mods (old ones restored in final cleanup)
                                disabledPenumbraMods = EnablePenumbraMod(switchConfig.PenumbraModFilter, npcCharacter, player);

                                // Apply new glamour designs
                                ApplyGlamourerDesign(switchConfig.NpcGlamourerDesign, npcCharacter);
                                ApplyGlamourerDesign(switchConfig.PartnerGlamourerDesign, player);
                            });

                            // 4. Let mods and glamour settle before starting new emotes
                            await Task.Delay(500);

                            // 5. Start new emotes
                            _plugin.Framework.RunOnFrameworkThread(() =>
                            {
                                ushort newNpcTimeline = ResolveEmoteToTimeline(switchConfig.NpcEmoteId);
                                if (switchConfig.NpcCposeIndex > 0)
                                    newNpcTimeline = ResolveCposeTimeline(newNpcTimeline, switchConfig.NpcCposeIndex);
                                ushort newPartnerTimeline = ResolveEmoteToTimeline(switchConfig.PartnerEmoteId);
                                if (switchConfig.PartnerCposeIndex > 0)
                                    newPartnerTimeline = ResolveCposeTimeline(newPartnerTimeline, switchConfig.PartnerCposeIndex);

                                if (newNpcTimeline > 0)
                                    _plugin.AnamcoreManager.TriggerEmote(npcCharacter.Address, newNpcTimeline, switchConfig.LoopAnimation);
                                if (newPartnerTimeline > 0 && player.Address != nint.Zero)
                                    _plugin.AnamcoreManager.TriggerEmote(player.Address, newPartnerTimeline, switchConfig.LoopAnimation);
                            });

                            // Update emotion and animation context overrides
                            npcData.TemporaryMoodOverride = switchConfig.EmotionOverride ?? "";
                            npcData.TemporaryAnimationContext = switchConfig.AnimationContext ?? "";

                            // Update the active config so cleanup uses the latest values
                            config = switchConfig;

                            // Reset duration timer for new animation
                            animTimer.Restart();
                            animStartPos = player.Position;
                            maxDurationMs = switchConfig.UseDuration
                                ? (switchConfig.LoopAnimation ? switchConfig.DurationMs : Math.Max(switchConfig.DurationMs, 5000))
                                : int.MaxValue;

                            _plugin.PluginLog.Information($"[PairedAnimation] Switched {npcData.NpcName} to new animation");
                        }

                        await Task.Delay(100);
                    }
                    _plugin.PluginLog.Information($"[PairedAnimation] Loop exited for {npcData.NpcName}. Elapsed={animTimer.ElapsedMilliseconds}ms, maxDuration={maxDurationMs}ms");

                    // Cleanup: stop animations, restore glamour, and restore state
                    _plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        npc.AnimationLocked = false;
                        _plugin.AnamcoreManager.ForceStopEmote(npcCharacter.Address);

                        if (config.PartnerEmoteId > 0 && player.Address != nint.Zero)
                        {
                            _plugin.AnamcoreManager.ForceStopEmote(player.Address);
                        }

                        // Restore saved Glamourer states
                        RestoreGlamourerState(npcCharacter, savedNpcState);
                        RestoreGlamourerState(player, savedPlayerState);

                        // Restore Penumbra mods
                        RestorePenumbraMods(config.PenumbraModFilter, disabledPenumbraMods, npcCharacter, player);

                        // Clear emotion and animation context overrides
                        npcData.TemporaryMoodOverride = "";
                        npcData.TemporaryAnimationContext = "";

                        if (wasFollowing)
                        {
                            npc.FollowPlayer(2);
                        }
                    });
                }
                catch (Exception ex)
                {
                    _plugin.PluginLog.Warning(ex, "[PairedAnimation] Error during player paired animation");
                }
                finally
                {
                    _activeAnimations.Remove(npcData.NpcName);
                }
            });
        }

        /// <summary>
        /// Execute a paired animation between two NPCs.
        /// NPC B walks to NPC A, both face each other, and both play their emotes.
        /// </summary>
        private void ExecuteNpcPairedAnimation(
            InteractiveNpc npcA, CustomNpcCharacter npcDataA, ICharacter npcCharacterA,
            InteractiveNpc npcB, string npcNameB, ICharacter npcCharacterB,
            PairedAnimationConfig config)
        {
            _activeAnimations.Add(npcDataA.NpcName);
            _activeAnimations.Add(npcNameB);

            Task.Run(async () =>
            {
                try
                {
                    bool wasFollowingA = npcA.IsFollowingPlayer;
                    bool wasFollowingB = npcB.IsFollowingPlayer;

                    // Stop both from following
                    npcA.StopFollowingPlayer();
                    npcB.StopFollowingPlayer();

                    // Turn NPC A to face NPC B before speaking
                    Vector3 faceRot = CoordinateUtility.LookAt(npcA.CurrentPosition, npcB.CurrentPosition).QuaternionToEuler();
                    _plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        npcA.SetDefaults(npcA.CurrentPosition, faceRot, 5f);
                    });

                    // Show a speech bubble on the initiating NPC
                    _plugin.SpeechBubbleManager?.ShowBubble(npcCharacterA, npcDataA.NpcName, GetApproachBubble());

                    // Wait for the approach delay so the bubble can be read
                    if (config.ApproachDelayMs > 0)
                        await Task.Delay(config.ApproachDelayMs);

                    // NPC B walks to NPC A's exact position for paired animation alignment
                    Vector3 npcAPos = npcA.CurrentPosition;
                    Vector3 targetPosB = npcAPos;
                    Vector3 walkFaceRot = CoordinateUtility.LookAt(npcB.CurrentPosition, npcAPos).QuaternionToEuler();

                    _plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        npcB.WalkToTarget(targetPosB, 5f);
                        npcB.SetDefaults(targetPosB, walkFaceRot, 5f);
                    });

                    // Wait for NPC B to arrive
                    var arrivalTimeout = Stopwatch.StartNew();
                    while (arrivalTimeout.ElapsedMilliseconds < 15000)
                    {
                        float dist = Vector3.Distance(npcB.CurrentPosition, targetPosB);
                        if (dist < 0.15f) break;
                        await Task.Delay(50);
                    }

                    // Match NPC A's rotation so both face the same direction
                    _plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        npcB.SetDefaults(targetPosB, npcA.CurrentRotation, 5f);
                    });

                    await Task.Delay(100);

                    // Stop movement, break idle emotes
                    _plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        npcB.ShouldBeMoving = false;
                        _plugin.AnamcoreManager.ForceStopEmote(npcCharacterA.Address);
                        _plugin.AnamcoreManager.ForceStopEmote(npcCharacterB.Address);
                    });

                    await Task.Delay(200);

                    // Lock both NPCs' animations
                    string savedNpcAState = null;
                    string savedNpcBState = null;
                    Dictionary<Guid, List<string>> disabledPenumbraMods = new Dictionary<Guid, List<string>>();

                    _plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        npcA.AnimationLocked = true;
                        npcB.AnimationLocked = true;

                        // Enable Penumbra mod if configured
                        disabledPenumbraMods = EnablePenumbraMod(config.PenumbraModFilter, npcCharacterA, npcCharacterB);

                        // Save current states before applying designs
                        if (!string.IsNullOrEmpty(config.NpcGlamourerDesign))
                            savedNpcAState = SaveGlamourerState(npcCharacterA);
                        if (!string.IsNullOrEmpty(config.PartnerGlamourerDesign))
                            savedNpcBState = SaveGlamourerState(npcCharacterB);

                        // Apply Glamourer designs if configured
                        ApplyGlamourerDesign(config.NpcGlamourerDesign, npcCharacterA);
                        ApplyGlamourerDesign(config.PartnerGlamourerDesign, npcCharacterB);

                        ushort npcTimelineId = ResolveEmoteToTimeline(config.NpcEmoteId);
                        if (config.NpcCposeIndex > 0)
                            npcTimelineId = ResolveCposeTimeline(npcTimelineId, config.NpcCposeIndex);
                        ushort partnerTimelineId = ResolveEmoteToTimeline(config.PartnerEmoteId);
                        if (config.PartnerCposeIndex > 0)
                            partnerTimelineId = ResolveCposeTimeline(partnerTimelineId, config.PartnerCposeIndex);

                        if (npcTimelineId > 0)
                            _plugin.AnamcoreManager.TriggerEmote(npcCharacterA.Address, npcTimelineId, config.LoopAnimation);

                        if (partnerTimelineId > 0)
                            _plugin.AnamcoreManager.TriggerEmote(npcCharacterB.Address, partnerTimelineId, config.LoopAnimation);
                    });

                    // Set emotion and animation context overrides if configured
                    if (!string.IsNullOrEmpty(config.EmotionOverride))
                    {
                        npcDataA.TemporaryMoodOverride = config.EmotionOverride;
                    }
                    if (!string.IsNullOrEmpty(config.AnimationContext))
                    {
                        npcDataA.TemporaryAnimationContext = config.AnimationContext;
                    }

                    var animTimer = Stopwatch.StartNew();
                    int maxDurationMs = config.UseDuration
                        ? (config.LoopAnimation ? config.DurationMs : Math.Max(config.DurationMs, 5000))
                        : int.MaxValue;

                    while (animTimer.ElapsedMilliseconds < maxDurationMs)
                    {
                        if (!_activeAnimations.Contains(npcDataA.NpcName) || !_activeAnimations.Contains(npcNameB))
                        {
                            break;
                        }

                        // Check if either moved manually
                        if (npcA.ShouldBeMoving || npcB.ShouldBeMoving || npcA.IsFollowingPlayer || npcB.IsFollowingPlayer)
                        {
                            break;
                        }

                        // Break if player moves too far away and neither is staying
                        var npcDataB = _plugin.Configuration.CustomNpcCharacters.FirstOrDefault(n => n.NpcName == npcNameB);
                        bool isStayingB = npcDataB != null && npcDataB.IsStaying;

                        if (!npcDataA.IsStaying && !isStayingB)
                        {
                            var player = _plugin.ObjectTable.LocalPlayer;
                            if (player != null && Vector3.Distance(npcA.CurrentPosition, player.Position) > 15f)
                            {
                                _plugin.PluginLog.Information($"[PairedAnimation] {npcDataA.NpcName} and {npcNameB} stopped: player moved too far away.");
                                break;
                            }
                        }

                        await Task.Delay(100);
                    }

                    // Cleanup: stop animations, restore glamour, and restore state
                    _plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        npcA.AnimationLocked = false;
                        npcB.AnimationLocked = false;
                        _plugin.AnamcoreManager.ForceStopEmote(npcCharacterA.Address);
                        _plugin.AnamcoreManager.ForceStopEmote(npcCharacterB.Address);

                        // Restore saved Glamourer states
                        RestoreGlamourerState(npcCharacterA, savedNpcAState);
                        RestoreGlamourerState(npcCharacterB, savedNpcBState);

                        // Restore Penumbra mods
                        RestorePenumbraMods(config.PenumbraModFilter, disabledPenumbraMods, npcCharacterA, npcCharacterB);

                        // Clear emotion and animation context overrides
                        npcDataA.TemporaryMoodOverride = "";
                        npcDataA.TemporaryAnimationContext = "";

                        if (wasFollowingA) npcA.FollowPlayer(2);
                        if (wasFollowingB) npcB.FollowPlayer(2);
                    });
                }
                catch (Exception ex)
                {
                    _plugin.PluginLog.Warning(ex, "[PairedAnimation] Error during NPC-NPC paired animation");
                }
                finally
                {
                    _activeAnimations.Remove(npcDataA.NpcName);
                    _activeAnimations.Remove(npcNameB);
                }
            });
        }

        /// <summary>
        /// Calculate a world position that is a given distance directly in front of a character
        /// based on their rotation (yaw in radians, FFXIV convention).
        /// </summary>
        private Vector3 GetPositionInFrontOf(Vector3 position, float rotationRadians, float distance)
        {
            // FFXIV rotation: 0 = south, positive = counter-clockwise
            // Forward direction in FFXIV is -sin(rot) on X, -cos(rot) on Z
            float frontX = position.X - MathF.Sin(rotationRadians) * distance;
            float frontZ = position.Z - MathF.Cos(rotationRadians) * distance;
            return new Vector3(frontX, position.Y, frontZ);
        }

        /// <summary>
        /// Returns a random short approach line for the NPC's speech bubble.
        /// </summary>
        private string GetApproachBubble()
        {
            string[] lines = new[]
            {
                "Of course!",
                "Sure, let's do it!",
                "I'd love to!",
                "Right away!",
                "With pleasure!",
                "You got it!",
                "Let's go!",
                "Gladly!",
            };
            return lines[Random.Shared.Next(lines.Length)];
        }

        /// <summary>
        /// Resolve an Emote RowId to its ActionTimeline RowId for animation playback.
        /// Returns 0 if the emote is not found or has no ActionTimeline.
        /// </summary>
        private ushort ResolveEmoteToTimeline(ushort emoteRowId)
        {
            if (emoteRowId == 0) return 0;
            try
            {
                var emoteSheet = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>();
                if (emoteSheet == null) return 0;
                var emote = emoteSheet.GetRow(emoteRowId);
                if (emote.RowId > 0 && emote.ActionTimeline[0].Value.RowId > 0)
                {
                    return (ushort)emote.ActionTimeline[0].Value.RowId;
                }
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Resolve an emote's ActionTimeline to a /cpose variant.
        /// The base timeline key (e.g. "emote/jmn") tells us the emote family prefix (e.g. "j" for groundsit).
        /// Cpose variants are separate ActionTimeline entries with keys like "emote/j_pose01_loop".
        /// We construct the variant key and search the ActionTimeline sheet for it.
        /// </summary>
        private ushort ResolveCposeTimeline(ushort baseTimelineId, int cposeIndex)
        {
            if (baseTimelineId == 0 || cposeIndex <= 0) return baseTimelineId;
            try
            {
                var timelineSheet = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ActionTimeline>();
                if (timelineSheet == null) return baseTimelineId;

                var baseTimeline = timelineSheet.GetRow(baseTimelineId);
                string baseKey = baseTimeline.Key.ToString();
                if (string.IsNullOrEmpty(baseKey)) return baseTimelineId;

                // Extract the emote prefix from the key
                // Base keys look like "emote/jmn" (groundsit), "emote/lmn" (doze), etc.
                // The prefix letter(s) before "mn" indicate the emote family
                string prefix = "";
                if (baseKey.StartsWith("emote/"))
                {
                    string afterEmote = baseKey.Substring(6);
                    foreach (char c in afterEmote)
                    {
                        if (char.IsLetter(c))
                            prefix += c;
                        else
                            break;
                    }
                    // Strip the "mn" suffix to get the family prefix
                    // "jmn" -> "j", "lmn" -> "l"
                    if (prefix.EndsWith("mn"))
                        prefix = prefix.Substring(0, prefix.Length - 2);
                }

                if (string.IsNullOrEmpty(prefix))
                    return baseTimelineId;

                // Construct the cpose variant key: "emote/{prefix}_pose{XX}_loop"
                string targetKey = "emote/" + prefix + "_pose" + cposeIndex.ToString("D2") + "_loop";

                // Search the ActionTimeline sheet for the matching key
                foreach (var timeline in timelineSheet)
                {
                    if (timeline.Key.ToString() == targetKey)
                        return (ushort)timeline.RowId;
                }
            }
            catch { }
            return baseTimelineId;
        }

        /// <summary>
        /// Enable a Penumbra mod by partial folder name match, disabling any conflicting mods
        /// that affect the same game file paths. Returns a dictionary of collection IDs to the
        /// list of mod directory names that were disabled so they can be restored later.
        /// </summary>
        private Dictionary<Guid, List<string>> EnablePenumbraMod(string modFilter, ICharacter char1, ICharacter char2)
        {
            var disabledMods = new Dictionary<Guid, List<string>>();
            if (string.IsNullOrEmpty(modFilter)) return disabledMods;
            try
            {
                var ipc = PenumbraAndGlamourerIpcWrapper.Instance;
                var collections = new HashSet<Guid>();
                if (char1 != null) collections.Add(ipc.GetCollectionForObject.Invoke(char1.ObjectIndex).EffectiveCollection.Id);
                if (char2 != null) collections.Add(ipc.GetCollectionForObject.Invoke(char2.ObjectIndex).EffectiveCollection.Id);

                // Get the Penumbra mod root directory
                string modRoot = ipc.GetModDirectory.Invoke();
                if (string.IsNullOrEmpty(modRoot) || !System.IO.Directory.Exists(modRoot))
                {
                    _plugin.ChatGui.Print("[Penumbra] Mod directory not found: " + modRoot);
                    return disabledMods;
                }

                // Search all mod folders for a partial name match
                string targetModDir = null;
                string targetModFolder = null;
                foreach (var dir in System.IO.Directory.GetDirectories(modRoot))
                {
                    string folderName = System.IO.Path.GetFileName(dir);
                    if (folderName.Contains(modFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        targetModDir = dir;
                        targetModFolder = folderName;
                        break;
                    }
                }

                if (targetModDir == null)
                {
                    _plugin.ChatGui.Print("[Penumbra] No mod folder matching: " + modFilter);
                    return disabledMods;
                }

                _plugin.ChatGui.Print("[Penumbra] Found mod: " + targetModFolder);

                // Ask Penumbra what paths this mod affects
                var targetItems = ipc.GetChangedItemsForMod.Invoke(targetModFolder, "");
                var targetPaths = new HashSet<string>(targetItems.Keys, StringComparer.OrdinalIgnoreCase);
                _plugin.ChatGui.Print("[Penumbra] Target mod affects " + targetPaths.Count + " items");

                // Find and disable conflicting mods (other mod folders with overlapping paths)
                foreach (var collection in collections)
                {
                    var collectionDisabled = new List<string>();
                    disabledMods[collection] = collectionDisabled;

                    if (targetPaths.Count > 0)
                    {
                        foreach (var dir in System.IO.Directory.GetDirectories(modRoot))
                        {
                            string folderName = System.IO.Path.GetFileName(dir);
                            if (folderName == targetModFolder) continue;

                            try
                            {
                                var otherItems = ipc.GetChangedItemsForMod.Invoke(folderName, "");
                                bool hasOverlap = otherItems.Keys.Any(p => targetPaths.Contains(p));
                                if (hasOverlap)
                                {
                                    // Check if this mod is currently enabled for this collection
                                    var settings = ipc.GetCurrentModSettings.Invoke(collection, folderName, "", false);
                                    bool isEnabled = settings.Item1 == Penumbra.Api.Enums.PenumbraApiEc.Success
                                        && settings.Item2 != null
                                        && settings.Item2.Value.Item1;
                                    if (isEnabled)
                                    {
                                        _plugin.ChatGui.Print($"[Penumbra] Disabling conflicting: {folderName} on collection {collection}");
                                        ipc.TrySetMod.Invoke(collection, folderName, false);
                                        collectionDisabled.Add(folderName);
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    // Enable the target mod
                    ipc.TrySetMod.Invoke(collection, targetModFolder, true);
                    ipc.TrySetModPriority.Invoke(collection, targetModFolder, 11);
                }

                int totalDisabled = disabledMods.Values.Sum(v => v.Count);
                _plugin.ChatGui.Print("[Penumbra] Enabled: " + targetModFolder
                    + (totalDisabled > 0 ? " (disabled " + totalDisabled + " conflicting)" : ""));
            }
            catch (Exception ex)
            {
                _plugin.ChatGui.Print("[Penumbra] Error: " + ex.Message);
            }
            return disabledMods;
        }

        /// <summary>
        /// Restore Penumbra mods that were disabled during animation.
        /// Re-enables the previously disabled mods and disables the one we enabled.
        /// </summary>
        private void RestorePenumbraMods(string modFilter, Dictionary<Guid, List<string>> disabledMods, ICharacter char1, ICharacter char2)
        {
            if (string.IsNullOrEmpty(modFilter) && (disabledMods == null || disabledMods.Count == 0)) return;
            try
            {
                var ipc = PenumbraAndGlamourerIpcWrapper.Instance;
                var collections = new HashSet<Guid>();
                if (char1 != null) collections.Add(ipc.GetCollectionForObject.Invoke(char1.ObjectIndex).EffectiveCollection.Id);
                if (char2 != null) collections.Add(ipc.GetCollectionForObject.Invoke(char2.ObjectIndex).EffectiveCollection.Id);

                // Re-enable mods we disabled and disable the mod we enabled
                string modRoot = string.IsNullOrEmpty(modFilter) ? null : ipc.GetModDirectory.Invoke();
                bool hasModRoot = !string.IsNullOrEmpty(modRoot) && System.IO.Directory.Exists(modRoot);

                foreach (var collection in collections)
                {
                    if (disabledMods.TryGetValue(collection, out var collectionDisabled))
                    {
                        foreach (var modDir in collectionDisabled)
                        {
                            try { ipc.TrySetMod.Invoke(collection, modDir, true); }
                            catch { }
                        }
                    }

                    if (hasModRoot)
                    {
                        foreach (var dir in System.IO.Directory.GetDirectories(modRoot))
                        {
                            string folderName = System.IO.Path.GetFileName(dir);
                            if (folderName.Contains(modFilter, StringComparison.OrdinalIgnoreCase))
                            {
                                ipc.TrySetMod.Invoke(collection, folderName, false);
                                break;
                            }
                        }
                    }
                }
            }
            catch { }
        }


        /// <summary>
        /// Apply a Glamourer design by name to a character. Returns true if successful.
        /// </summary>
        private bool ApplyGlamourerDesign(string designName, ICharacter character)
        {
            if (string.IsNullOrEmpty(designName) || character == null) return false;
            try
            {
                var designs = PenumbraAndGlamourerHelpers.PenumbraAndGlamourerHelperFunctions.GetGlamourerDesigns();
                foreach (var design in designs)
                {
                    if (design.Value.Equals(designName, StringComparison.OrdinalIgnoreCase))
                    {
                        PenumbraAndGlamourerIpcWrapper.Instance.ApplyDesign.Invoke(design.Key, character.ObjectIndex);
                        return true;
                    }
                }
                _plugin.PluginLog.Warning($"[PairedAnimation] Glamourer design '{designName}' not found.");
            }
            catch (Exception ex)
            {
                _plugin.PluginLog.Warning(ex, $"[PairedAnimation] Failed to apply Glamourer design '{designName}'");
            }
            return false;
        }

        /// <summary>
        /// Save a character's current Glamourer state (base64) so it can be restored later.
        /// Returns the base64 string, or null on failure.
        /// </summary>
        private string SaveGlamourerState(ICharacter character)
        {
            if (character == null) return null;
            try
            {
                var result = PenumbraAndGlamourerIpcWrapper.Instance.GetStateBase64.Invoke(character.ObjectIndex);
                return result.Item2;
            }
            catch (Exception ex)
            {
                _plugin.PluginLog.Warning(ex, "[PairedAnimation] Failed to save Glamourer state");
            }
            return null;
        }

        /// <summary>
        /// Restore a character's Glamourer state from a previously saved base64 string.
        /// </summary>
        private void RestoreGlamourerState(ICharacter character, string savedState)
        {
            if (character == null || string.IsNullOrEmpty(savedState)) return;
            try
            {
                PenumbraAndGlamourerIpcWrapper.Instance.ApplyState.Invoke(savedState, character.ObjectIndex, 0,
                    ApplyFlag.Equipment | ApplyFlag.Customization);
            }
            catch (Exception ex)
            {
                _plugin.PluginLog.Warning(ex, "[PairedAnimation] Failed to restore Glamourer state");
            }
        }
    }
}
