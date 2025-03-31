using System;
using System.Reflection;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;
using GunGame.API;

namespace buylevel
{
    public class BuyLevelPlugin : BasePlugin
    {
        public override string ModuleName => "BuyLevel Plugin";
        public override string ModuleVersion => "1.0.0";
        public override string ModuleAuthor => "Andrew Mathews";
        public override string ModuleDescription => "Allows players to !buylevel like in the old CS:GO server";

        private static PluginCapability<IAPI> APICapability { get; } = new("gungame:api");
        private IAPI? ggApi;
        private const int CostPerLevel = 10; // Kills required to buy a level (adjustable)

        public override void Load(bool hotReload)
        {
            ggApi = APICapability.Get();
            if (ggApi == null)
            {
                Logger.LogInformation("[BuyLevelPlugin] Error: GunGame API not found! Ensure GunGame plugin is loaded.");
                return;
            }

            AddCommand("css_buylevel", "Buy a level in GunGame", OnBuyLevelCommand);
            Logger.LogInformation("[BuyLevelPlugin] Loaded successfully.");
        }

        private void OnBuyLevelCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid || !player.PawnIsAlive)
            {
                Logger.LogInformation("[BuyLevelPlugin] Invalid or dead player attempted to use !buylevel.");
                return;
            }

            if (ggApi == null)
            {
                player.PrintToChat("Error: GunGame API is not available.");
                Logger.LogInformation("[BuyLevelPlugin] Error: ggApi is null in OnBuyLevelCommand.");
                return;
            }

            if (ggApi.IsWarmupInProgress())
            {
                player.PrintToChat("You cannot buy levels during warmup.");
                return;
            }

            int slot = player.Slot;
            int currentLevel = ggApi.GetPlayerLevel(slot);

            if (currentLevel >= ggApi.GetMaxLevel())
            {
                player.PrintToChat("You are already at the maximum level.");
                return;
            }

