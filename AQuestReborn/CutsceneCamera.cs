using Brio.Capabilities.Camera;
using Brio.Entities.Camera;
using Brio.Entities;
using Brio.UI.Controls.Editors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Brio;
using System.Numerics;
using SamplePlugin;
using System.Diagnostics;
using ECommons.MathHelpers;
using static FFXIVClientStructs.FFXIV.Client.UI.Misc.CharaViewPortrait.Delegates;
using Dalamud.Game.ClientState.GamePad;
using Lumina.Excel.Sheets;
using Hypostasis.Game.Structures;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using AQuestReborn.UiHide;

namespace AQuestReborn
{
    internal unsafe class CutsceneCamera : IDisposable
    {
        static private CameraCapability _camera;
        static private Plugin _plugin;
        static private Vector3 _startPosition;
        static private Vector3 _endPosition;
        static private float _speed;
        static Stopwatch _dollyTimer = new Stopwatch();
        static private Vector3 _startRotation;
        static private Vector3 _endRotation;
        static bool _isDoingCutScene = false;
        private static Vector3 _currentCameraPosition;
        static private Vector3 _currentRotation;
        static private bool _isCameraEditor;
        static private Vector3 _cameraStartingEditPosition;
        static private Vector3 _cameraStartingEditRotation;
        static private float _startFov;
        static private float _endFov;
        static private float _startZoom;
        static private float _endZoom;


        // xor al, al
        public static readonly AsmPatch cameraNoClippyReplacer = new("E8 ?? ?? ?? ?? F3 44 0F 10 B5", [0x30, 0xC0, 0x90, 0x90, 0x90], _isDoingCutScene); // E8 ?? ?? ?? ?? 48 8B B4 24 E0 00 00 00 40 32 FF (0x90, 0x90, 0x90, 0x90, 0x90)
        private static AsmPatch addMidHookReplacer;

        [HypostasisSignatureInjection("F3 0F 59 35 ?? ?? ?? ?? F3 0F 10 45 ??", Static = true, Required = true)]
        private static float* foVDeltaPtr;

        private static void SetCameraLookAtDetour(GameCamera* camera, Vector3* lookAtPosition, Vector3* cameraPosition, Vector3* a4) // a4 seems to be immediately overwritten and unused
        {
            if (_isDoingCutScene) return;
            camera->VTable.setCameraLookAt.Original(camera, lookAtPosition, cameraPosition, a4);
        }
        private static void GetCameraPositionDetour(GameCamera* camera, GameObject* target, Vector3* position, Bool swapPerson)
        {
            if (!_isDoingCutScene)
            {
                camera->VTable.getCameraPosition.Original(camera, target, position, swapPerson);
                _currentCameraPosition = new Vector3(position->X, position->Y, position->Z);
            }
            else
            {
                *position = _currentCameraPosition;
            }
        }
        public static float FoVDelta // 0.08726646751
        {
            get => foVDeltaPtr != null ? *foVDeltaPtr : 0;
            set
            {
                if (foVDeltaPtr != null)
                    *foVDeltaPtr = value;
            }
        }
        static public Vector3 Position
        {
            get
            {
                unsafe
                {
                    return _currentCameraPosition;
                }
            }
        }
        static public Vector3 Rotation
        {
            get
            {
                unsafe
                {
                    return new Vector3(-_camera.Camera->Angle.Y.RadToDeg(), _camera.Camera->Angle.X.RadToDeg(), _camera.Camera->Rotation.RadToDeg());
                }
            }
        }

        static public float CameraFov
        {
            get
            {
                unsafe
                {
                    return _camera.Camera->Camera.FoV;
                }
            }
        }

        static public float CameraZoom
        {
            get
            {
                unsafe
                {
                    return _camera.Camera->Camera.Distance;
                }
            }
        }
        public CutsceneCamera(Plugin plugin)
        {
            var vtbl = Common.CameraManager->worldCamera->VTable;
            vtbl.setCameraLookAt.CreateHook(SetCameraLookAtDetour);
            vtbl.getCameraPosition.CreateHook(GetCameraPositionDetour);
            vtbl.getCameraTarget.CreateHook(GetCameraTargetDetour);
            vtbl.canChangePerspective.CreateHook(CanChangePerspectiveDetour);
            //vtbl.getZoomDelta.CreateHook(GetZoomDeltaDetour);

            GameCamera.getCameraAutoRotateMode.CreateHook(GetCameraAutoRotateModeDetour);
            GameCamera.getCameraMaxMaintainDistance.CreateHook(GetCameraMaxMaintainDistanceDetour);
            GameCamera.updateLookAtHeightOffset.CreateHook(UpdateLookAtHeightOffsetDetour);
            GameCamera.shouldDisplayObject.CreateHook(ShouldDisplayObjectDetour);
            _plugin = plugin;
            _plugin.Framework.Update += Framework_Update;
            RefreshCamera();
        }
        private static byte GetCameraAutoRotateModeDetour(GameCamera* camera, Framework* framework) => (byte)(_isDoingCutScene ? 4 : GameCamera.getCameraAutoRotateMode.Original(camera, framework));
        private static Bool CanChangePerspectiveDetour() => !_isDoingCutScene;
        private static float GetCameraMaxMaintainDistanceDetour(GameCamera* camera) => GameCamera.getCameraMaxMaintainDistance.Original(camera) is var ret && ret < 10f ? ret : camera->maxZoom;
        public static Bool UpdateLookAtHeightOffsetDetour(GameCamera* camera, GameObject* o, Bool zero)
        {
            var ret = GameCamera.updateLookAtHeightOffset.Original(camera, o, zero);
            //if (ret && !zero && (nint)o == DalamudApi.ClientState.LocalPlayer?.Address && PresetManager.CurrentPreset != PresetManager.DefaultPreset)
            //    camera->lookAtHeightOffset = PresetManager.CurrentPreset.LookAtHeightOffset;
            return ret;
        }
        public static Bool ShouldDisplayObjectDetour(GameCamera* camera, GameObject* o, Vector3* cameraPosition, Vector3* cameraLookAt) =>
    ((nint)o != DalamudApi.ClientState.LocalPlayer?.Address || camera != Common.CameraManager->worldCamera || camera->mode != 0 || (camera->transition != 0 && camera->controlType <= 2)) && GameCamera.shouldDisplayObject.Original(camera, o, cameraPosition, cameraLookAt);

