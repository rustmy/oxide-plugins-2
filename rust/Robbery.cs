/*
TODO:
- Add option to steal random item from victim's inventory
- Add option for a cooldown
*/

using System;
using UnityEngine;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("Robbery", "Wulf/lukespragg", "2.3.0", ResourceId = 736)]
    [Description("Players can steal Economics money from other players.")]

    class Robbery : RustPlugin
    {
        // Do NOT edit this file, instead edit Robbery.json in server/<identity>/oxide/config

        #region Configuration

        // Messages
        string MoneyStolen => GetConfig("MoneyStolen", "You stole ${amount} from {player}!");
        string NothingStolen => GetConfig("NothingStolen", "You stole pocket lint from {player}!");

        // Settings
        bool AllowMugging => GetConfig("AllowMugging", true);
        bool AllowPickpocket => GetConfig("AllowPickpocket", true);
        float PercentAwake => GetConfig("PercentAwake", 100f);
        float PercentSleeping => GetConfig("PercentSleeping", 50f);

        protected override void LoadDefaultConfig()
        {
            // Messages
            Config["MoneyStolen"] = MoneyStolen;
            Config["NothingStolen"] = NothingStolen;

            // Settings
            Config["AllowMugging"] = AllowMugging;
            Config["AllowPickpocket"] = AllowPickpocket;
            Config["PercentAwake"] = PercentAwake;
            Config["PercentSleeping"] = PercentSleeping;

            SaveConfig();
        }

        #endregion

        #region General Setup

        [PluginReference] Plugin Economics;
        [PluginReference] Plugin EventManager;
        [PluginReference] Plugin ZoneManager;

        void Loaded()
        {
            LoadDefaultConfig();

            permission.RegisterPermission("robbery.allowed", this);

            if (!Economics) PrintWarning("Economics is not loaded, plugin disabled! Get Economics at http://oxidemod.org/plugins/717/");
        }

        #endregion

        #region Money Transfer

        void TransferMoney(BasePlayer victim, BasePlayer attacker)
        {
            // Check if player is in event/zone with no looting
            var inEvent = EventManager?.Call("isPlaying", victim);
            if (inEvent != null && (bool)inEvent) return;
            if (ZoneManager != null)
            {
                var noLooting = Enum.Parse(ZoneManager.GetType().GetNestedType("ZoneFlags"), "noplayerloot", true);
                if ((bool)ZoneManager.CallHook("HasPlayerFlag", victim, noLooting)) return;
            }

            // Calculate and transfer money
            var wallet = (double)Economics.Call("GetPlayerMoney", victim.userID);
            var amount = victim.IsSleeping() ? Math.Floor(wallet * (PercentSleeping / 100)) : Math.Floor(wallet * (PercentAwake / 100));
            if (amount > 0)
            {
                Economics.Call("Transfer", victim.userID, attacker.userID, amount);
                PrintToChat(attacker, MoneyStolen.Replace("{amount}", amount.ToString()).Replace("{player}", victim.displayName));
                return;
            }
            PrintToChat(attacker, NothingStolen.Replace("{player}", victim.displayName));
        }

        #endregion

        #region Mugging

        void OnEntityDeath(BaseEntity entity, HitInfo info)
        {
            if (!Economics || !AllowMugging) return;

            // Check for for valid players
            var victim = entity as BasePlayer;
            var attacker = info?.Initiator as BasePlayer;

            // Steal the money
            if (victim != null && attacker != null) TransferMoney(victim, attacker);
        }

        #endregion

        #region Pickpocketing

        void OnPlayerInput(BasePlayer attacker, InputState input)
        {
            if (!input.WasJustPressed(BUTTON.FIRE_PRIMARY) || !Economics || !AllowPickpocket) return;

            // Get target victim
            var ray = new Ray(attacker.eyes.position, attacker.eyes.HeadForward());
            var entity = FindObject(ray, 1);
            var victim = entity as BasePlayer;
            if (victim == null) return;

            // Make sure victim isn't looking
            var victimToAttacker = (attacker.transform.position - victim.transform.position).normalized;
            if (Vector3.Dot(victimToAttacker, victim.eyes.HeadForward().normalized) > 0) return;

            // Make sure attacker isn't holding an item
            if (attacker.GetActiveItem()?.GetHeldEntity() != null) return;

            // Steal the money
            TransferMoney(victim, attacker);
        }

        #endregion

        #region Helper Methods

        T GetConfig<T>(string name, T defaultValue)
        {
            if (Config[name] == null) return defaultValue;
            return (T)Convert.ChangeType(Config[name], typeof(T));
        }

        static BaseEntity FindObject(Ray ray, float distance)
        {
            RaycastHit hit;
            return !Physics.Raycast(ray, out hit, distance) ? null : hit.GetEntity();
        }

        bool HasPermission(BasePlayer player, string perm) => permission.UserHasPermission(player.UserIDString, perm);

        #endregion
    }
}
