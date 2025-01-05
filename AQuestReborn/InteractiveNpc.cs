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
        private Vector3 _defaultPosition;
        private Vector3 _defaultRotation;
        private Vector3 _currentRotation;
        private bool _disposed;
        private Vector3 _currentScale;
        private PosingCapability? _posing;

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
            if (!_plugin.AQuestReborn.WaitingForMcdfLoad && !McdfAccessUtils.McdfManager.IsWorking() && _plugin.ClientState.LocalPlayer != null)
            {
                if (_character != null)
                {
                    float delta = ((float)_plugin.Framework.UpdateDelta.Milliseconds / 1000f);
                    if (_followPlayer && !_plugin.DialogueWindow.IsOpen && !_plugin.ChoiceWindow.IsOpen)
                    {
                        if (Vector3.Distance(_currentPosition, _plugin.ClientState.LocalPlayer.Position) > 1)
                        {
                            SetTransform(_currentPosition = Vector3.Lerp(_currentPosition, _plugin.ClientState.LocalPlayer.Position, _speed * delta),
                                _currentRotation = Vector3.Lerp(_currentRotation, new Vector3(0, CoordinateUtility.ConvertRadiansToDegrees(_plugin.ClientState.LocalPlayer.Rotation), 0), _speed * delta),
                                _currentScale = Vector3.Lerp(_currentScale, _targetScale, _scaleSpeed * delta));
                            var value = _plugin.AnamcoreManager.GetCurrentAnimationId(_plugin.ClientState.LocalPlayer);
                            _plugin.AnamcoreManager.TriggerEmote(_character.Address, 22);
                        }
                        else
                        {
                            SetTransform(_currentPosition = Vector3.Lerp(_currentPosition,
                            new Vector3(_plugin.ClientState.LocalPlayer.Position.X, _currentPosition.Y, _plugin.ClientState.LocalPlayer.Position.Z), _speed * delta),
                            _currentRotation, _currentScale = Vector3.Lerp(_currentScale, _targetScale, _scaleSpeed * delta));
                            _plugin.AnamcoreManager.StopEmote(_character.Address);
                        }
                    }
                    else
                    {
                        if (_shouldBeMoving)
                        {
                            SetTransform(_currentPosition = Vector3.Lerp(_currentPosition, _target, _speed * delta),
                                         _currentRotation = Vector3.Lerp(_currentRotation, _defaultRotation, _speed * delta),
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
                            SetTransform(_currentPosition = Vector3.Lerp(_currentPosition, _defaultPosition, 10 * delta),
                                         _currentRotation = Vector3.Lerp(_currentRotation, _defaultRotation, 10 * delta),
                                         _currentScale = Vector3.Lerp(_currentScale, _targetScale, _scaleSpeed * delta));
                        }
                    }
                }
                else
                {
                    Dispose();
                }
            }
        }
        public Brio.Core.Transform GetTransform()
        {
            if (_posing != null)
            {
                return _posing.ModelPosing.Transform;
            }
            return new Brio.Core.Transform { Position = new Vector3(), Rotation = new System.Numerics.Quaternion(), Scale = new Vector3(1, 1, 1) };
        }
        public void SetTransform(Vector3 position, Vector3 rotation, Vector3 scale)
        {
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
        public void SetDefaults(Vector3 position, Vector3 rotation)
        {
            _defaultPosition = position;
            _defaultRotation = rotation;
            _currentPosition = _defaultPosition + new Vector3(0, -10, 0);
            _currentRotation = rotation;
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

        public void FollowPlayer(float speed)
        {
            _followPlayer = true;
            _speed = speed;
            _currentPosition = _character.Position;
            var value = _plugin.AnamcoreManager.GetCurrentAnimationId(_plugin.ClientState.LocalPlayer);
            _plugin.AnamcoreManager.TriggerEmote(_character.Address, 22);
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
