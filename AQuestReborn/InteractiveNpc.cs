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

namespace AQuestReborn
{
    public class InteractiveNpc : IDisposable
    {
        private ICharacter _character;
        private Plugin _plugin;
        private bool _shouldBeMoving;
        private Vector3 _target;
        private float _speed = 10;
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
        private bool _wasMoving;

        public string LastAppearance { get; internal set; }
        public bool LooksAtPlayer { get; internal set; }
        public bool ShouldBeMoving { get => _shouldBeMoving; set => _shouldBeMoving = value; }

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
        }

        private void ClientState_TerritoryChanged(ushort obj)
        {
            Dispose();
        }

        private unsafe void Framework_Update(IFramework framework)
        {
            try
            {
                if (!_plugin.AQuestReborn.WaitingForMcdfLoad && !AppearanceAccessUtils.AppearanceManager.IsWorking() && _plugin.ObjectTable.LocalPlayer != null)
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
                            if (Vector3.Distance(_currentPosition, targetPosition) > 1)
                            {
                                _currentPosition = Vector3.Lerp(_currentPosition, targetPosition, _speed * delta);
                                _currentRotation = CoordinateUtility.LookAt(_currentPosition, targetPosition).QuaternionToEuler();
                                _currentScale = Vector3.Lerp(_currentScale, _targetScale, _scaleSpeed * delta);
                                var value = _plugin.AnamcoreManager.GetCurrentAnimationId(_plugin.ObjectTable.LocalPlayer);
                                _plugin.AnamcoreManager.TriggerEmote(_character.Address, 22);
                                if (_horizontalRefreshTimer.ElapsedMilliseconds > 5000)
                                {
                                    _horizontalOffset = (float)new Random().NextDouble() * -4f;
                                    _horizontalRefreshTimer.Restart();
                                }
                            }
                            else
                            {
                                _currentPosition = Vector3.Lerp(_currentPosition, new Vector3(_currentPosition.X, _plugin.ObjectTable.LocalPlayer.Position.Y, _currentPosition.Z), _speed * delta);
                                _currentScale = Vector3.Lerp(_currentScale, _targetScale, _scaleSpeed * delta);
                                _plugin.AnamcoreManager.StopEmote(_character.Address);
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

                                    SetTransform(_currentPosition, _currentRotation, _currentScale);
                                    if (Vector3.Distance(_currentPosition, _plugin.ObjectTable.LocalPlayer.Position) > 0.2f)
                                    {
                                        _plugin.AnamcoreManager.TriggerEmote(_character.Address, 22);
                                        _wasMoving = true;
                                    }
                                }
                                else
                                {
                                    if (_wasMoving)
                                    {
                                        _wasMoving = false;
                                        _plugin.AnamcoreManager.StopEmote(_character.Address);
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
                                    SetTransform(_currentPosition, _currentRotation, _currentScale);
                                }
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
                if (!_plugin.AQuestReborn.WaitingForMcdfLoad && !AppearanceAccessUtils.AppearanceManager.IsWorking() && _plugin.ObjectTable.LocalPlayer != null)
                {
                    CheckPosing();
                    if (_posing != null)
                    {
                        _posing.ModelPosing.Transform = new Brio.Core.Transform()
                        {
                            Position = position,
                            Rotation = CoordinateUtility.ToQuaternion(rotation),
                            Scale = scale
                        };
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
        public void SetDefaults(Vector3 position, Vector3 rotation, float speed = 10, QuestEvent.EventMovementType eventMovementType = QuestEvent.EventMovementType.Lerp)
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
            _plugin.AnamcoreManager.StopEmote(_character.Address);
        }

        public void SetDefaultRotation(Vector3 rotation)
        {
            _defaultRotation = rotation;
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
        public void Dispose()
        {
            _disposed = true;
            _plugin.Framework.Update -= Framework_Update;
            _plugin.ClientState.TerritoryChanged -= ClientState_TerritoryChanged;
            _character = null;
        }
    }
}
