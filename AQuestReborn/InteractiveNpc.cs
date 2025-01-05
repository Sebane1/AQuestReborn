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

namespace AQuestReborn
{
    public class InteractiveNpc : IDisposable
    {
        private ICharacter _character;
        private Plugin _plugin;
        private bool _shouldBeMoving;
        private Vector3 _target;
        private float _speed;
        private bool _shouldBeScaling;
        private Vector3 _targetScale = new Vector3(1, 1, 1);
        private float _scaleSpeed;
        private bool _followPlayer;
        private Vector3 _currentPosition;
        private bool _disposed;

        public InteractiveNpc(Plugin plugin, ICharacter character)
        {
            _character = character;
            _plugin = plugin;
            _plugin.Framework.Update += Framework_Update;
            _plugin.ClientState.TerritoryChanged += ClientState_TerritoryChanged;
        }

        private void ClientState_TerritoryChanged(ushort obj)
        {
            Dispose();
        }

        private void Framework_Update(IFramework framework)
        {
            if (!_plugin.AQuestReborn.WaitingForMcdfLoad && _plugin.ClientState.LocalPlayer != null)
            {
                if (_character != null)
                {
                    float delta = ((float)_plugin.Framework.UpdateDelta.Milliseconds / 1000f);
                    if (_shouldBeMoving)
                    {
                        BrioAccessUtils.EntityManager.SetSelectedEntity(_character);
                        BrioAccessUtils.EntityManager.TryGetCapabilityFromSelectedEntity<PosingCapability>(out var posing);
                        if (posing != null)
                        {
                            posing.ModelPosing.Transform = new Brio.Core.Transform()
                            {
                                Position = _currentPosition = Vector3.Lerp(_currentPosition, _target, _speed * delta),
                                Rotation = CoordinateUtility.ToQuaternion(0, CoordinateUtility.ConvertRadiansToDegrees(_plugin.ClientState.LocalPlayer.Rotation), 0),
                                Scale = Vector3.Lerp(posing.ModelPosing.Transform.Scale, _targetScale, _scaleSpeed * delta)
                            };
                        }
                    }
                    else if (_followPlayer && !_plugin.DialogueWindow.IsOpen && !_plugin.ChoiceWindow.IsOpen)
                    {
                        BrioAccessUtils.EntityManager.SetSelectedEntity(_character);
                        BrioAccessUtils.EntityManager.TryGetCapabilityFromSelectedEntity<PosingCapability>(out var posing);
                        if (Vector3.Distance(_currentPosition, _plugin.ClientState.LocalPlayer.Position) > 1)
                        {
                            if (posing != null)
                            {
                                posing.ModelPosing.Transform = new Brio.Core.Transform()
                                {
                                    Position = _currentPosition = Vector3.Lerp(_currentPosition, _plugin.ClientState.LocalPlayer.Position, _speed * delta),
                                    Rotation = CoordinateUtility.ToQuaternion(0, CoordinateUtility.ConvertRadiansToDegrees(_plugin.ClientState.LocalPlayer.Rotation), 0),
                                    Scale = posing.ModelPosing.Transform.Scale
                                };
                            }
                            var value = _plugin.AnamcoreManager.GetCurrentAnimationId(_plugin.ClientState.LocalPlayer);
                            _plugin.AnamcoreManager.TriggerEmote(_character.Address, 22);
                        }
                        else
                        {
                            _plugin.AnamcoreManager.StopEmote(_character.Address);
                        }

                    }
                }
                else
                {
                    Dispose();
                }
            }
        }


        public void SetPosition(Vector3 vector3)
        {
            _currentPosition = vector3;
            _target = vector3;
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
