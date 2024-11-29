using Dalamud.Plugin.Services;
using RoleplayingQuestCore;
using System.Numerics;

namespace SamplePlugin
{
    internal class QuestGameObject : IQuestGameObject
    {
        private IClientState _clientState;

        public QuestGameObject(IClientState clientState)
        {
            _clientState = clientState;
        }

        int IQuestGameObject.TerritoryId => _clientState.TerritoryType;

        string IQuestGameObject.Name => _clientState.LocalPlayer != null ? _clientState.LocalPlayer.Name.ToString() : "";

        Vector3 IQuestGameObject.Position => _clientState.LocalPlayer != null ? _clientState.LocalPlayer.Position : new Vector3();

        Vector3 IQuestGameObject.Rotation => _clientState.LocalPlayer != null ? new Vector3(0, _clientState.LocalPlayer.Rotation, 0) : new Vector3();
    }
}
