using Anamnesis.Memory;
using Brio.Capabilities.Posing;
using Brio;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Common.Lua;
using SamplePlugin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using AnamCore;
using McdfDataImporter;
using RoleplayingQuestCore;
using AQuestReborn.CustomNpc;
using System.Diagnostics;
using Quaternion = System.Numerics.Quaternion;
using Brio.Core;
using Brio.Capabilities.Actor;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.Game;
using static RoleplayingQuestCore.QuestEvent;

namespace AQuestReborn
{
    public class InteractiveNpc : IDisposable
    {
        public static string LastCombatTarget = "";
        public static HashSet<string> CombatTargets = new HashSet<string>();
        private ICharacter _character;
        private ushort _cachedObjectIndex = ushort.MaxValue;
        private nint SafeCharacterAddress
        {
            get
            {
                try
                {
                    if (_character == null || _cachedObjectIndex == ushort.MaxValue) return nint.Zero;
                    // Use the cached ObjectIndex to look up a fresh reference from the
                    // object table via its safe, bounds-checked indexer. This avoids
                    // touching any stale native pointer — the table returns null for
                    // empty / recycled slots without dereferencing freed memory.
                    var fresh = _plugin.ObjectTable[(int)_cachedObjectIndex];
                    if (fresh == null) return nint.Zero;
                    return fresh.Address;
                }
                catch
                {
                    return nint.Zero;
                }
            }
        }
        private Plugin _plugin;
        private bool _shouldBeMoving;
        private Vector3 _target;
        private float _speed = 5;
        private QuestEvent.EventMovementType _eventMovementType;
        private bool _shouldBeScaling;
        private Vector3 _targetScale = new Vector3(1, 1, 1);
        private float _scaleSpeed = 10;
        private bool _followPlayer;
        public bool IsFollowingPlayer => _followPlayer;
        private List<Vector3> _currentBreadcrumbPath = new List<Vector3>();
        private int _currentBreadcrumbTargetIndex = 0;
        private Vector3 _currentPosition;
        private Vector3 _followStart;
        private Vector3 _defaultPosition;
        private Vector3 _defaultRotation;
        private Vector3 _currentRotation;
        private bool _disposed;
        private Vector3 _currentScale;
        private PosingCapability? _posing;
        private uint _lastMovementAnimationId = uint.MaxValue;
        private float _lastDistanceToPlayerForCulling;
        private int _index;
        private bool _followDataLock;
        private bool firstPositionSet;
        private Vector3 _lastDefaultPosition;
        private Vector3 _lastDefaultRotation;
        private Vector3 _snapPosition;
        private PosingCapability? _playerPosing;
        private float _horizontalOffset;
        Stopwatch _horizontalRefreshTimer = new Stopwatch();
        Stopwatch _fixedMovementTimer = new Stopwatch();
        Stopwatch _idleTimer = new Stopwatch();
        Stopwatch _emoteExitCooldown = new Stopwatch();
        private int _idleThresholdMs = 20000;
        private bool _idleEmotePlaying;
        private bool _victoryPosePlaying;
        private ushort _idleEmoteId;
        private bool _wasMoving;
        private bool _isCombatMoving;
        private bool _isFollowMoving;
        private ushort _activeEmoteTimelineId;
        private bool _waitingForEmoteExit;
        private bool _wasInCombat;
        private ushort _lastPlayerTimelineId;
        private ushort _nextCombatAnimationToPlay;
        private Stopwatch _combatAttackDelayTimer = new Stopwatch();
        private int _currentCombatDelayMs;
        private ushort _queuedVictoryPose;
        private Stopwatch _victoryPoseDelayTimer = new Stopwatch();
        private int _victoryPoseDelayMs;
        private Stopwatch _victoryPoseLockTimer = new Stopwatch();
        private Stopwatch _autonomousAttackTimer = new Stopwatch();
        private int _nextAutonomousAttackMs;
        private Vector3 _lastPlayerPos;
        private float _playerSpeedSmoothed;
        private float _stamina = 100f;
        private bool _playerIsDead;
        private bool _reactingToPlayerDeath;
        private bool _deathEmotePlayed;
        private float _deathSpreadAngle;
        private bool _wasSwimming;
        /// <summary>
        /// Vertical offset applied when swim BaseOverride is active.
        /// The swim animation shifts the model up, so we push the position down to compensate.
        /// </summary>
        private const float SwimYOffset = -0.65f;
        /// <summary>
        /// Max Y-axis change per frame. The game engine nukes DrawObjects when characters
        /// move too far vertically in a single frame (e.g. falling from cliffs).
        /// </summary>
        private const float MaxYDeltaPerFrame = 1.5f;
        EventMovementAnimation _eventMovementAnimationType = EventMovementAnimation.Automatic;
        public static Dictionary<uint, List<ushort>> JobCombatAnimations = null;