        static private unsafe void Framework_Update(Dalamud.Plugin.Services.IFramework framework)
        {
            try
            {
                RefreshCamera();
                if (IsDoingCutScene)
                {
                    var dollyProgress = Math.Clamp(_dollyTimer.ElapsedMilliseconds / _speed, 0, 1);
                    _currentCameraPosition = Vector3.Lerp(_startPosition, _endPosition, dollyProgress);
                    _currentRotation = Vector3.Lerp(_startRotation, _endRotation, dollyProgress);
                    var zoom = float.Lerp(_startZoom, _endZoom, dollyProgress);
                    _camera.Camera->Camera.MinDistance = zoom;
                    _camera.Camera->Camera.Distance = zoom;
                    _camera.Camera->Camera.MaxDistance = zoom;
                    _camera.Camera->Camera.FoV = float.Lerp(_startFov, _endFov, dollyProgress);
                    _camera.Camera->Rotation = _currentRotation.Z.DegToRad();
                    _camera.Camera->Angle = new Vector2(_currentRotation.Y.DegToRad(), _currentRotation.X.DegToRad());
                }
                else
                {
                    if (_isCameraEditor)
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                _plugin.PluginLog.Warning(ex, ex.Message);
            }
        }

        static public CameraCapability Camera { get => _camera; }
        static public bool IsDoingCutScene
        {
            get => _isDoingCutScene; set
            {
                if (_camera.DelimitCamera != value)
                {
                    _camera.DelimitCamera = value;
                }
                _camera.DisableCollision = value;
                _isDoingCutScene = value;
                if (value)
                {
                    unsafe
                    {

                    }
                }
            }
        }

        static public bool IsCameraEditor
        {
            get => _isCameraEditor; set
            {
                if (_camera.DelimitCamera != value)
                {
                    _camera.DelimitCamera = value;
                }
                _isCameraEditor = value;
                if (value)
                {
                    unsafe
                    {

                    }
                }
                _cameraStartingEditPosition = Position;
                _cameraStartingEditRotation = Rotation;
            }
        }

        static public unsafe void RefreshCamera()
        {
            if (_camera == null)
            {
                if (BrioAccessUtils.EntityManager.TryGetEntity<CameraEntity>("camera", out var camEntity))
                {
                    if (camEntity.TryGetCapability<CameraCapability>(out var camCap))
                    {
                        _camera = camCap;
                    }
                    return;
                }
            }
        }
        private static GameObject* GetCameraTargetDetour(GameCamera* camera)
        {
            //if (EnableSpectating)
            //{
            //    if (DalamudApi.TargetManager.FocusTarget is { } focus)
            //    {
            //        IsSpectating = true;
            //        return (GameObject*)focus.Address;
            //    }

            //    if (DalamudApi.TargetManager.SoftTarget is { } soft)
            //    {
            //        IsSpectating = true;
            //        return (GameObject*)soft.Address;
            //    }
            //}

            //if (Cammy.Config.DeathCamMode == Configuration.DeathCamSetting.Spectate && DalamudApi.Condition[ConditionFlag.Unconscious] && DalamudApi.TargetManager.Target is { } target)
            //{
            //    IsSpectating = true;
            //    return (GameObject*)target.Address;
            //}

            //IsSpectating = false;
            return camera->VTable.getCameraTarget.Original(camera);
        }
        static public unsafe void SetCameraPosition(Vector3 position)
        {
            RefreshCamera();
            _startPosition = position;
            _endPosition = position;
            _dollyTimer.Restart();
        }
        static public unsafe void SetCameraPosition(Vector3 startPosition, Vector3 endPosition, float speed)
        {
            _startPosition = startPosition;
            _endPosition = endPosition;
            _speed = speed;
            _dollyTimer.Restart();
        }
        static public unsafe void SetCameraRotation(Vector3 rotation)
        {
            _startRotation = rotation;
            _endRotation = rotation;
        }

        static public unsafe void SetFov(float fov)
        {
            _startFov = fov;
            _endFov = fov;
        }
        static public void SetFov(float startFov, float endFov)
        {
            _startFov = startFov;
            _endFov = endFov;
        }

        static public void SetZoom(float zoom)
        {
            _startZoom = zoom;
            _endZoom = zoom;
        }

        static public void SetZoom(float startZoom, float endZoom)
        {
            _startZoom = startZoom;
            _endZoom = endZoom;
        }

        static public unsafe void SetCameraRotation(Vector3 startRotation, Vector3 endRotation)
        {
            RefreshCamera();
            _startRotation = startRotation;
            _endRotation = endRotation;
        }
        static public unsafe void ResetCamera()
        {
            _isDoingCutScene = false;
            RefreshCamera();
            _camera.Reset();
        }

        public void Dispose()
        {
            ResetCamera();
            _isDoingCutScene = false;
            Plugin.Instance.Framework.Update -= Framework_Update;
        }
    }
}
