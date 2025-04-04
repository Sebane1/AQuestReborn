using Dalamud.Plugin.Services;
using DragAndDropTexturing.ThreadSafeDalamudObjectTable;
using RoleplayingQuestCore;
using System.Numerics;

namespace SamplePlugin
{
    internal class QuestGameObject : IQuestGameObject
    {
        private ThreadSafeGameObjectManager _objectTable;
        private IClientState _clientState;

        public QuestGameObject(ThreadSafeGameObjectManager objectTable, IClientState clientState)
        {
            _objectTable = objectTable;
            _clientState = clientState;
        }

        int IQuestGameObject.TerritoryId => _clientState.TerritoryType;

        string IQuestGameObject.Name => _objectTable.LocalPlayer != null ? _objectTable.LocalPlayer.Name.ToString() : "";

        Vector3 IQuestGameObject.Position => _objectTable.LocalPlayer != null ? _objectTable.LocalPlayer.Position : new Vector3();

        Vector3 IQuestGameObject.Rotation => _objectTable.LocalPlayer != null ? new Vector3(0, _objectTable.LocalPlayer.Rotation, 0) : new Vector3();
    }
}
