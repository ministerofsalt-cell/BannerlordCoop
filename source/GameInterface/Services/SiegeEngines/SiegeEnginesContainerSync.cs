using GameInterface.AutoSync;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Text;
using static TaleWorlds.CampaignSystem.Siege.SiegeEvent;

namespace GameInterface.Services.SiegeEngines
{
    /// <summary>
    /// Synchronizes SiegeEnginesContainer state across the network.
    /// 
    /// This class handles automatic synchronization of siege engine deployment and reservation state
    /// within a SiegeEnginesContainer. It ensures that both public properties and private backing fields
    /// are kept in sync between server and clients.
    /// 
    /// Architecture Notes:
    /// - For collection fields (lists/arrays), additional transpiler patches are required per wiki guidance
    ///   to intercept Add/Remove operations. See Patching-Basics wiki for collection patching details.
    /// - Private fields with underscore prefix represent internal state that must be synced alongside
    ///   their corresponding public properties to maintain consistency.
    /// - The sync system uses Harmony patches to intercept field changes and propagate them through
    ///   the message broker to all connected clients.
    /// </summary>
    internal class SiegeEnginesContainerSync : IAutoSync
    {
        public SiegeEnginesContainerSync(IAutoSyncBuilder autoSyncBuilder)
        {
            // Public property fields - deployment tracking
            autoSyncBuilder.AddField(AccessTools.Field(typeof(SiegeEnginesContainer), nameof(SiegeEnginesContainer.SiegePreparations)));
            autoSyncBuilder.AddField(AccessTools.Field(typeof(SiegeEnginesContainer), nameof(SiegeEnginesContainer.DeployedRangedSiegeEngines)));
            autoSyncBuilder.AddField(AccessTools.Field(typeof(SiegeEnginesContainer), nameof(SiegeEnginesContainer.DeployedMeleeSiegeEngines)));
            
            // Private backing fields - internal state (note: collection fields require transpiler patches)
            autoSyncBuilder.AddField(AccessTools.Field(typeof(SiegeEnginesContainer), "_deployedSiegeEngines"));
            autoSyncBuilder.AddField(AccessTools.Field(typeof(SiegeEnginesContainer), "_reservedSiegeEngines"));
            autoSyncBuilder.AddField(AccessTools.Field(typeof(SiegeEnginesContainer), "_deployedSiegeEngineTypesCount"));
            autoSyncBuilder.AddField(AccessTools.Field(typeof(SiegeEnginesContainer), "_reservedSiegeEngineTypesCount"));
            autoSyncBuilder.AddField(AccessTools.Field(typeof(SiegeEnginesContainer), "_removedSiegeEngines"));
        }
    }
}