        public static void LoadJobAnimations(Plugin plugin)
        {
            if (JobCombatAnimations != null) return;
            JobCombatAnimations = new Dictionary<uint, List<ushort>>();
            try
            {
                var actions = plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
                var jobs = plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>();
                
                // Cache job abbreviations
                var jobProps = new Dictionary<uint, System.Reflection.PropertyInfo>();
                var jobCategoriesType = typeof(Lumina.Excel.Sheets.ClassJobCategory);
                foreach (var job in jobs)
                {
                    if (job.RowId == 0) continue;
                    var prop = jobCategoriesType.GetProperty(job.Abbreviation.ToString());
                    if (prop != null)
                    {
                        jobProps[job.RowId] = prop;
                    }
                }

                foreach (var action in actions)
                {
                    uint animIdEnd = action.AnimationEnd.RowId;
                    uint animIdStart = action.AnimationStart.RowId;
                    uint catId = action.ActionCategory.RowId;

                    if (catId >= 2 && catId <= 4 && action.IsPlayerAction)
                    {
                        var animIdsToMap = new List<uint>();
                        if (animIdEnd > 0) animIdsToMap.Add(animIdEnd);
                        if (animIdStart > 0) animIdsToMap.Add(animIdStart);

                        foreach (uint animId in animIdsToMap)
                        {
                            // Some actions have direct ClassJob
                            uint directJobId = action.ClassJob.RowId;
                            if (directJobId > 0)
                            {
                                if (!JobCombatAnimations.ContainsKey(directJobId))
                                    JobCombatAnimations[directJobId] = new List<ushort>();
                                if (!JobCombatAnimations[directJobId].Contains((ushort)animId))
                                    JobCombatAnimations[directJobId].Add((ushort)animId);
                            }

                            // Map via ClassJobCategory
                            var cjc = action.ClassJobCategory.Value;
                            if (cjc.RowId > 0)
                            {
                                foreach (var kvp in jobProps)
                                {
                                    bool allowed = (bool)kvp.Value.GetValue(cjc);
                                    if (allowed)
                                    {
                                        uint jobId = kvp.Key;
                                        if (!JobCombatAnimations.ContainsKey(jobId))
                                            JobCombatAnimations[jobId] = new List<ushort>();
                                        if (!JobCombatAnimations[jobId].Contains((ushort)animId))
                                            JobCombatAnimations[jobId].Add((ushort)animId);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                plugin.PluginLog.Warning(e, "Failed to load job combat animations");
            }
        }

        public string LastAppearance { get; internal set; }
        public bool LooksAtPlayer { get; internal set; }
        public bool ShouldBeMoving { get => _shouldBeMoving; set => _shouldBeMoving = value; }
        public ICharacter Character
        {
            get => _character;
            set
            {
                _character = value;
                _cachedObjectIndex = value?.ObjectIndex ?? ushort.MaxValue;
            }
        }
        public EventMovementAnimation EventMovementAnimationType { get => _eventMovementAnimationType; set => _eventMovementAnimationType = value; }
        public ushort VictoryPoseEmoteId { get; set; }
        public List<ushort> RandomIdleEmotes = new List<ushort>();
        public ushort IdleEmoteId
        {
            get => _idleEmoteId;
            set
            {
                _idleEmoteId = value;
                _idleEmotePlaying = false;
                _idleTimer.Restart();
                _idleThresholdMs = 20000 + new System.Random().Next(20000);
            }
        }
        
        public uint TargetClassJobId { get; set; }
        public uint TargetWeaponItemId { get; set; }
        public bool ClassWeaponApplied { get; set; }
        public CustomNpcCharacter NpcConfig { get; set; }

        public InteractiveNpc(Plugin plugin, ICharacter character)
        {
            _character = character;
            _cachedObjectIndex = character.ObjectIndex;
            _plugin = plugin;
            _plugin.Framework.Update += Framework_Update;
            _plugin.ClientState.TerritoryChanged += ClientState_TerritoryChanged;
            BrioAccessUtils.EntityManager.SetSelectedEntity(_character);
            BrioAccessUtils.EntityManager.TryGetCapabilityFromSelectedEntity<PosingCapability>(out var posing);
            _posing = posing;
            _index = _plugin.AQuestReborn.InteractiveNpcDictionary.Count;
            _currentPosition = character.Position;
            _defaultPosition = character.Position;
            _horizontalRefreshTimer.Start();
            _idleTimer.Start();
            _idleThresholdMs = 20000 + new System.Random().Next(20000);
        }

        private void ClientState_TerritoryChanged(uint obj)
        {
            Dispose();
        }

        public void HideNPC()
        {
            _targetScale = new Vector3(0.0001f, 0.0001f, 0.0001f);
        }
        public void ShowNPC()
        {
            _targetScale = new Vector3(1f, 1f, 1f);
        }

        public unsafe void ApplyClassWeapon()
        {
            if (_character == null || !_character.IsValid()) return;
            if (TargetClassJobId == 0)
            {
                _plugin.AnamcoreManager.SetWeapon(_character, 0, 0);
                var nullChara = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)SafeCharacterAddress;
                nullChara->ClassJob = 0;
                return;
            }

            var cj = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>().GetRow(TargetClassJobId);
            if (cj.RowId == 0) return;
            string abrv = cj.Abbreviation.ToString();
            var prop = typeof(Lumina.Excel.Sheets.ClassJobCategory).GetProperty(abrv);
            if (prop == null) return;

            var items = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            
            ulong mainHandModel = 0;
            ulong offHandModel = 0;

            if (TargetWeaponItemId > 0)
            {
                var specificItem = items.GetRow(TargetWeaponItemId);
                if (specificItem.RowId != 0 && specificItem.ModelMain != 0)
                {
                    mainHandModel = specificItem.ModelMain;
                    offHandModel = specificItem.ModelSub;
                }
            }
            
            if (mainHandModel == 0)
            {
                foreach (var item in items)
                {
                    if (item.EquipSlotCategory.RowId == 1 || item.EquipSlotCategory.RowId == 13) 
                    {
                        var cjc = item.ClassJobCategory.Value;
                        if (cjc.RowId != 0)
                        {
                            bool allowed = (bool)prop.GetValue(cjc);
                            if (allowed && item.ModelMain != 0)
                            {
                                mainHandModel = item.ModelMain;
                                offHandModel = item.ModelSub;
                                break; 
                            }
                        }
                    }
                }
            }

            if (mainHandModel != 0)
            {
                var chara = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)SafeCharacterAddress;
                
                // Always force the weapon — Glamourer may have already set a different model
                _plugin.AnamcoreManager.SetWeapon(_character, mainHandModel, offHandModel);
                
                // Force the NPC's class job to match so combat stances and animations are natively correct!
                chara->ClassJob = (byte)TargetClassJobId;
            }
        }

        public unsafe uint ContextBasedMovementId(bool isMoving, float speed = 6.0f)
        {
            if (Conditions.Instance()->Swimming || Conditions.Instance()->Diving)
            {
                if (!isMoving) return 4947u; // Swim idle (treading water)

                // Check if the local player is currently sprinting (Status 50)
                if (speed >= 4.5f && _plugin.ObjectTable != null && _plugin.ObjectTable.LocalPlayer != null)
                {
                    foreach (var status in _plugin.ObjectTable.LocalPlayer.StatusList)
                    {
                        if (status.StatusId == 50) return 4958u; // Swim sprint
                    }
                }

                return speed < 3.5f ? 4950u : 4954u; // Swim walk : Swim run
            }
            else
            {
                if (!isMoving) return 0u;

                // Check if the local player is currently sprinting (Status 50)
                if (speed >= 4.5f && _plugin.ObjectTable != null && _plugin.ObjectTable.LocalPlayer != null)
                {
                    foreach (var status in _plugin.ObjectTable.LocalPlayer.StatusList)
                    {
                        if (status.StatusId == 50) return 30u;
                    }
                }

                return speed < 3.5f ? 13u : 22u;
            }
        }

        private void TriggerEmoteIfChanged(uint animationId)
        {
            if (_character == null || !_character.IsValid()) return;
            if (_lastMovementAnimationId != animationId)
            {
                _lastMovementAnimationId = animationId;
                _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, animationId);
            }
        }

        public unsafe void Framework_Update(IFramework framework)
        {
            if (!_disposed)
            {
                try
                {
                    if (_plugin.AQuestReborn != null && !_plugin.AQuestReborn.WaitingForMcdfLoad && (AppearanceAccessUtils.AppearanceManager == null || !AppearanceAccessUtils.AppearanceManager.IsWorking()) && _plugin.ClientState.IsLoggedIn)
                    {
                        // Validate character address is live in the object table BEFORE any native access.
                        // This prevents AccessViolationException (which cannot be caught in .NET Core)
                        // when the character object has been freed during logout→login transitions.
                        var safeAddr = SafeCharacterAddress;
                        if (safeAddr != nint.Zero)
                        {
                            if (_plugin.ObjectTable.LocalPlayer == null || !_plugin.ObjectTable.LocalPlayer.IsValid()) return;

                            // Weapon and Job will be applied right before entering combat for the first time.
                            float delta = ((float)_plugin.Framework.UpdateDelta.Milliseconds / 1000f);
                            if (delta > 0)
                            {
                                float playerSpeedThisFrame = Vector2.Distance(
                                    new Vector2(_plugin.ObjectTable.LocalPlayer.Position.X, _plugin.ObjectTable.LocalPlayer.Position.Z),
                                    new Vector2(_lastPlayerPos.X, _lastPlayerPos.Z)) / delta;
                                _lastPlayerPos = _plugin.ObjectTable.LocalPlayer.Position;
                                _playerSpeedSmoothed = Math.Clamp(_playerSpeedSmoothed + (playerSpeedThisFrame - _playerSpeedSmoothed) * Math.Min(10f * delta, 1f), 0f, 15f);
                            }
                            // Tail playback takes over the entire update loop
                            if (_isTailPlayback)
                            {
                                UpdateTailPlayback(delta);
                                return;
                            }
                            // Detect swimming state changes and force-reset animations
                            bool isSwimming = Conditions.Instance()->Swimming || Conditions.Instance()->Diving;
                            if (isSwimming != _wasSwimming)
                            {
                                _wasSwimming = isSwimming;
                                _lastMovementAnimationId = uint.MaxValue; // Force re-trigger
                                if (_idleEmotePlaying)
                                {
                                    _plugin.AnamcoreManager.ForceStopEmote(SafeCharacterAddress);
                                    _idleEmotePlaying = false;
                                }
                                _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, ContextBasedMovementId(false));
                            }
                            if (_followPlayer && !_plugin.EventWindow.IsOpen && !_plugin.ChoiceWindow.IsOpen
                                && _plugin.EventWindow.TimeSinceLastDialogueDisplayed.ElapsedMilliseconds > 200
                                && _plugin.ChoiceWindow.TimeSinceLastChoiceMade.ElapsedMilliseconds > 200 && !Conditions.Instance()->Mounted)
                            {
                                var followingNpcs = _plugin.AQuestReborn.InteractiveNpcDictionary.Values.Where(n => n.IsFollowingPlayer).ToList();
                                int followerCount = Math.Max(1, followingNpcs.Count);
                                int followerIndex = Math.Max(0, followingNpcs.IndexOf(this));

                                var playerPos = _plugin.ObjectTable.LocalPlayer.Position;
                                var targetPosition = playerPos
                                        + GetVerticalOffsetFromPlayer((followerIndex) - ((float)(followerCount - 1) / 2f))
                                        + GetHorizontalOffsetFromPlayer(_horizontalOffset);
                                
                                float distToPlayer = Vector3.Distance(_currentPosition, playerPos);

                                // Force re-trigger animations if the NPC pops back into render distance
                                if ((_lastDistanceToPlayerForCulling >= 35.0f && distToPlayer < 35.0f) ||
                                    (_lastDistanceToPlayerForCulling >= 20.0f && distToPlayer < 20.0f))
                                {
                                    _lastMovementAnimationId = uint.MaxValue;
                                }
                                _lastDistanceToPlayerForCulling = distToPlayer;

                                // If the player is far away, attempt to use the breadcrumb path
                                if (distToPlayer > 10.0f)
                                {
                                    if (_currentBreadcrumbPath.Count == 0 || Vector3.DistanceSquared(_currentBreadcrumbPath.Last(), playerPos) > 100.0f)
                                    {
                                        _currentBreadcrumbPath = _plugin.AQuestReborn.BreadcrumbMap.GetPath(_currentPosition, playerPos, out _);
                                        _currentBreadcrumbTargetIndex = 0;
                                    }
                                }
                                else if (distToPlayer < 5.0f)
                                {
                                    _currentBreadcrumbPath.Clear();
                                }

                                if (_currentBreadcrumbPath.Count > 0 && _currentBreadcrumbTargetIndex < _currentBreadcrumbPath.Count)
                                {
                                    targetPosition = _currentBreadcrumbPath[_currentBreadcrumbTargetIndex];
                                    
                                    // If we are close to the current breadcrumb, move to the next one
                                    float dxNode = _currentPosition.X - targetPosition.X;
                                    float dzNode = _currentPosition.Z - targetPosition.Z;
                                    if (dxNode * dxNode + dzNode * dzNode < 2.0f * 2.0f)
                                    {
                                        _currentBreadcrumbTargetIndex++;
                                        if (_currentBreadcrumbTargetIndex < _currentBreadcrumbPath.Count)
                                            targetPosition = _currentBreadcrumbPath[_currentBreadcrumbTargetIndex];
                                    }
                                }

                                float distToTarget = Vector3.Distance(_currentPosition, targetPosition);
                                float distToFinalTarget = _currentBreadcrumbPath.Count > 0 ? distToPlayer : distToTarget;

                                // Check if player is facing the NPC
                                bool playerFacingNpc = false;
                                if (distToFinalTarget > 0.5f)
                                {
                                    float playerRot = _plugin.ObjectTable.LocalPlayer.Rotation; // radians, yaw
                                    float dx = _currentPosition.X - _plugin.ObjectTable.LocalPlayer.Position.X;
                                    float dz = _currentPosition.Z - _plugin.ObjectTable.LocalPlayer.Position.Z;
                                    float angleToNpc = MathF.Atan2(dx, dz);
                                    float diff = angleToNpc - playerRot;
                                    // Normalize to [-π, π]
                                    while (diff > MathF.PI) diff -= 2f * MathF.PI;
                                    while (diff < -MathF.PI) diff += 2f * MathF.PI;
                                    playerFacingNpc = MathF.Abs(diff) < MathF.PI / 4f; // 45° half-angle
                                }
                                // Hysteresis: start moving at 2.5y, keep moving until within 1.5y
                                // Freeze when player is directly facing the NPC
                                if (!playerFacingNpc && distToFinalTarget > 2.5f)
                                {
                                    if (_queuedVictoryPose > 0 || (_victoryPoseLockTimer.IsRunning && _victoryPoseLockTimer.ElapsedMilliseconds < 3000))
                                    {
                                        // Do not move while victory pose is queued or playing
                                        _isFollowMoving = false;
                                    }
                                    else
                                    {
                                        _isFollowMoving = true;
                                    }
                                }
                                if (distToFinalTarget <= 1.5f || playerFacingNpc) _isFollowMoving = false;
                                
                                bool inCombat = Conditions.Instance()->InCombat;
                                if (inCombat) _isFollowMoving = false;

                                // --- Player Death Reaction ---
                                bool playerDead = _plugin.ObjectTable.LocalPlayer.CurrentHp == 0;
                                if (playerDead && !_reactingToPlayerDeath)
                                {
                                    _reactingToPlayerDeath = true;
                                    _deathEmotePlayed = false;
                                    _isFollowMoving = false;
                                    // Compute spread angle once so it doesn't shift as NPC count changes
                                    _deathSpreadAngle = ((followerIndex - 1) * (MathF.PI * 2f / followerCount));
                                    // Break idle emote if playing
                                    if (_idleEmotePlaying)
                                    {
                                        _plugin.AnamcoreManager.ForceStopEmote(SafeCharacterAddress);
                                        _idleEmotePlaying = false;
                                    }
                                    // Trigger speech bubble reaction (only one NPC triggers the call)
                                    if (followerIndex == 0)
                                    {
                                        _plugin.SpeechBubbleManager?.NotifyPlayerDeath();
                                    }
                                }
                                else if (!playerDead && _reactingToPlayerDeath)
                                {
                                    // Player revived — clear state
                                    _reactingToPlayerDeath = false;
                                    _deathEmotePlayed = false;
                                    _plugin.AnamcoreManager.ForceStopEmote(SafeCharacterAddress);
                                    TriggerEmoteIfChanged(ContextBasedMovementId(false));
                                    if (followerIndex == 0)
                                    {
                                        _plugin.SpeechBubbleManager?.NotifyPlayerRevived();
                                    }
                                }

                                if (_reactingToPlayerDeath)
                                {
                                    var playerBody = _plugin.ObjectTable.LocalPlayer.Position;

                                    // Use pre-computed spread angle (set once on death start)
                                    var spreadTarget = playerBody + new Vector3(MathF.Cos(_deathSpreadAngle) * 1.2f, 0, MathF.Sin(_deathSpreadAngle) * 1.2f);

                                    float distToSpot = Vector3.Distance(
                                        new Vector3(_currentPosition.X, 0, _currentPosition.Z),
                                        new Vector3(spreadTarget.X, 0, spreadTarget.Z));

                                    if (distToSpot > 0.3f)
                                    {
                                        // Sprint to the spread position near the body
                                        float rushSpeed = 7.8f;
                                        var dirToSpot = Vector3.Normalize(new Vector3(spreadTarget.X - _currentPosition.X, 0, spreadTarget.Z - _currentPosition.Z));
                                        float maxMove = rushSpeed * delta;
                                        if (distToSpot <= maxMove)
                                        {
                                            _currentPosition.X = spreadTarget.X;
                                            _currentPosition.Z = spreadTarget.Z;
                                        }
                                        else
                                        {
                                            _currentPosition.X += dirToSpot.X * maxMove;
                                            _currentPosition.Z += dirToSpot.Z * maxMove;
                                        }
                                        float groundY = _plugin.AQuestReborn.GroundMap.GetGroundY(
                                            _currentPosition.X, _currentPosition.Z, playerBody.Y);
                                        float targetY = groundY + (_wasSwimming ? SwimYOffset : 0f);
                                        float yDelta = targetY - _currentPosition.Y;
                                        yDelta = Math.Clamp(yDelta, -MaxYDeltaPerFrame, MaxYDeltaPerFrame);
                                        float yLerp = Math.Clamp(10f * delta, 0f, 1f);
                                        _currentPosition.Y += yDelta * yLerp;

                                        // Face the player body
                                        var desiredQuat = CoordinateUtility.LookAt(_currentPosition, playerBody);
                                        var currentQuat = CoordinateUtility.ToQuaternion(_currentRotation);
                                        var smoothed = Quaternion.Slerp(currentQuat, desiredQuat, Math.Min(10f * delta, 1f));
                                        _currentRotation = smoothed.QuaternionToEuler();

                                        TriggerEmoteIfChanged(ContextBasedMovementId(true, rushSpeed));
                                    }
                                    else if (!_deathEmotePlayed)
                                    {
                                        // Arrived at spread position — stop run, play grief emote
                                        _deathEmotePlayed = true;
                                        _plugin.AnamcoreManager.ForceStopEmote(SafeCharacterAddress);
                                        // Face the player body
                                        var desiredQuat = CoordinateUtility.LookAt(_currentPosition, playerBody);
                                        _currentRotation = desiredQuat.QuaternionToEuler();

                                        // Pick a random grief emote: cry(24), kneel(19), comfort(57)
                                        ushort[] griefEmotes = new ushort[] { 24, 19, 57 };
                                        ushort chosen = griefEmotes[new Random(Environment.TickCount + _index).Next(griefEmotes.Length)];
                                        try
                                        {
                                            var emote = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>().GetRow(chosen);
                                            TriggerEmoteIfChanged((ushort)emote.ActionTimeline[0].Value.RowId);
                                        }
                                        catch
                                        {
                                            TriggerEmoteIfChanged(ContextBasedMovementId(false));
                                        }
                                    }

                                    _currentScale = Vector3.Lerp(_currentScale, _targetScale, _scaleSpeed * delta);
                                    SetTransform(_currentPosition, _currentRotation, _currentScale);
                                    return; // Skip all normal follow/combat logic
                                }

                                if (_isFollowMoving)
                                {
                                    // Always reset idle timer while moving
                                    _idleTimer.Restart();
                                    // Clear emote state - give StopEmote one frame to process
                                    if (_idleEmotePlaying || _victoryPosePlaying)
                                    {
                                        _plugin.AnamcoreManager.ForceStopEmote(SafeCharacterAddress);
                                        _idleEmotePlaying = false;
                                        _victoryPosePlaying = false;
                                        SetTransform(_currentPosition, _currentRotation, _currentScale);
                                        return;
                                    }
                                    // Clear head target while moving so NPC looks forward
                                    _plugin.AnamcoreManager.ClearHeadTarget(SafeCharacterAddress);
                                    // Smooth rotation BEFORE moving
                                    if (distToTarget > 0.5f)
                                    {
                                        var desiredQuat = CoordinateUtility.LookAt(_currentPosition, targetPosition);
                                        var currentQuat = CoordinateUtility.ToQuaternion(_currentRotation);
                                        var smoothed = Quaternion.Slerp(currentQuat, desiredQuat, Math.Min(10f * delta, 1f));
                                        _currentRotation = smoothed.QuaternionToEuler();
                                    }
                                    // Use ground map Y at the NPC's current XZ instead of player's Y
                                    float groundY = _plugin.AQuestReborn.GroundMap.GetGroundY(
                                        _currentPosition.X, _currentPosition.Z, targetPosition.Y);
                                    // Match player speed categories (Walk: 2.4, Run: 6.0, Sprint: 7.8)
                                    float targetSpeed = 6.0f;
                                    if (_playerSpeedSmoothed > 0.1f && _playerSpeedSmoothed < 4.5f) {
                                        targetSpeed = 2.4f; // Walk
                                    } else if (_playerSpeedSmoothed >= 4.5f) {
                                        targetSpeed = 6.0f; // Run
                                        if (_playerSpeedSmoothed > 7.0f) targetSpeed = 7.8f; // Sprint
                                    } else {
                                        targetSpeed = distToFinalTarget > 5f ? 6.0f : 2.4f; // Player stopped
                                    }
                                    
                                    // Stamina System
                                    if (targetSpeed > 3.0f) {
                                        _stamina = Math.Max(0f, _stamina - (15f * delta)); // Drain when running
                                    } else {
                                        _stamina = Math.Min(100f, _stamina + (25f * delta)); // Recover when walking
                                    }
                                    
                                    // Apply exhaustion penalty if stamina is low
                                    if (_stamina < 30f) {
                                        // Speed smoothly drops to a slow jog as stamina hits 0
                                        float exhaustionFactor = Math.Max(0.7f, _stamina / 30f);
                                        targetSpeed *= exhaustionFactor;
                                        targetSpeed = Math.Max(4.2f, targetSpeed); // Cap minimum to a slow jog (prevents dropping to walk animation)
                                    }
                                    
                                    // Catch up logic
                                    if (distToFinalTarget > 2.0f) {
                                        if (_playerSpeedSmoothed >= 4.5f) {
                                            // Player is running, sprint to catch up
                                            targetSpeed = Math.Max(targetSpeed, _playerSpeedSmoothed) * 1.35f;
                                        } else {
                                            // Player is walking or stopped, catch up gently
                                            targetSpeed = Math.Max(targetSpeed, _playerSpeedSmoothed) * 1.1f;
                                        }
                                    }
                                    if (distToFinalTarget > 6.0f) {
                                        // Panic burst if extremely far behind
                                        targetSpeed = Math.Max(targetSpeed, 7.8f); 
                                    }
                                    
                                    Vector3 currentH = new Vector3(_currentPosition.X, 0, _currentPosition.Z);
                                    Vector3 targetH = new Vector3(targetPosition.X, 0, targetPosition.Z);
                                    
                                    float maxMoveDist = targetSpeed * delta;
                                    Vector3 newH;
                                    if (Vector3.Distance(currentH, targetH) <= maxMoveDist) {
                                        newH = targetH;
                                    } else {
                                        Vector3 dirH = Vector3.Normalize(targetH - currentH);
                                        newH = currentH + (dirH * maxMoveDist);
                                    }

                                    float yLerp = Math.Clamp(10f * delta, 0f, 1f);
                                    float targetY = groundY + (_wasSwimming ? SwimYOffset : 0f);
                                    float yDelta = targetY - _currentPosition.Y;
                                    yDelta = Math.Clamp(yDelta, -MaxYDeltaPerFrame, MaxYDeltaPerFrame);
                                    var newPosition = new Vector3(
                                        newH.X,
                                        _currentPosition.Y + yDelta * yLerp,
                                        newH.Z);
                                        
                                    float speedThisFrame = targetSpeed;
                                    _currentPosition = newPosition;
                                    
                                    _currentScale = Vector3.Lerp(_currentScale, _targetScale, _scaleSpeed * delta);
                                    _wasMoving = true;
                                    TriggerEmoteIfChanged(ContextBasedMovementId(true, speedThisFrame));
                                    if (_horizontalRefreshTimer.ElapsedMilliseconds > 5000)
                                    {
                                        _horizontalOffset = (float)new Random().NextDouble() * -4f;
                                        _horizontalRefreshTimer.Restart();
                                    }
                                }
                                else
                                {
                                    Dalamud.Game.ClientState.Objects.Types.IGameObject activeTarget = _plugin.ObjectTable.LocalPlayer.TargetObject;
                                    if (inCombat && activeTarget == null)
                                    {
                                        float closestDist = 100f; // Limit to 100 yalms
                                        foreach (var obj in _plugin.ObjectTable)
                                        {
                                            if (obj is Dalamud.Game.ClientState.Objects.Types.IBattleNpc bnpc && bnpc.SubKind == 5 && bnpc.CurrentHp > 0)
                                            {
                                                bool isTargetingUs = bnpc.TargetObjectId == _plugin.ObjectTable.LocalPlayer.GameObjectId || bnpc.TargetObjectId == _character.GameObjectId;
                                                
                                                if (!isTargetingUs && bnpc.TargetObjectId != 0 && bnpc.TargetObjectId != 0xE0000000)
                                                {
                                                    if (Vector3.Distance(_plugin.ObjectTable.LocalPlayer.Position, bnpc.Position) < 30f)
                                                    {
                                                        isTargetingUs = true;
                                                    }
                                                }

                                                if (isTargetingUs)
                                                {
                                                    float dist = Vector3.Distance(_currentPosition, bnpc.Position);
                                                    if (dist < closestDist)
                                                    {
                                                        closestDist = dist;
                                                        activeTarget = bnpc;
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    float fallbackY = _plugin.ObjectTable.LocalPlayer.Position.Y;
                                    if (inCombat && activeTarget != null)
                                    {
                                        fallbackY = activeTarget.Position.Y;
                                    }
                                    float groundY = _plugin.AQuestReborn.GroundMap.GetGroundY(
                                        _currentPosition.X, _currentPosition.Z, fallbackY);
                                    float yLerp = Math.Clamp(10f * delta, 0f, 1f);
                                    float targetY = groundY + (_wasSwimming ? SwimYOffset : 0f);
                                    float yDelta = targetY - _currentPosition.Y;
                                    yDelta = Math.Clamp(yDelta, -MaxYDeltaPerFrame, MaxYDeltaPerFrame);
                                    _currentPosition = new Vector3(_currentPosition.X, _currentPosition.Y + yDelta * yLerp, _currentPosition.Z);
                                    _currentScale = Vector3.Lerp(_currentScale, _targetScale, _scaleSpeed * delta);

                                    if (_wasMoving)
                                    {
                                        _wasMoving = false;
                                        if (inCombat)
                                        {
                                            TriggerEmoteIfChanged(34); // Re-apply combat stance
                                        }
                                        else
                                        {
                                            TriggerEmoteIfChanged(ContextBasedMovementId(false));
                                        }
                                    }

                                    if (inCombat)
                                    {
                                        _idleTimer.Restart();
                                        if (!_wasInCombat)
                                        {
                                            _wasInCombat = true;
                                            NotifyCombatStateChanged(true);
                                            CombatTargets.Clear();
                                            
                                            if (!ClassWeaponApplied)
                                            {
                                                ApplyClassWeapon();
                                                ClassWeaponApplied = true;
                                            }

                                            var nChara = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)SafeCharacterAddress;
                                            nChara->DrawData.IsWeaponHidden = false;
                                            nChara->Timeline.TimelineSequencer.PlayTimeline(5616); // Draw weapon
                                            TriggerEmoteIfChanged(34); // Draw Weapon / Combat Stance
                                        }

                                        if (activeTarget != null)
                                        {
                                            LastCombatTarget = activeTarget.Name.TextValue;
                                            if (!string.IsNullOrWhiteSpace(LastCombatTarget))
                                                CombatTargets.Add(LastCombatTarget);

                                            _plugin.AnamcoreManager.SetHeadTarget(SafeCharacterAddress, activeTarget.EntityId);
                                            var tgtPos = activeTarget.Position;

                                            bool isMelee = false;
                                            if (TargetClassJobId > 0)
                                            {
                                                var cj = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>().GetRow(TargetClassJobId);
                                                if (cj.RowId > 0 && (cj.Role == 1 || cj.Role == 2))
                                                {
                                                    isMelee = true;
                                                }
                                            }

                                            if (isMelee)
                                            {
                                                var combatFollowing = _plugin.AQuestReborn.InteractiveNpcDictionary.Values.Where(n => n.IsFollowingPlayer).ToList();
                                                int fCount = Math.Max(1, combatFollowing.Count);
                                                int fIndex = Math.Max(0, combatFollowing.IndexOf(this));
                                                float spreadAngle = (fIndex * (MathF.PI * 2f / fCount));
                                                Vector3 meleeTgtPos = tgtPos + new Vector3(MathF.Cos(spreadAngle) * 2.5f, 0, MathF.Sin(spreadAngle) * 2.5f);

                                                var diff = new Vector3(meleeTgtPos.X - _currentPosition.X, 0, meleeTgtPos.Z - _currentPosition.Z);
                                                float distToTgtXZ = diff.Length();

                                                bool shouldCombatMove = _isCombatMoving;
                                                if (distToTgtXZ > 1.5f) shouldCombatMove = true;
                                                if (distToTgtXZ <= 0.5f) shouldCombatMove = false;

                                                if (shouldCombatMove)
                                                {
                                                    float moveSpeed = _speed * delta * 2.4f; // 12.0 yalms per second (fast run/sprint pace)
                                                    
                                                    if (distToTgtXZ <= moveSpeed || distToTgtXZ == 0)
                                                    {
                                                        _currentPosition.X = meleeTgtPos.X;
                                                        _currentPosition.Z = meleeTgtPos.Z;
                                                    }
                                                    else
                                                    {
                                                        var dir = Vector3.Normalize(diff);
                                                        _currentPosition.X += dir.X * moveSpeed;
                                                        _currentPosition.Z += dir.Z * moveSpeed;
                                                    }

                                                    if (!_isCombatMoving)
                                                    {
                                                        _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, ContextBasedMovementId(true));
                                                        _isCombatMoving = true;
                                                    }
                                                }
                                                else
                                                {
                                                    if (_isCombatMoving)
                                                    {
                                                        _isCombatMoving = false;
                                                        _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, 34); // Resume combat stance
                                                    }
                                                }
                                            }

                                            var desiredQuat = CoordinateUtility.LookAt(_currentPosition, tgtPos);
                                            var currentQuat = CoordinateUtility.ToQuaternion(_currentRotation);
                                            var smoothed = Quaternion.Slerp(currentQuat, desiredQuat, Math.Min(10f * delta, 1f));
                                            _currentRotation = smoothed.QuaternionToEuler();
                                        }
                                        else
                                        {
                                            _plugin.AnamcoreManager.ClearHeadTarget(SafeCharacterAddress);
                                        }

                                        var pChara = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)_plugin.ObjectTable.LocalPlayer.Address;
                                        ushort pTimeline = pChara->Timeline.TimelineSequencer.TimelineIds[1];
                                        
                                        bool shouldAttack = false;
                                        if (pTimeline != 0 && pTimeline != _lastPlayerTimelineId)
                                        {
                                            shouldAttack = true;
                                        }

                                        if (!_autonomousAttackTimer.IsRunning || _autonomousAttackTimer.ElapsedMilliseconds > _nextAutonomousAttackMs)
                                        {
                                            shouldAttack = true;
                                            _autonomousAttackTimer.Restart();
                                            _nextAutonomousAttackMs = new Random(Environment.TickCount + _index).Next(2500, 4500);
                                        }

                                        if (shouldAttack)
                                        {
                                            LoadJobAnimations(_plugin);
                                            
                                            if (TargetClassJobId > 0 && JobCombatAnimations != null && JobCombatAnimations.ContainsKey(TargetClassJobId))
                                            {
                                                var jobAnims = JobCombatAnimations[TargetClassJobId];
                                                if (jobAnims.Count > 0)
                                                {
                                                    _nextCombatAnimationToPlay = jobAnims[new Random(Environment.TickCount + _index).Next(jobAnims.Count)];
                                                }
                                            }
                                            else
                                            {
                                                _nextCombatAnimationToPlay = pTimeline != 0 ? pTimeline : (ushort)0;
                                            }

                                            // Seed random differently for each NPC based on their index
                                            _currentCombatDelayMs = new Random(Environment.TickCount + _index).Next(300, 1500);
                                            _combatAttackDelayTimer.Restart();
                                        }
                                        _lastPlayerTimelineId = pTimeline;

                                        if (_nextCombatAnimationToPlay != 0 && _combatAttackDelayTimer.ElapsedMilliseconds > _currentCombatDelayMs)
                                        {
                                            var nChara = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)SafeCharacterAddress;
                                            _plugin.PluginLog.Information($"Playing timeline {_nextCombatAnimationToPlay} for job {TargetClassJobId}");
                                            nChara->Timeline.TimelineSequencer.PlayTimeline(_nextCombatAnimationToPlay);
                                            _nextCombatAnimationToPlay = 0;
                                        }
                                    }
                                    else
                                    {
                                        if (_wasInCombat)
                                        {
                                            _wasInCombat = false;
                                            _isCombatMoving = false;
                                            NotifyCombatStateChanged(false);
                                            if (VictoryPoseEmoteId > 0)
                                            {
                                                _queuedVictoryPose = VictoryPoseEmoteId;
                                                _victoryPoseDelayTimer.Restart();
                                                _victoryPoseDelayMs = new Random(Environment.TickCount + _index).Next(500, 3000);
                                            }
                                            else
                                            {
                                                _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, ContextBasedMovementId(false));
                                            }
                                            _lastPlayerTimelineId = 0;
                                            _nextCombatAnimationToPlay = 0;
                                        }

                                        if (_queuedVictoryPose > 0 && _victoryPoseDelayTimer.ElapsedMilliseconds > _victoryPoseDelayMs)
                                        {
                                            try
                                            {
                                                var emote = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>().GetRow(_queuedVictoryPose);
                                                TriggerEmoteIfChanged((ushort)emote.ActionTimeline[0].Value.RowId);
                                                _victoryPosePlaying = true;
                                            }
                                            catch { }
                                            _queuedVictoryPose = 0;
                                            _victoryPoseLockTimer.Restart();
                                        }

                                        // Trigger idle emote if standing still long enough
                                        ushort selectedEmoteId = _idleEmoteId;
                                        if (RandomIdleEmotes != null && RandomIdleEmotes.Count > 0)
                                        {
                                            selectedEmoteId = RandomIdleEmotes[new System.Random().Next(RandomIdleEmotes.Count)];
                                        }

                                        if (selectedEmoteId > 0 && !_idleEmotePlaying && _idleTimer.ElapsedMilliseconds > _idleThresholdMs && !Conditions.Instance()->Swimming && !Conditions.Instance()->Diving)
                                        {
                                            try
                                            {
                                                var emote = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>().GetRow(selectedEmoteId);
                                                _activeEmoteTimelineId = (ushort)emote.ActionTimeline[0].Value.RowId;
                                                _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, _activeEmoteTimelineId, true);
                                            }
                                            catch { }
                                            _idleEmotePlaying = true;
                                            NotifyIdleEmoteStarted(selectedEmoteId);
                                        }
                                        // Set head target to player if within range, otherwise look forward
                                        if (_plugin.ObjectTable.LocalPlayer != null
                                            && Vector3.Distance(_currentPosition, _plugin.ObjectTable.LocalPlayer.Position) < 3f)
                                        {
                                            _plugin.AnamcoreManager.SetHeadTarget(SafeCharacterAddress, _plugin.ObjectTable.LocalPlayer.EntityId);
                                        }
                                        else
                                        {
                                            _plugin.AnamcoreManager.ClearHeadTarget(SafeCharacterAddress);
                                        }
                                    }
                                }
                                SetTransform(_currentPosition, _currentRotation, _currentScale);
                            }
                            else
                            {
                                if (!_followPlayer || _plugin.EventWindow.IsOpen || _plugin.ChoiceWindow.IsOpen)
                                {
                                    if (_followPlayer)
                                    {
                                        _defaultPosition = _currentPosition;
                                        _defaultRotation = _currentRotation;
                                    }
                                    if (Vector3.Distance(new Vector3(_currentPosition.X, 0, _currentPosition.Z), new Vector3(_defaultPosition.X, 0, _defaultPosition.Z)) > 0.2)
                                    {
                                        switch (_eventMovementType)
                                        {
                                            case QuestEvent.EventMovementType.Lerp:
                                                _currentPosition = Vector3.Lerp(_currentPosition, _defaultPosition, (_speed / 2) * delta);
                                                break;
                                            case QuestEvent.EventMovementType.FixedTime:
                                                if (!_fixedMovementTimer.IsRunning)
                                                {
                                                    _fixedMovementTimer.Start();
                                                }
                                                _currentPosition = Vector3.Lerp(_lastDefaultPosition, _defaultPosition, Math.Clamp(_fixedMovementTimer.ElapsedMilliseconds / _speed, 0, 1));
                                                break;
                                        }
                                        _currentRotation = CoordinateUtility.LookAt(_currentPosition, _defaultPosition).QuaternionToEuler();
                                        _currentScale = Vector3.Lerp(_currentScale, _targetScale, _scaleSpeed * delta);
                                        if (Vector3.Distance(new Vector3(_currentPosition.X, 0, _currentPosition.Z), new Vector3(_defaultPosition.X, 0, _defaultPosition.Z)) > 0.2f)
                                        {
                                            switch (_eventMovementAnimationType)
                                            {
                                                case EventMovementAnimation.Automatic:
                                                    _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, ContextBasedMovementId(true));
                                                    break;
                                                case EventMovementAnimation.Run:
                                                    _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, 22);
                                                    break;
                                                case EventMovementAnimation.Walk:
                                                    _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, 13);
                                                    break;
                                                case EventMovementAnimation.Swim:
                                                    _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, 4954);
                                                    break;
                                            }
                                            // Break out of idle emote when starting to move
                                            if (_idleEmotePlaying)
                                            {
                                                _plugin.AnamcoreManager.ForceStopEmote(SafeCharacterAddress);
                                                _idleEmotePlaying = false;
                                            }
                                            _idleTimer.Restart();
                                            _wasMoving = true;
                                        }
                                    }
                                    else
                                    {
                                        if (_wasMoving)
                                        {
                                            _wasMoving = false;
                                            _idleEmotePlaying = false;
                                            _idleTimer.Restart();
                                            _idleThresholdMs = 20000 + new Random().Next(20000); // 20-40 seconds
                                            _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, ContextBasedMovementId(false));
                                        }
                                        // Trigger idle emote after threshold
                                        ushort selectedEmoteId = _idleEmoteId;
                                        if (RandomIdleEmotes != null && RandomIdleEmotes.Count > 0)
                                        {
                                            selectedEmoteId = RandomIdleEmotes[new System.Random().Next(RandomIdleEmotes.Count)];
                                        }

                                        if (selectedEmoteId > 0 && !_idleEmotePlaying && _idleTimer.ElapsedMilliseconds > _idleThresholdMs && !Conditions.Instance()->Swimming && !Conditions.Instance()->Diving)
                                        {
                                            try
                                            {
                                                var emote = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>().GetRow(selectedEmoteId);
                                                _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, (ushort)emote.ActionTimeline[0].Value.RowId, true);
                                            }
                                            catch { }
                                            _idleEmotePlaying = true;
                                            NotifyIdleEmoteStarted(selectedEmoteId);
                                        }
                                        if ((_plugin.EventWindow.IsOpen || _plugin.ChoiceWindow.IsOpen) && LooksAtPlayer)
                                        {
                                            _currentPosition = Vector3.Lerp(_currentPosition, _defaultPosition, 5 * delta);
                                            _currentRotation = CoordinateUtility.LookAt(_currentPosition, _plugin.ObjectTable.LocalPlayer.Position).QuaternionToEuler();
                                            _currentScale = Vector3.Lerp(_currentScale, _targetScale, _scaleSpeed * delta);
                                        }
                                        else
                                        {
                                            _currentPosition = Vector3.Lerp(_currentPosition, _defaultPosition, 5 * delta);
                                            _currentRotation = Vector3.Lerp(_currentRotation, _defaultRotation, 1);
                                            _currentScale = Vector3.Lerp(_currentScale, _targetScale, _scaleSpeed * delta);
                                        }
                                        // Head tracking for non-follow NPCs
                                        if (_plugin.ObjectTable.LocalPlayer != null
                                            && Vector3.Distance(_currentPosition, _plugin.ObjectTable.LocalPlayer.Position) < 3f)
                                        {
                                            _plugin.AnamcoreManager.SetHeadTarget(SafeCharacterAddress, _plugin.ObjectTable.LocalPlayer.EntityId);
                                        }
                                        else
                                        {
                                            _plugin.AnamcoreManager.ClearHeadTarget(SafeCharacterAddress);
                                        }
                                    }
                                    SetTransform(_currentPosition, _currentRotation, _currentScale);
                                }
                            }
                        }
                        else
                        {
                            Dispose();
                        }
                    }
                }
                catch (Exception e)
                {
                    _plugin.PluginLog.Warning(e, e.Message);
                }
            }
        }
        public Brio.Core.Transform GetTransform()
        {
            CheckPosing();
            if (_posing != null)
            {
                return _posing.ModelPosing.Transform;
            }
            return new Brio.Core.Transform { Position = new Vector3(), Rotation = new System.Numerics.Quaternion(), Scale = new Vector3(1, 1, 1) };
        }
        public Vector3 GetVerticalOffsetFromPlayer(float offset)
        {
            CheckPosing();
            if (_playerPosing?.ModelPosing == null) return new Vector3(0, 0, 0);
            return _playerPosing.ModelPosing.Transform.Rotation.VectorDirection(new Vector3(1, 0, 0)) * offset;
        }
        public Vector3 GetHorizontalOffsetFromPlayer(float offset)
        {
            CheckPosing();
            if (_playerPosing?.ModelPosing == null) return new Vector3(0, 0, 0);
            return _playerPosing.ModelPosing.Transform.Rotation.VectorDirection(new Vector3(0, 0, 1)) * offset;
        }
        public Vector3 GetVerticalOffset(float offset)
        {
            CheckPosing();
            if (_posing?.ModelPosing == null) return new Vector3(0, 0, 0);
            return _posing.ModelPosing.Transform.Rotation.VectorDirection(new Vector3(1, 0, 0)) * offset;
        }
        public Vector3 GetHorizontalOffset(float offset)
        {
            CheckPosing();
            if (_posing?.ModelPosing == null) return new Vector3(0, 0, 0);
            return _posing.ModelPosing.Transform.Rotation.VectorDirection(new Vector3(0, 0, 1)) * offset;
        }
        public void SetTransform(Vector3 position, Vector3 rotation, Vector3 scale)
        {
            try
            {
                if (_character != null && _character.IsValid() && SafeCharacterAddress != 0)
                {
                    unsafe
                    {
                        var native = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)SafeCharacterAddress;
                        if (_wasSwimming)
                        {
                            // When swimming, only update XZ natively — the game engine fights our Y
                            // due to the swim BaseOverride. Brio handles the actual rendered Y.
                            native->GameObject.SetPosition(position.X, native->GameObject.Position.Y, position.Z);
                        }
                        else
                        {
                            native->GameObject.SetPosition(position.X, position.Y, position.Z);
                        }
                        
                        // FFXIV's native SetRotation expects radians, but our vectors use degrees.
                        float rotationRadians = rotation.Y * (MathF.PI / 180f);
                        native->GameObject.SetRotation(rotationRadians); 
                    }
                }

                if (_plugin.AQuestReborn != null && !_plugin.AQuestReborn.WaitingForMcdfLoad && (AppearanceAccessUtils.AppearanceManager == null || !AppearanceAccessUtils.AppearanceManager.IsWorking()) && _plugin.ObjectTable.LocalPlayer != null)
                {
                    CheckPosing();
                    if (_posing != null)
                    {
                        try
                        {
                            if (_posing.ModelPosing != null)
                            {
                                _posing.ModelPosing.Transform = new Brio.Core.Transform()
                                {
                                    Position = position,
                                    Rotation = CoordinateUtility.ToQuaternion(rotation),
                                    Scale = scale
                                };
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _plugin.PluginLog.Warning(e, e.Message);
            }
        }
        public void CheckPosing()
        {
            if (_posing == null)
            {
                if (_character != null && _character.IsValid())
                {
                    BrioAccessUtils.EntityManager.SetSelectedEntity(_character);
                    BrioAccessUtils.EntityManager.TryGetCapabilityFromSelectedEntity<PosingCapability>(out var posing);
                    _posing = posing;
                }
            }
            if (_playerPosing == null)
            {
                var localPlayer = _plugin.ObjectTable.LocalPlayer;
                if (localPlayer != null)
                {
                    BrioAccessUtils.EntityManager.SetSelectedEntity(localPlayer);
                    BrioAccessUtils.EntityManager.TryGetCapabilityFromSelectedEntity<PosingCapability>(out var posing);
                    _playerPosing = posing;
                }
            }
        }
        public void SetDefaults(Vector3 position, Vector3 rotation, float speed = 5, QuestEvent.EventMovementType eventMovementType = QuestEvent.EventMovementType.Lerp)
        {
            if (!firstPositionSet)
            {
                firstPositionSet = true;
                _lastDefaultPosition = position;
                _lastDefaultRotation = rotation;
            }
            else
            {
                _lastDefaultPosition = _defaultPosition;
                _lastDefaultRotation = _defaultRotation;
            }

            _defaultPosition = position;
            _defaultRotation = rotation;
            _speed = speed;
            _eventMovementType = eventMovementType;
            _fixedMovementTimer.Reset();
            if (!_followPlayer && !_shouldBeMoving)
            {
                _currentPosition = position;
                _currentRotation = rotation;
            }
            _shouldBeMoving = false;
            _plugin.AnamcoreManager.ForceStopEmote(SafeCharacterAddress);
        }

        public Vector3 CurrentPosition => _currentPosition;
        public Vector3 CurrentRotation => _currentRotation;

        /// <summary>
        /// Updates the default position/rotation to the NPC's current actual position.
        /// Used when entering dialogue to prevent NPCs from running back to a stale
        /// default position set by a previous objective/event.
        /// </summary>
        public void SnapDefaultsToCurrent()
        {
            _defaultPosition = _currentPosition;
            _defaultRotation = _currentRotation;
        }

        /// <summary>
        /// Instantly moves the NPC to a position, bypassing follow-state guards.
        /// Use when re-summoning from pool or teleporting.
        /// </summary>
        public void TeleportTo(Vector3 position)
        {
            _currentPosition = position;
            _defaultPosition = position;
            _lastDefaultPosition = position;
            SetTransform(_currentPosition, _currentRotation, _currentScale);
        }
        public void SetDefaultRotation(Vector3 rotation)
        {
            _defaultRotation = rotation;
            _currentRotation = rotation;
        }

        public void WalkToTarget(Vector3 vector3, float speed)
        {
            _shouldBeMoving = true;
            _target = vector3;
            _speed = speed;
        }

        public void FollowPlayer(float speed, bool usePlayerPos = false)
        {
            if (_plugin.ObjectTable.LocalPlayer != null)
            {
                _followPlayer = true;
                _speed = speed;
                // NPC walks from current position — no snap
            }
        }
        public void StopFollowingPlayer()
        {
            _followPlayer = false;
            _defaultPosition = _currentPosition;
            _defaultRotation = _currentRotation;
        }

        public void SetScale(Vector3 scale, float speed)
        {
            _shouldBeScaling = true;
            _targetScale = scale;
            _scaleSpeed = speed;
        }

        /// <summary>
        /// Whether the NPC is currently standing still (not actively walking/following).
        /// </summary>
        public bool IsStationary
        {
            get
            {
                if (_followPlayer && _plugin.ObjectTable.LocalPlayer != null)
                {
                    return !_isFollowMoving;
                }
                return !_shouldBeMoving;
            }
        }

        /// <summary>
        /// Make the NPC begin their idle emote soon (within ~2 seconds).
        /// </summary>
        public void TriggerIdleSoon()
        {
            if (!_idleEmotePlaying && _idleEmoteId > 0)
            {
                _idleThresholdMs = 2000;
                _idleTimer.Restart();
            }
        }

        /// <summary>
        /// Make the NPC react to a player emote by mirroring it.
        /// Faces the player and plays the emote's ActionTimeline.
        /// </summary>
        public void ReactToEmote(ushort emoteId)
        {
            if (_character == null || _disposed) return;
            // Delay 2 seconds for natural feel
            Task.Run(async () =>
            {
                await Task.Delay(2000);
                if (_character == null || _disposed) return;
                try
                {
                    _plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        // Face the player
                        if (_plugin.ObjectTable.LocalPlayer != null)
                        {
                            _currentRotation = CoordinateUtility.LookAt(_currentPosition, _plugin.ObjectTable.LocalPlayer.Position).QuaternionToEuler();
                            SetTransform(_currentPosition, _currentRotation, _currentScale);
                        }

                        // Stop current idle emote
                        if (_idleEmotePlaying)
                        {
                            _plugin.AnamcoreManager.ForceStopEmote(SafeCharacterAddress);
                            _idleEmotePlaying = false;
                        }

                        // Play the emote (timed, not looping)
                        var emote = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>().GetRow(emoteId);
                        var timelineId = (ushort)emote.ActionTimeline[0].Value.RowId;
                        if (timelineId > 0)
                        {
                            _plugin.AnamcoreManager.TriggerEmoteTimed(_character, timelineId, 5000);
                        }

                        // Reset idle timer so the reaction emote plays a while before idle kicks in
                        _idleTimer.Restart();
                        _idleThresholdMs = 20000 + new System.Random().Next(20000);
                    });
                }
                catch { }
            });
        }

        /// <summary>
        /// Inject narrator context when combat starts or ends.
        /// </summary>
        private void NotifyCombatStateChanged(bool entered)
        {
            try
            {
                if (_character == null || _plugin?.AQuestReborn == null) return;

                string npcName = null;
                foreach (var kvp in _plugin.AQuestReborn.InteractiveNpcDictionary)
                {
                    if (kvp.Value == this) { npcName = kvp.Key; break; }
                }
                if (string.IsNullOrEmpty(npcName)) return;

                var convManagers = _plugin.AQuestReborn.CustomNpcConversationManagers;
                if (convManagers == null || !convManagers.ContainsKey(npcName)) return;

                string playerName = _plugin.ObjectTable.LocalPlayer?.Name?.TextValue?.Split(" ")[0] ?? "the adventurer";
                string firstName = npcName.Split(" ")[0];

                // Gather all summoned NPC names
                var allNames = new List<string>();
                foreach (var kvp in _plugin.AQuestReborn.InteractiveNpcDictionary)
                {
                    string name = kvp.Key.Split(" ")[0];
                    if (!allNames.Contains(name)) allNames.Add(name);
                }
                allNames.Add(playerName);
                string partyList = string.Join(", ", allNames);

                string contextLine;
                if (entered)
                {
                    string target = !string.IsNullOrEmpty(LastCombatTarget) ? LastCombatTarget : "an enemy";
                    contextLine = $"[Combat begins — {partyList} draw their weapons to fight {target}]";
                }
                else
                {
                    string enemies = CombatTargets.Count > 0 ? string.Join(", ", CombatTargets) : "the enemies";
                    contextLine = $"[Combat ends — {partyList} sheathe their weapons after defeating {enemies}]";

                    if (CombatTargets.Count > 0)
                    {
                        var speechManager = _plugin.SpeechBubbleManager;
                        if (speechManager != null)
                        {
                            speechManager.NotifyCombatEnded(enemies);
                        }
                    }
                }

                convManagers[npcName].InjectNarratorContext(contextLine);
            }
            catch (Exception e)
            {
                try { _plugin.PluginLog.Debug($"[CombatContext] {e.Message}"); } catch { }
            }
        }

        /// <summary>
        /// Inject narrator context when the NPC begins their idle emote.
        /// This makes the NPC aware of what they're doing when the player next talks to them.
        /// </summary>
        private void NotifyIdleEmoteStarted(ushort emoteRowId)
        {
            try
            {
                if (_character == null || _plugin?.AQuestReborn == null) return;

                // Find this NPC's name from the dictionary
                string npcName = null;
                foreach (var kvp in _plugin.AQuestReborn.InteractiveNpcDictionary)
                {
                    if (kvp.Value == this)
                    {
                        npcName = kvp.Key;
                        break;
                    }
                }
                if (string.IsNullOrEmpty(npcName)) return;

                // Find the conversation manager for this NPC
                var convManagers = _plugin.AQuestReborn.CustomNpcConversationManagers;
                if (convManagers == null || !convManagers.ContainsKey(npcName)) return;

                // Resolve emote name
                string emoteName = "rest";
                try
                {
                    var emote = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>().GetRow(emoteRowId);
                    string name = emote.Name.ToString();
                    if (!string.IsNullOrEmpty(name)) emoteName = name.ToLower();
                }
                catch { }

                // Build the context narration
                string playerName = _plugin.ObjectTable.LocalPlayer?.Name?.TextValue?.Split(" ")[0] ?? "the adventurer";
                string firstName = npcName.Split(" ")[0];
                string contextLine = $"[{firstName} decides to {emoteName} while {playerName} takes a break]";

                convManagers[npcName].InjectNarratorContext(contextLine);
            }
            catch (Exception e)
            {
                try { _plugin.PluginLog.Debug($"[IdleNarration] {e.Message}"); } catch { }
            }
        }

        #region Tail Objective Playback

        // Tail playback state
        private bool _isTailPlayback;
        private TailObjectiveData _tailData;
        private int _tailWaypointIndex;
        private Stopwatch _tailPlaybackTimer = new Stopwatch();
        private bool _tailIsLookingBack;
        private float _tailNextLookBackTime;
        private Stopwatch _tailLookBackTimer = new Stopwatch();
        private Vector3 _tailForwardDirection;
        private bool _tailPathCompleted;
        private Random _tailRng = new Random();
        private ushort _tailLastEmoteId;
        private float _dynamicMaxTailDistance;

        /// <summary>
        /// Fired when the NPC detects the player during a look-back.
        /// </summary>
        public enum TailFailureReason { Spotted, TooClose, TooFar }
        public class TailFailureEventArgs : EventArgs { public TailFailureReason Reason { get; set; } }

        public event EventHandler<TailFailureEventArgs> OnPlayerDetected;

        /// <summary>
        /// Fired when the NPC reaches the end of the recorded path.
        /// </summary>
        public event EventHandler OnTailPathCompleted;

        public bool IsTailPlayback => _isTailPlayback;
        public bool TailPathCompleted => _tailPathCompleted;

        /// <summary>
        /// Start tail objective playback. The NPC will walk the recorded path
        /// and periodically look behind to check for the player.
        /// </summary>
        public void StartTailPlayback(TailObjectiveData tailData)
        {
            if (tailData == null || tailData.Waypoints.Count < 2) return;

            _tailData = tailData;
            _isTailPlayback = true;
            _tailPathCompleted = false;
            _tailWaypointIndex = 0;
            _tailIsLookingBack = false;
            _tailLastEmoteId = 0;

            // Position NPC at the first waypoint
            var firstWp = tailData.Waypoints[0];
            _currentPosition = firstWp.Position;
            _currentRotation = firstWp.Rotation;
            _defaultPosition = firstWp.Position;
            _defaultRotation = firstWp.Rotation;

            // Schedule first look-back
            _tailNextLookBackTime = _tailRng.Next(
                (int)(_tailData.LookBackMinInterval * 1000),
                (int)(_tailData.LookBackMaxInterval * 1000));

            // Calculate dynamic max distance based on initial player position
            if (_plugin.ObjectTable.LocalPlayer != null)
            {
                float initialDistance = Vector3.Distance(_currentPosition, _plugin.ObjectTable.LocalPlayer.Position);
                _dynamicMaxTailDistance = Math.Max(initialDistance * 1.1f, _tailData.MaximumTailDistance);
            }
            else
            {
                _dynamicMaxTailDistance = _tailData.MaximumTailDistance;
            }

            _tailPlaybackTimer.Restart();
            _tailLookBackTimer.Reset();
        }

        /// <summary>
        /// Stop tail playback and reset state.
        /// </summary>
        public void StopTailPlayback(bool updateDefaultPosition = false)
        {
            _isTailPlayback = false;
            _tailPlaybackTimer.Stop();
            _tailLookBackTimer.Stop();
            _tailIsLookingBack = false;

            if (updateDefaultPosition)
            {
                _defaultPosition = _currentPosition;
                _defaultRotation = _currentRotation;
            }
        }

        /// <summary>
        /// Pauses the tail playback and shows a failure reaction.
        /// Call ResetTailToStart() a few seconds after this to actually restart the objective.
        /// </summary>
        public void ShowTailFailure(TailFailureReason reason)
        {
            if (!_isTailPlayback) return;
            
            _tailPlaybackTimer.Stop();
            _tailLookBackTimer.Stop();

            // Play caught blurb if they spotted the player or the player got too close
            if ((reason == TailFailureReason.Spotted || reason == TailFailureReason.TooClose) 
                && _tailData != null && _tailData.CaughtBlurbs != null && _tailData.CaughtBlurbs.Count > 0)
            {
                _tailIsFailed = true;
                string blurb = _tailData.CaughtBlurbs[_tailRng.Next(_tailData.CaughtBlurbs.Count)];
                _plugin.SpeechBubbleManager?.ShowBubble(_character, _tailData.NpcName, blurb);

                // Face the player
                if (_plugin.ObjectTable.LocalPlayer != null)
                {
                    var desiredQuat = CoordinateUtility.LookAt(_currentPosition, _plugin.ObjectTable.LocalPlayer.Position);
                    _tailFailedTargetRotation = desiredQuat.QuaternionToEuler();
                }
            }
        }

        private bool _tailIsFailed = false;
        private Vector3 _tailFailedTargetRotation = Vector3.Zero;

        /// <summary>
        /// Reset the NPC to the start of the tail path (after player detection / fail).
        /// </summary>
        public void ResetTailToStart()
        {
            if (_tailData == null || _tailData.Waypoints.Count == 0) return;

            _tailWaypointIndex = 0;
            _tailPathCompleted = false;
            _tailIsLookingBack = false;
            _tailIsFailed = false;
            _tailLastEmoteId = 0;

            var firstWp = _tailData.Waypoints[0];
            _currentPosition = firstWp.Position;
            _currentRotation = firstWp.Rotation;
            _defaultPosition = firstWp.Position;
            _defaultRotation = firstWp.Rotation;

            _tailNextLookBackTime = _tailRng.Next(
                (int)(_tailData.LookBackMinInterval * 1000),
                (int)(_tailData.LookBackMaxInterval * 1000));

            _tailPlaybackTimer.Restart();
            _tailLookBackTimer.Reset();
        }

        /// <summary>
        /// Called each frame from Framework_Update when _isTailPlayback is true.
        /// </summary>
        private void UpdateTailPlayback(float delta)
        {
            if (_tailData == null || _tailPathCompleted) return;

            if (_tailIsFailed)
            {
                // Smoothly rotate to face the player after failing
                var desiredQuat = CoordinateUtility.ToQuaternion(_tailFailedTargetRotation);
                var currentQuat = CoordinateUtility.ToQuaternion(_currentRotation);
                var smoothed = Quaternion.Slerp(currentQuat, desiredQuat, Math.Min(5f * delta, 1f));
                _currentRotation = smoothed.QuaternionToEuler();
                
                _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, ContextBasedMovementId(false));
                SetTransform(_currentPosition, _currentRotation, _currentScale);
                return;
            }

            float elapsed = (float)_tailPlaybackTimer.Elapsed.TotalSeconds;

            // --- Check fail conditions (too close or too far) ---
            if (_plugin.ObjectTable.LocalPlayer != null)
            {
                var playerPos = _plugin.ObjectTable.LocalPlayer.Position;
                float distanceToPlayer = Vector3.Distance(_currentPosition, playerPos);
                
                if (distanceToPlayer < _tailData.MinimumTailDistance)
                {
                    OnPlayerDetected?.Invoke(this, new TailFailureEventArgs { Reason = TailFailureReason.TooClose });
                    return;
                }
                else if (distanceToPlayer > _dynamicMaxTailDistance)
                {
                    OnPlayerDetected?.Invoke(this, new TailFailureEventArgs { Reason = TailFailureReason.TooFar });
                    return;
                }
            }

            // --- Look-back interjection ---
            if (_tailIsLookingBack)
            {
                float lookElapsed = (float)_tailLookBackTimer.Elapsed.TotalSeconds;
                if (lookElapsed >= _tailData.LookBackDuration)
                {
                    // Finished looking back — resume path
                    _tailIsLookingBack = false;
                    _tailLookBackTimer.Reset();
                    _tailPlaybackTimer.Start(); // Resume path timer

                    // Schedule next look-back
                    _tailNextLookBackTime = (float)_tailPlaybackTimer.Elapsed.TotalMilliseconds +
                        _tailRng.Next(
                            (int)(_tailData.LookBackMinInterval * 1000),
                            (int)(_tailData.LookBackMaxInterval * 1000));
                }
                else
                {
                    // Currently looking back — check if player is detected in cone
                    if (_plugin.ObjectTable.LocalPlayer != null)
                    {
                        var playerPos = _plugin.ObjectTable.LocalPlayer.Position;
                        if (IsPlayerInDetectionCone(_currentPosition, _tailForwardDirection, playerPos,
                            _tailData.DetectionRadius, _tailData.DetectionConeAngle))
                        {
                            OnPlayerDetected?.Invoke(this, new TailFailureEventArgs { Reason = TailFailureReason.Spotted });
                            return;
                        }
                    }

                    // Smoothly rotate to look behind
                    var desiredQuat = CoordinateUtility.ToQuaternion(CoordinateUtility.DirectionToEuler(_tailForwardDirection));
                    var currentQuat = CoordinateUtility.ToQuaternion(_currentRotation);
                    var smoothed = Quaternion.Slerp(currentQuat, desiredQuat, Math.Min(5f * delta, 1f));
                    _currentRotation = smoothed.QuaternionToEuler();
                }

                // Play idle animation while looking
                _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, ContextBasedMovementId(false));
                SetTransform(_currentPosition, _currentRotation, _currentScale);
                return;
            }

            // --- Check if it's time for a look-back ---
            if (_tailPlaybackTimer.ElapsedMilliseconds >= _tailNextLookBackTime)
            {
                _tailIsLookingBack = true;
                _tailPlaybackTimer.Stop(); // Pause path advancement
                _tailLookBackTimer.Restart();

                // Set the target backward direction, but don't snap rotation immediately
                var currentFacing = CoordinateUtility.EulerToDirection(_currentRotation);
                _tailForwardDirection = -currentFacing; 

                if (_tailData.LookAroundBlurbs != null && _tailData.LookAroundBlurbs.Count > 0)
                {
                    string blurb = _tailData.LookAroundBlurbs[_tailRng.Next(_tailData.LookAroundBlurbs.Count)];
                    _plugin.SpeechBubbleManager?.ShowBubble(_character, _tailData.NpcName, blurb);
                }

                // Play idle/look animation
                _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, ContextBasedMovementId(false));
                SetTransform(_currentPosition, _currentRotation, _currentScale);
                return;
            }

            // --- Normal path playback ---
            if (_tailWaypointIndex >= _tailData.Waypoints.Count - 1)
            {
                // Reached end of path
                _tailPathCompleted = true;
                _tailPlaybackTimer.Stop();
                _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, ContextBasedMovementId(false));
                SetTransform(_currentPosition, _currentRotation, _currentScale);
                OnTailPathCompleted?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Find the correct waypoint pair based on elapsed time
            while (_tailWaypointIndex < _tailData.Waypoints.Count - 1 &&
                   _tailData.Waypoints[_tailWaypointIndex + 1].Timestamp <= elapsed)
            {
                _tailWaypointIndex++;
            }

            if (_tailWaypointIndex >= _tailData.Waypoints.Count - 1)
            {
                _tailPathCompleted = true;
                _tailPlaybackTimer.Stop();
                _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, ContextBasedMovementId(false));
                SetTransform(_currentPosition, _currentRotation, _currentScale);
                OnTailPathCompleted?.Invoke(this, EventArgs.Empty);
                return;
            }

            var wpCurrent = _tailData.Waypoints[_tailWaypointIndex];
            var wpNext = _tailData.Waypoints[_tailWaypointIndex + 1];

            // Lerp between waypoints
            float segmentDuration = wpNext.Timestamp - wpCurrent.Timestamp;
            float segmentElapsed = elapsed - wpCurrent.Timestamp;
            float t = segmentDuration > 0 ? Math.Clamp(segmentElapsed / segmentDuration, 0f, 1f) : 1f;

            _currentPosition = Vector3.Lerp(wpCurrent.Position, wpNext.Position, t);
            
            // Smoothly interpolate rotation to prevent snapping when turning back from a look-back
            var targetRotation = Vector3.Lerp(wpCurrent.Rotation, wpNext.Rotation, t);
            var pathTargetQuat = CoordinateUtility.ToQuaternion(targetRotation);
            var pathCurrentQuat = CoordinateUtility.ToQuaternion(_currentRotation);
            var pathSmoothed = Quaternion.Slerp(pathCurrentQuat, pathTargetQuat, Math.Min(10f * delta, 1f));
            _currentRotation = pathSmoothed.QuaternionToEuler();

            // Determine speed between waypoints for animation
            float waypointDistance = Vector3.Distance(wpCurrent.Position, wpNext.Position);
            float waypointSpeed = segmentDuration > 0 ? waypointDistance / segmentDuration : 0;

            if (waypointSpeed > 0.1f)
            {
                // Moving — play walk or run animation
                if (waypointSpeed > 3.5f)
                    _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, 22); // Run
                else
                    _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, 13); // Walk
            }
            else
            {
                // Paused/idle
                _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, ContextBasedMovementId(false));
            }

            // Trigger recorded emotes
            if (wpCurrent.EmoteId > 0 && wpCurrent.EmoteId != _tailLastEmoteId)
            {
                try
                {
                    var emote = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>().GetRow(wpCurrent.EmoteId);
                    _plugin.AnamcoreManager.TriggerEmote(SafeCharacterAddress, (ushort)emote.ActionTimeline[0].Value.RowId);
                    _tailLastEmoteId = wpCurrent.EmoteId;
                }
                catch { }
            }

            SetTransform(_currentPosition, _currentRotation, _currentScale);
        }

        /// <summary>
        /// Check if the player is within the NPC's detection cone.
        /// </summary>
        public static bool IsPlayerInDetectionCone(Vector3 npcPosition, Vector3 lookDirection,
            Vector3 playerPosition, float detectionRadius, float coneAngleDeg)
        {
            // Distance check
            float distance = Vector3.Distance(npcPosition, playerPosition);
            if (distance > detectionRadius) return false;

            // Cone check — angle between look direction and direction to player
            var toPlayer = Vector3.Normalize(playerPosition - npcPosition);
            var lookDir = Vector3.Normalize(lookDirection);

            // Use only XZ plane (horizontal)
            var toPlayerXZ = Vector3.Normalize(new Vector3(toPlayer.X, 0, toPlayer.Z));
            var lookDirXZ = Vector3.Normalize(new Vector3(lookDir.X, 0, lookDir.Z));

            float dot = Vector3.Dot(lookDirXZ, toPlayerXZ);
            float angleDeg = (float)(Math.Acos(Math.Clamp(dot, -1f, 1f)) * (180.0 / Math.PI));

            return angleDeg <= (coneAngleDeg / 2f);
        }

        #endregion

        public void Dispose()
        {
            StopTailPlayback();
            _disposed = true;
            _plugin.Framework.Update -= Framework_Update;
            _plugin.ClientState.TerritoryChanged -= ClientState_TerritoryChanged;
            _character = null;
        }
    }
}
