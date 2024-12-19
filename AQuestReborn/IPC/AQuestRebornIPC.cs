using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AQuestReborn.IPC
{
    public interface AQuestRebornIPC
    {
        /// <summary>
        /// Gets currently loaded quest information.
        /// </summary>
        /// <returns>List of tuple [QuestId][QuestName] </returns>
        public List<Tuple<string, string>> GetQuests();
        public List<Tuple<string, string, bool>> GetPrimaryQuestObjectiveData(string questId);
        public int GetPrimaryQuestObjectiveProgressionIndex(string questId);

        public bool ObjectiveIdCompleted(string objectiveId);

        /// <summary>
        /// Configure event handle for future callbacks on dialogue events.
        /// </summary>
        /// <param name="">Tuple, Quest Id, Objective Id, Npc Name, NPC Text, NPC Gender, Raw QuestDisplayObject</param>
        /// <returns>Succeeded</returns>
        public bool OnDialogueOccured(EventHandler<Tuple<string, string, string, string, bool, object>> eventHandler);

        /// <summary>
        /// Configure event handle for future quest progression events/
        /// </summary>
        /// <param name="eventHandler">Tuple, Quest Id, Previous Objective Id, Current Objective Id</param>
        /// <returns>Succeeded</returns>
        public bool OnQuestProgression(EventHandler<Tuple<string, string, string, string, bool>> eventHandler);


    }
}
