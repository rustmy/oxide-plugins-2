using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Oxide.Core;
using Oxide.Core.Libraries;

namespace Oxide.Plugins
{
    [Info("CharitySigns", "Wulf/lukespragg", "0.1.0", ResourceId = 0)]
    [Description("Creates dynamic signs that show Child's Play event information.")]

    class CharitySigns : RustPlugin
    {
        // Do NOT edit this file, instead edit CharitySigns.json in server/<identity>/oxide/config

        #region Configuration

        // Messages
        string NoPermission => GetConfig("NoPermission", "Sorry, you can't use 'charitysign' right now");
        string NoSignsFound => GetConfig("NoSignFound", "No usable signs could be found");

        // Settings
        string ChatCommand => GetConfig("ChatCommand", "charitysign");
        string EventId => GetConfig("EventId", "");
        bool LockSigns => GetConfig("LockSigns", true);

        protected override void LoadDefaultConfig()
        {
            // Messages
            Config["NoPermission"] = NoPermission;
            Config["NoSignsFound"] = NoSignsFound;

            // Settings
            Config["ChatCommand"] = ChatCommand;
            Config["EventId"] = EventId;
            Config["LockSigns"] = LockSigns;

            SaveConfig();
        }

        #endregion

        #region Data Storage

        static EventData eventData;

        class EventData
        {
            public Dictionary<uint, SignData> Signs = new Dictionary<uint, SignData>();
            public string EventId;
            public string Prefix;
            public string Title;
            public string Description;
            public string StartDate;
            public string EndDate;
            public string Currency;
            public string Symbol;
            public int Contributions;
            public double Total;
            public string Goal;
            public int Percentage;

            public EventData()
            {
            }
        }

        class SignData
        {
            public uint TextureId;
            /*public string SignColor;
            public string TextColor;
            public int Width;
            public int Height;*/

            public SignData()
            {
            }

            public SignData(Signage sign)
            {
                TextureId = sign.textureID;
                /*SignColor = "ffffff";
                TextColor = "000000";
                Width = 0;
                Height = 0;*/
            }
        }

        #endregion

        #region General Setup

        void Loaded()
        {
            LoadDefaultConfig();
            eventData = Interface.Oxide.DataFileSystem.ReadObject<EventData>(Name);

            permission.RegisterPermission("charitysigns.admin", this);
            cmd.AddChatCommand(ChatCommand, this, "SignChatCmd");
        }

        void OnServerInitialized()
        {
            webObject = new GameObject("WebObject");
            uWeb = webObject.AddComponent<UnityWeb>();

            timer.Repeat(1f, 0, GetEventInfo);
        }

        #endregion

        #region Unity WWW

        GameObject webObject;
        UnityWeb uWeb;

        class QueueItem
        {
            public string url;
            public Signage sign;
            public BasePlayer sender;

            public QueueItem(string ur, BasePlayer se, Signage si)
            {
                url = ur;
                sender = se;
                sign = si;
            }
        }

        class UnityWeb : MonoBehaviour
        {
            const int MaxActiveLoads = 3;
            static readonly List<QueueItem> QueueList = new List<QueueItem>();
            static byte activeLoads;

            public void Add(string url, BasePlayer player, Signage sign)
            {
                QueueList.Add(new QueueItem(url, player, sign));
                if (activeLoads < MaxActiveLoads) Next();
            }

            void Next()
            {
                activeLoads++;
                var qi = QueueList[0];
                QueueList.RemoveAt(0);
                var www = new WWW(qi.url);
                StartCoroutine(WaitForRequest(www, qi));
            }

            IEnumerator WaitForRequest(WWW www, QueueItem info)
            {
                yield return www;
                var player = info.sender;

                if (www.error == null)
                {
                    var sign = info.sign;
                    if (sign.textureID > 0U) FileStorage.server.Remove(sign.textureID, FileStorage.Type.png, sign.net.ID);
                    sign.textureID = FileStorage.server.Store(www.bytes, FileStorage.Type.png, sign.net.ID, 0U);
                    sign.SendNetworkUpdate();
                    player.SendMessage("Charity sign created!");
                }
                else
                {
                    player.ChatMessage(www.error);
                }

                activeLoads--;
                if (QueueList.Count > 0) Next();
            }
        }

        #endregion

        readonly WebRequests request = Interface.Oxide.GetLibrary<WebRequests>("WebRequests");

