/*
TODO:
- Fix OnPlayerLoot hooks not working
- Add animal names to config for localization
- Store and check if player was already looted once
*/

using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("GatherRewards", "Wulf/lukespragg", "2.0.0", ResourceId = 770)]
    [Description("Gain money through the Economics API for killing and gathering.")]

    class GatherRewards : RustPlugin
    {
        // Do NOT edit this file, instead edit GatherRewards.json in server/<identity>/oxide/config

        #region Configuration Defaults

        PluginConfig DefaultConfig()
        {
            var defaultConfig = new PluginConfig
            {
                Settings = new PluginSettings
                {
                    ShowMessages = true,
                    Rewards = new Dictionary<string, int>
                    {
                        { PluginRewards.Corpse, 25 },
                        { PluginRewards.Ore, 25 },
                        { PluginRewards.Stones, 25 },
                        { PluginRewards.Wood, 25 }
                    }
                },
                Messages = new Dictionary<string, string>
                {
                    { PluginMessage.ReceivedForGather, "You have received ${0} for gathering {1}." },
                    { PluginMessage.ReceivedForKill, "You have received ${0} for killing a {1}." },
                    { PluginMessage.ReceivedForLoot, "You have received ${0} for looting {1}." }
                }
            };
            foreach (var str in GameManifest.Get().pooledStrings)
            {
                if (!str.str.StartsWith("assets/bundled/prefabs/autospawn/animals/")) continue;
                var animal = str.str.Substring(str.str.LastIndexOf("/", StringComparison.Ordinal) + 1).Replace(".prefab", "");
                defaultConfig.Settings.Rewards[UppercaseFirst(animal)] = 25;
            }
            return defaultConfig;
        }

        #endregion

        #region Configuration Setup

        bool configChanged;
        PluginConfig config;

        static class PluginRewards
        {
            public const string Corpse = "Corpse";
            public const string Ore = "Ore";
            public const string Stones = "Stones";
            public const string Wood = "Wood";
        }

        static class PluginMessage
        {
            public const string ReceivedForGather = "ReceivedForGather";
            public const string ReceivedForKill = "ReceivedForKill";
            public const string ReceivedForLoot = "ReceivedForLoot";
        }

        class PluginSettings
        {
            public bool ShowMessages { get; set; }
            public Dictionary<string, int> Rewards { get; set; }
        }

        class PluginConfig
        {
            public PluginSettings Settings { get; set; }
            public Dictionary<string, string> Messages { get; set; }
        }

        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(DefaultConfig(), true);
            PrintWarning("New configuration file created.");
        }

        void LoadConfigValues()
        {
            config = Config.ReadObject<PluginConfig>();
            var defaultConfig = DefaultConfig();
            Merge(config.Messages, defaultConfig.Messages);
            Merge(config.Settings.Rewards, defaultConfig.Settings.Rewards);

            if (!configChanged) return;
            PrintWarning("Configuration file updated.");
            Config.WriteObject(config);
        }

        void Merge<T1, T2>(IDictionary<T1, T2> current, IDictionary<T1, T2> defaultDict)
        {
            foreach (var pair in defaultDict.Where(pair => !current.ContainsKey(pair.Key))) {
                current[pair.Key] = pair.Value;
                configChanged = true;
            }
            var oldPairs = current.Keys.Except(defaultDict.Keys).ToList();
            foreach (var oldPair in oldPairs)
            {
                current.Remove(oldPair);
                configChanged = true;
            }
        }

        #endregion

        void Loaded() => LoadConfigValues();

        void OnDispenserGather(ResourceDispenser dispenser, BaseEntity entity, Item item)
        {
            if (!Economics) return;

            var player = entity.ToPlayer();

            if (player)
            {
                var shortName = item.info.shortname;
                string resource = null;
                var amount = 0;

                if (shortName.Contains(".ore"))
                {
                    amount = config.Settings.Rewards[PluginRewards.Ore];
                    resource = "ore";
                }

                if (shortName.Equals("stones"))
                {
                    amount = config.Settings.Rewards[PluginRewards.Stones];
                    resource = "stones";
                }

                if (dispenser.GetComponentInParent<TreeEntity>())
                {
                    amount = config.Settings.Rewards[PluginRewards.Wood];
                    resource = "wood";
                }

                if (resource == null || amount <= 0) return;

                Economics.CallHook("Deposit", player.userID, amount);

                if (config.Settings.ShowMessages)
                {
                    PrintToChat(player, string.Format(config.Messages[PluginMessage.ReceivedForGather], amount, resource));
                }
            }
        }

        void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (!Economics) return;
            if (!entity.GetComponent("BaseNPC")) return;
            if (!info.Initiator?.ToPlayer()) return;

            var player = info.Initiator?.ToPlayer();
            var animal = UppercaseFirst(entity.ShortPrefabName.Replace(".prefab", ""));

            int amount;
            if (!config.Settings.Rewards.TryGetValue(animal, out amount) || amount <= 0) return;

            Economics.CallHook("Deposit", player.userID, amount);

            if (config.Settings.ShowMessages)
            {
                PrintToChat(player, string.Format(config.Messages[PluginMessage.ReceivedForKill], amount, animal.ToLower()));
            }
        }

        void OnPlayerLoot(PlayerLoot lootInventory, BaseEntity targetEntity)
        {
            if (!(targetEntity is BasePlayer)) return;

            var targetPlayer = (BasePlayer)targetEntity;
            var player = lootInventory.GetComponent("BasePlayer") as BasePlayer;
            var amount = config.Settings.Rewards[PluginRewards.Corpse];

            if (!player || amount <= 0) return;

            Economics.CallHook("Deposit", player.userID, amount);

            if (config.Settings.ShowMessages)
            {
                PrintToChat(player, string.Format(config.Messages[PluginMessage.ReceivedForLoot], amount, targetPlayer.displayName));
            }
        }

        #region Economics Support

        [PluginReference] Plugin Economics;

        #endregion

        #region Helpers

        static string UppercaseFirst(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var a = s.ToCharArray();
            a[0] = char.ToUpper(a[0]);
            return new string(a);
        }

        #endregion
    }
}