            try
            {
                // Use reflection to access GunGame internals
                var ggPluginType = ggApi.GetType();
                var ggPluginInstance = ggApi;

                // Access playerManager
                var playerManagerField = ggPluginType.GetField("playerManager", BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new Exception("'playerManager' field not found");
                var playerManager = playerManagerField.GetValue(ggPluginInstance);
                if (playerManager == null)
                {
                    player.PrintToChat("Error: GunGame player manager not initialized.");
                    Logger.LogInformation("[BuyLevelPlugin] Error: playerManager is null.");
                    return;
                }

                // Access playerMap dictionary
                var playerMapField = playerManager.GetType().GetField("playerMap", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new Exception("'playerMap' field not found");
                var playerMapObj = playerMapField.GetValue(playerManager);
                if (playerMapObj == null)
                {
                    player.PrintToChat("Error: Player map data not initialized.");
                    Logger.LogInformation("[BuyLevelPlugin] Error: playerMap is null.");
                    return;
                }
                var playerMap = (System.Collections.IDictionary)playerMapObj;
                if (!playerMap.Contains(slot))
                {
                    player.PrintToChat("Error: Player data not found.");
                    Logger.LogInformation($"[BuyLevelPlugin] Error: No player data for slot {slot}.");
                    return;
                }
                var playerData = playerMap[slot];
                if (playerData == null)
                {
                    player.PrintToChat("Error: Player data is null.");
                    Logger.LogInformation($"[BuyLevelPlugin] Error: playerData for slot {slot} is null.");
                    return;
                }

                // Access GGVariables.Instance.weaponsList from GG2.dll
                var ggVariablesType = Type.GetType("GunGame.GGVariables, GG2")
                    ?? throw new Exception("GGVariables type not found in GG2 assembly");
                var instanceField = ggVariablesType.GetField("Instance", BindingFlags.Public | BindingFlags.Static)
                    ?? throw new Exception("'Instance' field not found in GGVariables");
                var ggVariables = instanceField.GetValue(null);
                if (ggVariables == null)
                {
                    player.PrintToChat("Error: GunGame variables not initialized.");
                    Logger.LogInformation("[BuyLevelPlugin] Error: GGVariables.Instance is null.");
                    return;
                }
                var weaponsListField = ggVariablesType.GetField("weaponsList", BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new Exception("'weaponsList' field not found");
                var weaponsListObj = weaponsListField.GetValue(ggVariables);
                if (weaponsListObj == null)
                {
                    player.PrintToChat("Error: Weapons list not initialized.");
                    Logger.LogInformation("[BuyLevelPlugin] Error: weaponsList is null.");
                    return;
                }
                var weaponsList = (System.Collections.IList)weaponsListObj;

                // Get current weapon (levels start at 1, list is 0-indexed)
                var currentWeaponObj = weaponsList[currentLevel - 1];
                if (currentWeaponObj == null)
                {
                    player.PrintToChat("Error: Current weapon data is missing.");
                    Logger.LogInformation($"[BuyLevelPlugin] Error: Weapon at level {currentLevel} is null.");
                    return;
                }
                var weaponNameField = currentWeaponObj.GetType().GetField("Name", BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new Exception("'Name' field not found in Weapon");
                var currentWeaponObjName = weaponNameField.GetValue(currentWeaponObj);
                if (currentWeaponObjName == null)
                {
                    player.PrintToChat("Error: Current weapon name is missing.");
                    Logger.LogInformation($"[BuyLevelPlugin] Error: Weapon name at level {currentLevel} is null.");
                    return;
                }
                string currentWeapon = (string)currentWeaponObjName;

                // Restrict buying on grenade or knife levels
                if (currentWeapon.Contains("hegrenade") || currentWeapon.Contains("knife"))
                {
                    player.PrintToChat("You cannot buy levels when on a grenade or knife level.");
                    return;
                }

                // Get player's "points" (using CurrentKillsPerWeap as a proxy)
                var pointsField = playerData.GetType().GetField("CurrentKillsPerWeap", BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new Exception("'CurrentKillsPerWeap' field not found");
                var pointsObj = pointsField.GetValue(playerData);
                if (pointsObj == null)
                {
                    player.PrintToChat("Error: Player kill data is missing.");
                    Logger.LogInformation($"[BuyLevelPlugin] Error: CurrentKillsPerWeap for slot {slot} is null.");
                    return;
                }
                int points = (int)pointsObj;

                // Check if player has enough points
                if (points < CostPerLevel)
                {
                    player.PrintToChat($"You need {CostPerLevel} kills to buy a level. You have {points} kills.");
                    return;
                }

                // Deduct points
                pointsField.SetValue(playerData, points - CostPerLevel);

                // Increase level (Level is private, use SetLevel method)
                var setLevelMethod = playerData.GetType().GetMethod("SetLevel", BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new Exception("'SetLevel' method not found");
                setLevelMethod.Invoke(playerData, new object[] { currentLevel + 1 });

                // Get new weapon
                var newWeaponObj = weaponsList[currentLevel]; // Next level (currentLevel + 1 - 1)
                if (newWeaponObj == null)
                {
                    player.PrintToChat("Error: New weapon data is missing.");
                    Logger.LogInformation($"[BuyLevelPlugin] Error: Weapon at level {currentLevel + 1} is null.");
                    return;
                }
                var newWeaponObjName = weaponNameField.GetValue(newWeaponObj);
                if (newWeaponObjName == null)
                {
                    player.PrintToChat("Error: New weapon name is missing.");
                    Logger.LogInformation($"[BuyLevelPlugin] Error: Weapon name at level {currentLevel + 1} is null.");
                    return;
                }
                string newWeapon = (string)newWeaponObjName;

                // Update player's weapon
                var pawn = player.PlayerPawn.Value;
                if (pawn != null && pawn.WeaponServices != null)
                {
                    if (pawn.WeaponServices.ActiveWeapon.Value != null)
                    {
                        pawn.WeaponServices.ActiveWeapon.Value.Remove();
                    }
                    player.GiveNamedItem(newWeapon);
                }

                // Notify player
                player.PrintToChat($"You spent {CostPerLevel} kills to upgrade to level {currentLevel + 1} ({newWeapon})!");
                Logger.LogInformation($"[BuyLevelPlugin] {player.PlayerName} bought level {currentLevel + 1}");
            }
            catch (Exception ex)
            {
                player.PrintToChat("An error occurred while buying a level.");
                Logger.LogInformation($"[BuyLevelPlugin] Exception: {ex.Message}\nStack Trace: {ex.StackTrace}");
            }
        }
    }
}