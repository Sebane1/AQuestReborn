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
        private ICharacter _character;
        private Plugin _plugin;
        private bool _shouldBeMoving;
        private Vector3 _target;
        private float _speed = 5;
        private QuestEvent.EventMovementType _eventMovementType;
        private bool _shouldBeScaling;
        private Vector3 _targetScale = new Vector3(1, 1, 1);
        private float _scaleSpeed = 10;
        private bool _followPlayer;
        private Vector3 _currentPosition;
        private Vector3 _followStart;
        private Vector3 _defaultPosition;
        private Vector3 _defaultRotation;
        private Vector3 _currentRotation;
        private bool _disposed;
        private Vector3 _currentScale;
        private PosingCapability? _posing;
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
        private ushort _idleEmoteId;
        private bool _wasMoving;
        private bool _isFollowMoving;
        private ushort _activeEmoteTimelineId;
        private bool _waitingForEmoteExit;
        EventMovementAnimation _eventMovementAnimationType = EventMovementAnimation.Automatic;
        public string LastAppearance { get; internal set; }
        public bool LooksAtPlayer { get; internal set; }
        public bool ShouldBeMoving { get => _shouldBeMoving; set => _shouldBeMoving = value; }
        public ICharacter Character { get => _character; set => _character = value; }
        public EventMovementAnimation EventMovementAnimationType { get => _eventMovementAnimationType; set => _eventMovementAnimationType = value; }
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

        public InteractiveNpc(Plugin plugin, ICharacter character)
        {
            _character = character;
            _plugin = plugin;
            _plugin.Framework.Update += Framework_Update;
            _plugin.ClientState.TerritoryChanged += ClientState_TerritoryChanged;
            BrioAccessUtils.EntityManager.SetSelectedEntity(_character);
            BrioAccessUtils.EntityManager.TryGetCapabilityFromSelectedEntity<PosingCapability>(out var posing);
            _posing = posing;
            _index = _plugin.AQuestReborn.InteractiveNpcDictionary.Count;
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

        public unsafe uint ContextBasedMovementId(bool isMoving)
        {
            if (Conditions.Instance()->Swimming || Conditions.Instance()->Diving)
            {
                return isMoving ? 4954u : 4947u;
            }
            else
            {
                return isMoving ? 22u : 0u;
            }
        }
        public unsafe void Framework_Update(IFramework framework)
        {
            if (!_disposed)
            {
                try
                {
                    if (_plugin.AQuestReborn != null && !_plugin.AQuestReborn.WaitingForMcdfLoad && (AppearanceAccessUtils.AppearanceManager == null || !AppearanceAccessUtils.AppearanceManager.IsWorking()) && _plugin.ObjectTable.LocalPlayer != null)
                    {
                        if (_character != null)
                        {
                            float delta = ((float)_plugin.Framework.UpdateDelta.Milliseconds / 1000f);
                            if (_followPlayer && !_plugin.EventWindow.IsOpen && !_plugin.ChoiceWindow.IsOpen
                                && _plugin.EventWindow.TimeSinceLastDialogueDisplayed.ElapsedMilliseconds > 200
                                && _plugin.ChoiceWindow.TimeSinceLastChoiceMade.ElapsedMilliseconds > 200 && !Conditions.Instance()->Mounted)
                            {
                                var targetPosition = _plugin.ObjectTable.LocalPlayer.Position
                                        + GetVerticalOffsetFromPlayer((_index) - ((float)(_plugin.AQuestReborn.InteractiveNpcDictionary.Count - 1) / 2f))
                                        + GetHorizontalOffsetFromPlayer(_horizontalOffset);
                                float distToTarget = Vector3.Distance(_currentPosition, targetPosition);
                                // Check if player is facing the NPC (within ~45° cone)
                                bool playerFacingNpc = false;
                                if (distToTarget > 0.5f)
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
                                // Hysteresis: start moving at 4y, keep moving until within 2y
                                // Freeze when player is directly facing the NPC
                                if (!playerFacingNpc && distToTarget > 4) _isFollowMoving = true;
                                if (distToTarget <= 2 || playerFacingNpc) _isFollowMoving = false;
                                if (_isFollowMoving)
                                {
                                    // Always reset idle timer while moving
                                    _idleTimer.Restart();
                                    // Clear emote state - give StopEmote one frame to process
                                    if (_idleEmotePlaying)
                                    {
                                        _plugin.AnamcoreManager.ForceStopEmote(_character.Address);
                                        _idleEmotePlaying = false;
                                        SetTransform(_currentPosition, _currentRotation, _currentScale);
                                        return;
                                    }
                                    // Clear head target while moving so NPC looks forward
                                    _plugin.AnamcoreManager.ClearHeadTarget(_character.Address);
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
                                    // Lerp XZ at normal speed, Y at 10x for quick ground snapping
                                    float xzLerp = _speed * delta;
                                    float yLerp = Math.Clamp(_speed * delta * 10f, 0f, 1f);
                                    _currentPosition = new Vector3(
                                        _currentPosition.X + (targetPosition.X - _currentPosition.X) * xzLerp,
                                        _currentPosition.Y + (groundY - _currentPosition.Y) * yLerp,
                                        _currentPosition.Z + (targetPosition.Z - _currentPosition.Z) * xzLerp);
                                    _currentScale = Vector3.Lerp(_currentScale, _targetScale, _scaleSpeed * delta);
                                    _plugin.AnamcoreManager.TriggerEmote(_character.Address, ContextBasedMovementId(true));
                                    if (_horizontalRefreshTimer.ElapsedMilliseconds > 5000)
                                    {
                                        _horizontalOffset = (float)new Random().NextDouble() * -4f;
                                        _horizontalRefreshTimer.Restart();
                                    }
                                }
                                else
                                {
                                    float groundY = _plugin.AQuestReborn.GroundMap.GetGroundY(
                                        _currentPosition.X, _currentPosition.Z, _plugin.ObjectTable.LocalPlayer.Position.Y);
                                    float yLerp = Math.Clamp(_speed * delta * 10f, 0f, 1f);
                                    _currentPosition = new Vector3(_currentPosition.X, _currentPosition.Y + (groundY - _currentPosition.Y) * yLerp, _currentPosition.Z);
                                    _currentScale = Vector3.Lerp(_currentScale, _targetScale, _scaleSpeed * delta);
                                    // Trigger idle emote if standing still long enough
                                    if (_idleEmoteId > 0 && !_idleEmotePlaying && _idleTimer.ElapsedMilliseconds > _idleThresholdMs)
                                    {
                                        try
                                        {
                                            var emote = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>().GetRow(_idleEmoteId);
                                            _activeEmoteTimelineId = (ushort)emote.ActionTimeline[0].Value.RowId;
                                            _plugin.AnamcoreManager.TriggerEmote(_character.Address, _activeEmoteTimelineId);
                                        }
                                        catch { }
                                        _idleEmotePlaying = true;
                                    }
                                    else if (!_idleEmotePlaying)
                                    {
                                        _plugin.AnamcoreManager.TriggerEmote(_character.Address, ContextBasedMovementId(false));
                                    }
                                    // Set head target to player if within range, otherwise look forward
                                    if (_plugin.ObjectTable.LocalPlayer != null
                                        && Vector3.Distance(_currentPosition, _plugin.ObjectTable.LocalPlayer.Position) < 3f)
                                    {
                                        _plugin.AnamcoreManager.SetHeadTarget(_character.Address, _plugin.ObjectTable.LocalPlayer.EntityId);
                                    }
                                    else
                                    {
                                        _plugin.AnamcoreManager.ClearHeadTarget(_character.Address);
                                    }
                                }
                                SetTransform(_currentPosition, _currentRotation, _currentScale);
                            }
                            else
                            {
                                if (!_followPlayer || _plugin.EventWindow.IsOpen || _plugin.ChoiceWindow.IsOpen)
                                {
                                    if (Vector3.Distance(new Vector3(_currentPosition.X, 0, _currentPosition.X), new Vector3(_defaultPosition.X, 0, _defaultPosition.X)) > 0.2)
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
                                        _currentRotation = _currentRotation = CoordinateUtility.LookAt(_currentPosition, _defaultPosition).QuaternionToEuler();
                                        _currentScale = Vector3.Lerp(_currentScale, _targetScale, _scaleSpeed * delta);
                                        if (Vector3.Distance(_currentPosition, _plugin.ObjectTable.LocalPlayer.Position) > 0.2f)
                                        {
                                            switch (_eventMovementAnimationType)
                                            {
                                                case EventMovementAnimation.Automatic:
                                                    _plugin.AnamcoreManager.TriggerEmote(_character.Address, ContextBasedMovementId(true));
                                                    break;
                                                case EventMovementAnimation.Run:
                                                    _plugin.AnamcoreManager.TriggerEmote(_character.Address, 22);
                                                    break;
                                                case EventMovementAnimation.Walk:
                                                    _plugin.AnamcoreManager.TriggerEmote(_character.Address, 13);
                                                    break;
                                                case EventMovementAnimation.Swim:
                                                    _plugin.AnamcoreManager.TriggerEmote(_character.Address, 4954);
                                                    break;
                                            }
                                            // Break out of idle emote when starting to move
                                            if (_idleEmotePlaying)
                                            {
                                                _plugin.AnamcoreManager.ForceStopEmote(_character.Address);
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
                                            _plugin.AnamcoreManager.TriggerEmote(_character.Address, ContextBasedMovementId(false));
                                        }
                                        // Trigger idle emote after threshold
                                        if (_idleEmoteId > 0 && !_idleEmotePlaying && _idleTimer.ElapsedMilliseconds > _idleThresholdMs)
                                        {
                                            try
                                            {
                                                var emote = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>().GetRow(_idleEmoteId);
                                                _plugin.AnamcoreManager.TriggerEmote(_character.Address, (ushort)emote.ActionTimeline[0].Value.RowId);
                                            }
                                            catch { }
                                            _idleEmotePlaying = true;
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
                                            _plugin.AnamcoreManager.SetHeadTarget(_character.Address, _plugin.ObjectTable.LocalPlayer.EntityId);
                                        }
                                        else
                                        {
                                            _plugin.AnamcoreManager.ClearHeadTarget(_character.Address);
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
            return _playerPosing.ModelPosing.Transform.Rotation.VectorDirection(new Vector3(1, 0, 0)) * offset;
        }
        public Vector3 GetHorizontalOffsetFromPlayer(float offset)
        {
            CheckPosing();
            return _playerPosing.ModelPosing.Transform.Rotation.VectorDirection(new Vector3(0, 0, 1)) * offset;
        }
        public Vector3 GetVerticalOffset(float offset)
        {
            CheckPosing();
            return _posing.ModelPosing.Transform.Rotation.VectorDirection(new Vector3(1, 0, 0)) * offset;
        }
        public Vector3 GetHorizontalOffset(float offset)
        {
            CheckPosing();
            return _posing.ModelPosing.Transform.Rotation.VectorDirection(new Vector3(0, 0, 1)) * offset;
        }
        public void SetTransform(Vector3 position, Vector3 rotation, Vector3 scale)
        {
            try
            {
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
                BrioAccessUtils.EntityManager.SetSelectedEntity(_character);
                BrioAccessUtils.EntityManager.TryGetCapabilityFromSelectedEntity<PosingCapability>(out var posing);
                _posing = posing;
            }
            if (_playerPosing == null)
            {
                BrioAccessUtils.EntityManager.SetSelectedEntity(_plugin.ObjectTable.LocalPlayer);
                BrioAccessUtils.EntityManager.TryGetCapabilityFromSelectedEntity<PosingCapability>(out var posing);
                _playerPosing = posing;
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
            _plugin.AnamcoreManager.ForceStopEmote(_character.Address);
        }

        public Vector3 CurrentPosition => _currentPosition;
        public Vector3 CurrentRotation => _currentRotation;
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
                if (usePlayerPos)
                {
                    _currentPosition = _plugin.ObjectTable.LocalPlayer.Position;
                }
            }
        }
        public void StopFollowingPlayer()
        {
            _followPlayer = false;
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
                    return Vector3.Distance(_currentPosition, _plugin.ObjectTable.LocalPlayer.Position) <= 1;
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
            try
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
                    _plugin.AnamcoreManager.ForceStopEmote(_character.Address);
                    _idleEmotePlaying = false;
                }

                // Play the emote
                var emote = _plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>().GetRow(emoteId);
                var timelineId = (ushort)emote.ActionTimeline[0].Value.RowId;
                if (timelineId > 0)
                {
                    _plugin.AnamcoreManager.TriggerEmote(_character.Address, timelineId);
                }

                // Reset idle timer so the reaction emote plays a while before idle kicks in
                _idleTimer.Restart();
                _idleThresholdMs = 20000 + new System.Random().Next(20000);
            }
            catch { }
        }

        public void Dispose()
        {
            _disposed = true;
            _plugin.Framework.Update -= Framework_Update;
            _plugin.ClientState.TerritoryChanged -= ClientState_TerritoryChanged;
            _character = null;
        }
    }
}
