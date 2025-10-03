using GameInterface.AutoSync;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
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
    /// 
    /// Collection Synchronization:
    /// - This class includes transpiler patches for methods that mutate the collection fields:
    ///   _deployedSiegeEngines, _reservedSiegeEngines, and _removedSiegeEngines
    /// - Patches target DeploySiegeEngine, RemoveSiegeEngine, ReserveSiegeEngine methods
    /// - Each Add/Remove operation triggers field sync to propagate changes across the network
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

    /// <summary>
    /// Harmony patch for SiegeEnginesContainer methods that mutate collection fields.
    /// 
    /// According to Patching-Basics wiki, collection field synchronization requires transpiler patches
    /// to inject sync triggers after each collection mutation operation (Add/Remove/Insert/etc).
    /// 
    /// Patched Methods:
    /// - DeploySiegeEngine: Adds to _deployedSiegeEngines list
    /// - RemoveSiegeEngine: Removes from _deployedSiegeEngines and adds to _removedSiegeEngines
    /// - ReserveSiegeEngine: Adds to _reservedSiegeEngines list
    /// 
    /// Implementation:
    /// Each transpiler identifies collection mutation IL instructions (callvirt to Add/Remove methods)
    /// and inserts IL code immediately after to trigger field synchronization via the AutoSync system.
    /// This ensures all collection changes are propagated to connected clients in real-time.
    /// </summary>
    [HarmonyPatch(typeof(SiegeEnginesContainer))]
    internal static class SiegeEnginesContainerCollectionPatches
    {
        /// <summary>
        /// Transpiler for DeploySiegeEngine method.
        /// Patches: _deployedSiegeEngines.Add() calls
        /// Emits sync trigger after each Add operation to propagate the list change.
        /// </summary>
        [HarmonyPatch("DeploySiegeEngine")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> DeploySiegeEngine_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var addMethod = AccessTools.Method(typeof(List<>).MakeGenericType(typeof(SiegeEngine)), "Add");
            var deployedField = AccessTools.Field(typeof(SiegeEnginesContainer), "_deployedSiegeEngines");
            
            for (int i = 0; i < codes.Count; i++)
            {
                // Look for callvirt to List<SiegeEngine>.Add
                if (codes[i].opcode == OpCodes.Callvirt && codes[i].operand is MethodInfo method && method.Name == "Add")
                {
                    // Insert field sync trigger after the Add call
                    // Pattern: Load 'this', load field, call sync trigger
                    var syncInstructions = GenerateFieldSyncInstructions(deployedField);
                    codes.InsertRange(i + 1, syncInstructions);
                    i += syncInstructions.Count; // Skip inserted instructions
                }
            }
            
            return codes.AsEnumerable();
        }

        /// <summary>
        /// Transpiler for RemoveSiegeEngine method.
        /// Patches: _deployedSiegeEngines.Remove() and _removedSiegeEngines.Add() calls
        /// Emits sync triggers after each collection mutation to maintain consistent state.
        /// </summary>
        [HarmonyPatch("RemoveSiegeEngine")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> RemoveSiegeEngine_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var deployedField = AccessTools.Field(typeof(SiegeEnginesContainer), "_deployedSiegeEngines");
            var removedField = AccessTools.Field(typeof(SiegeEnginesContainer), "_removedSiegeEngines");
            
            for (int i = 0; i < codes.Count; i++)
            {
                // Look for callvirt to collection methods (Add/Remove)
                if (codes[i].opcode == OpCodes.Callvirt && codes[i].operand is MethodInfo method)
                {
                    if (method.Name == "Remove" || method.Name == "Add")
                    {
                        // Determine which field was modified based on the context
                        // Insert appropriate field sync trigger
                        FieldInfo targetField = method.Name == "Remove" ? deployedField : removedField;
                        var syncInstructions = GenerateFieldSyncInstructions(targetField);
                        codes.InsertRange(i + 1, syncInstructions);
                        i += syncInstructions.Count;
                    }
                }
            }
            
            return codes.AsEnumerable();
        }

        /// <summary>
        /// Transpiler for ReserveSiegeEngine method.
        /// Patches: _reservedSiegeEngines.Add() calls
        /// Emits sync trigger after reservation list modifications.
        /// </summary>
        [HarmonyPatch("ReserveSiegeEngine")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> ReserveSiegeEngine_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var reservedField = AccessTools.Field(typeof(SiegeEnginesContainer), "_reservedSiegeEngines");
            
            for (int i = 0; i < codes.Count; i++)
            {
                // Look for callvirt to List<SiegeEngine>.Add
                if (codes[i].opcode == OpCodes.Callvirt && codes[i].operand is MethodInfo method && method.Name == "Add")
                {
                    // Insert field sync trigger after the Add call
                    var syncInstructions = GenerateFieldSyncInstructions(reservedField);
                    codes.InsertRange(i + 1, syncInstructions);
                    i += syncInstructions.Count;
                }
            }
            
            return codes.AsEnumerable();
        }

        /// <summary>
        /// Generates IL instructions to trigger field synchronization.
        /// Per AutoSync wiki guidance, this loads the instance and field, then invokes the sync mechanism.
        /// 
        /// Generated IL pattern:
        /// - ldarg.0 (load 'this')
        /// - ldfld (load the collection field)
        /// - pop (discard value, sync trigger is based on field assignment interception)
        /// - ldarg.0 (load 'this' again)
        /// - ldarg.0 (load 'this' for field getter)
        /// - ldfld (load field value)
        /// - stfld (store to same field - triggers AutoSync setter interception)
        /// </summary>
        private static List<CodeInstruction> GenerateFieldSyncInstructions(FieldInfo field)
        {
            return new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0), // Load 'this'
                new CodeInstruction(OpCodes.Ldarg_0), // Load 'this' again for field getter
                new CodeInstruction(OpCodes.Ldfld, field), // Load field value
                new CodeInstruction(OpCodes.Stfld, field)  // Store to trigger setter interception
            };
        }
    }
}
