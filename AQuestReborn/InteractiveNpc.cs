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

namespace AQuestReborn
{
    public class InteractiveNpc : IDisposable
    {
        private ICharacter _character;
        private Plugin _plugin;
        private bool _shouldBeMoving;
        private Vector3 _target;
        private float _speed = 10;
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
        private bool _lock;

        public string LastMcdf { get; internal set; }

        public InteractiveNpc(Plugin plugin, ICharacter character)
        {
            _character = character;
            _plugin = plugin;
            _plugin.Framework.Update += Framework_Update;
            _plugin.ClientState.TerritoryChanged += ClientState_TerritoryChanged;
            BrioAccessUtils.EntityManager.SetSelectedEntity(_character);
            BrioAccessUtils.EntityManager.TryGetCapabilityFromSelectedEntity<PosingCapability>(out var posing);
            _posing = posing;
        }

        private void ClientState_TerritoryChanged(ushort obj)
        {
            Dispose();
        }

        private void Framework_Update(IFramework framework)
        {
            try
            {
                if (!_plugin.AQuestReborn.WaitingForMcdfLoad && !McdfAccessUtils.McdfManager.IsWorking() && _plugin.ClientState.LocalPlayer != null && !_lock)
                {
                    if (_character != null)
                    {
                        float delta = ((float)_plugin.Framework.UpdateDelta.Milliseconds / 1000f);
                        if (_followPlayer && !_plugin.DialogueWindow.IsOpen && !_plugin.ChoiceWindow.IsOpen
                            && _plugin.DialogueWindow.TimeSinceLastDialogueDisplayed.ElapsedMilliseconds > 200
                            && _plugin.ChoiceWindow.TimeSinceLastChoiceMade.ElapsedMilliseconds > 200)
                        {
                            if (Vector3.Distance(_currentPosition, _plugin.ClientState.LocalPlayer.Position) > 1)
                            {
                                SetTransform(_currentPosition = Vector3.Lerp(_currentPosition, _plugin.ClientState.LocalPlayer.Position, _speed * delta),
                                    _currentRotation = new Vector3(0, CoordinateUtility.ConvertRadiansToDegrees(_plugin.ClientState.LocalPlayer.Rotation), 0),
                                    _currentScale = Vector3.Lerp(_currentScale, _targetScale, _scaleSpeed * delta));
                                var value = _plugin.AnamcoreManager.GetCurrentAnimationId(_plugin.ClientState.LocalPlayer);
                                _plugin.AnamcoreManager.TriggerEmote(_character.Address, 22);
                            }
                            else
                            {
                                SetTransform(_currentPosition = Vector3.Lerp(_currentPosition,
                                new Vector3(_currentPosition.X, _plugin.ClientState.LocalPlayer.Position.Y, _currentPosition.Z), _speed * delta),
                                _currentRotation, _currentScale = Vector3.Lerp(_currentScale, _targetScale, _scaleSpeed * delta));
                                _plugin.AnamcoreManager.StopEmote(_character.Address);
                            }
                        }
                        else
                        {
                            if (!_lock)
                            {
                                if (_shouldBeMoving)
                                {
                                    SetTransform(_currentPosition = Vector3.Lerp(_currentPosition, _target, _speed * delta),
                                                 _currentRotation = Vector3.Lerp(_currentRotation, _defaultRotation, 1),
                                                 _currentScale = Vector3.Lerp(_currentScale, _targetScale, _scaleSpeed * delta));
                                    if (Vector3.Distance(_currentPosition, _plugin.ClientState.LocalPlayer.Position) > 0.2f)
                                    {
                                        _plugin.AnamcoreManager.TriggerEmote(_character.Address, 22);
                                    }
                                    else
                                    {
                                        _plugin.AnamcoreManager.StopEmote(_character.Address);
                                    }
                                }
                                else
                                {
                                    SetTransform(_currentPosition = Vector3.Lerp(_currentPosition, _defaultPosition, 5 * delta),
                                                 _currentRotation = Vector3.Lerp(_currentRotation, _defaultRotation, 1),
                                                 _currentScale = Vector3.Lerp(_currentScale, _targetScale, _scaleSpeed * delta));
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
        public void SetTransform(Vector3 position, Vector3 rotation, Vector3 scale)
        {
            if (!_plugin.AQuestReborn.WaitingForMcdfLoad && !McdfAccessUtils.McdfManager.IsWorking() && _plugin.ClientState.LocalPlayer != null)
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

        public void CheckPosing()
        {
            if (_posing == null)
            {
                BrioAccessUtils.EntityManager.SetSelectedEntity(_character);
                BrioAccessUtils.EntityManager.TryGetCapabilityFromSelectedEntity<PosingCapability>(out var posing);
                _posing = posing;
            }
        }
        public void SetDefaults(Vector3 position, Vector3 rotation)
        {
            _defaultPosition = position;
            _defaultRotation = rotation;
            if (!_followPlayer)
            {
                _currentPosition = position;
                _currentRotation = rotation;
            }
            _shouldBeMoving = false;
            _plugin.AnamcoreManager.StopEmote(_character.Address);
        }

        public void SetDefaultRotation(Vector3 vector3, Vector3 rotation)
        {
            _defaultRotation = rotation;
        }

        public void WalkToTarget(Vector3 vector3, float speed)
        {
            _shouldBeMoving = true;
            _target = vector3;
            _speed = speed;
        }

        public void FollowPlayer(float speed, bool setsCurrentPosition = false)
        {
            _lock = true;
            if (_plugin.ClientState.LocalPlayer != null)
            {
                _followPlayer = true;
                _speed = speed;
                if (setsCurrentPosition)
                {
                    _currentPosition = _plugin.ClientState.LocalPlayer.Position;
                }
            }
            _lock = false;
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