        void GetEventInfo()
        {
            var url = $"https://donate.childsplaycharity.org/api/event/{EventId}/json/";

            request.EnqueueGet(url, (code, response) =>
            {
                if (code != 200 || response == null)
                {
                    Puts("Checking for event info failed! (" + code + ")");
                    return;
                }

                // Extract the event information
                var json = JObject.Parse(response);
                eventData.EventId = EventId;
                eventData.Prefix = (string) json["prefix"];
                eventData.Title = (string) json["title"];
                eventData.Description = (string) json["description"];
                eventData.StartDate = (string) json["start_date"]; // TODO: Convert to DateTime?
                eventData.EndDate = (string) json["end_date"]; // TODO: Convert to DateTime?
                eventData.Currency = (string) json["currency"];
                eventData.Symbol = ((string) json["symbol"]).Split(' ').Last();
                eventData.Contributions = (int) json["contributions"];
                eventData.Total = Math.Floor((double) json["total"]);
                eventData.Goal = (string) json["goal"]; // TODO: Somehow handle "" string if not set
                eventData.Percentage = (int) json["percentage"];

                // Store updated data
                Interface.Oxide.DataFileSystem.WriteObject(Name, eventData);

                // Update signs
                UpdateSigns();
            }, this);
        }

        string SignImage()
        {
            var text = $"{eventData.Contributions}+contributions+{eventData.Symbol}{eventData.Total}+raised!";
            const string signColor = "ffffff"; // TODO: Move to config
            const string textColor = "000000"; // TODO: Move to config
            const int textSize = 40; // TODO: Move to config
            const int width = 350; // TODO: Move to config
            const int height = 150; // TODO: Move to config

            return $"http://placeholdit.imgix.net/~text?bg={signColor}&txtclr={textColor}&txtsize={textSize}&txt={text}&w={width}&h={height}";
        }

        void CreateSign(BasePlayer player, Signage sign)
        {
            uWeb.Add(SignImage(), player, sign);

            // Prevent player edits
            if (!LockSigns) return;
            sign.SetFlag(BaseEntity.Flags.Locked, true);
            sign.SendNetworkUpdate();
        }

        #region Chat Command

        void SignChatCmd(BasePlayer player)
        {
            if (!HasPermission(player, "charitysigns.admin"))
            {
                PrintToChat(player, NoPermission);
                return;
            }

            // Check for sign to use
            RaycastHit hit;
            Signage sign = null;
            if (Physics.Raycast(player.eyes.HeadRay(), out hit, 2f)) sign = hit.transform.GetComponentInParent<Signage>();

            if (sign == null)
            {
                PrintToChat(player, NoSignsFound);
                return;
            }

            if (eventData.Signs.ContainsKey(sign.net.ID))
            {
                PrintToChat(player, "Already a charity sign!");
                return;
            }

            CreateSign(player, sign);

            // Store updated data
            var info = new SignData(sign);
            eventData.Signs.Add(sign.net.ID, info);
            Interface.Oxide.DataFileSystem.WriteObject(Name, eventData);
        }

        #endregion

        #region Sign Updating

        void UpdateSigns()
        {
            var signs = 0;
            foreach (var id in eventData.Signs)
            {
                // Find sign entity
                var sign = BaseNetworkable.serverEntities.Find(id.Key) as Signage;

                // Create sign image
                if (sign == null) continue;
                CreateSign(null, sign);
                signs++;
            }

            //Puts($"{eventData.Symbol}{eventData.Total} in contributions, {signs} signs updated!");
        }

        #endregion

        #region Sign Cleanup

        void OnEntityDeath(BaseEntity entity)
        {
            // Remove data for destroyed sign
            var signage = entity as Signage;
            if (signage) eventData.Signs.Remove(signage.net.ID);

            // Store updated data
            Interface.Oxide.DataFileSystem.WriteObject(Name, eventData);
        }

        void Unload() => UnityEngine.Object.Destroy(webObject);

        #endregion

        #region Helper Methods

        T GetConfig<T>(string name, T defaultValue)
        {
            if (Config[name] == null) return defaultValue;
            return (T)Convert.ChangeType(Config[name], typeof(T));
        }

        bool HasPermission(BasePlayer player, string perm) => permission.UserHasPermission(player.UserIDString, perm);

        #endregion
    }
}
