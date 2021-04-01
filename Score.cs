
/*
 ########### README ####################################################
 #                                                                     #
 #   1. If you found a bug, please report them to developer!           #
 #   2. Don't edit that file (edit files only in CONFIG/LANG/DATA)     #
 #                                                                     #
 ########### CONTACT INFORMATION #######################################
 #                                                                     #
 #   Website: https://rustworkshop.space/                              #
 #   Discord: Orange#0900                                              #
 #   Email: official.rustworkshop@gmail.com                            #
 #                                                                     #
 #######################################################################
*/

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    // Creation date: 21-09-2020
    // Last update date: 19-10-2020 
    [Info("Score", "Orange", "1.0.5")]
    [Description("https://rustworkshop.space/resources/score.220/")]
    public class Score : RustPlugin
    {
        #region Vars

        private Dictionary<string, double> scoreDictionary = new Dictionary<string, double>();
        private Dictionary<uint, BasePlayer> helicopterAttackers = new Dictionary<uint, BasePlayer>();
        private HashSet<uint> lootedCrates = new HashSet<uint>();
        private const string command = "score";
        private const string elemMain = "visualscore.main";
        private string uiJson;

        #endregion
         
        #region Oxide Hooks

        private void Init()
        {
            cmd.AddConsoleCommand(command, this, nameof(cmdControlConsole));
            BuildUI();
        }

        private void OnServerInitialized()
        {
            RefreshUI();
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, elemMain);
            }
            
            SaveScore();
        }
        
        private void OnEntityTakeDamage(BaseHelicopter entity, HitInfo info)
        {
            if (info == null)
            {
                return;
            }
            
            var player = info.InitiatorPlayer ?? entity.lastAttacker as BasePlayer;
            if (player != null && entity.Health() > 1f)
            {
                if (helicopterAttackers.TryAdd(entity.net.ID, player) == false)
                {
                    helicopterAttackers[entity.net.ID] = player;
                }
            }
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (info == null || entity == info.Initiator)
            {
                return;
            }
           
            var initiatorPlayer = info.InitiatorPlayer ?? entity.lastAttacker as BasePlayer;
            if (initiatorPlayer != null)
            {
                if (entity.OwnerID == initiatorPlayer.userID)
                {
                    return;
                }

                if (initiatorPlayer.currentTeam > 0)
                {
                    var team = initiatorPlayer.Team;
                    var victimPlayer = entity as BasePlayer;
                    if (victimPlayer != null)
                    {
                        if (InSameClan(initiatorPlayer, victimPlayer))
                        {
                            return;
                        }

                        if (initiatorPlayer.currentTeam == victimPlayer.currentTeam)
                        {
                            return;
                        }
                    }
                    
                    if (entity.OwnerID > 0 && team.members.Contains(entity.OwnerID) == true)
                    {
                        return;
                    }
                }
            }
            
            if (entity is BaseHelicopter)
            {
                helicopterAttackers.TryGetValue(entity.net.ID, out initiatorPlayer);
            }
            
            var name = entity.ShortPrefabName;
            
            NextTick(() =>
            {
                if (entity == null || entity.IsDead() == true)
                {
                    AddScore(initiatorPlayer, name);
                }
            });
        }
        
        private void OnLootEntity(BasePlayer player, LootContainer entity)
        {
            if (lootedCrates.Contains(entity.net.ID) == false)
            {
                lootedCrates.Add(entity.net.ID);
                AddScore(player, entity.ShortPrefabName);
            }
        }
        
        private void OnLootSpawn(LootContainer container)
        {
            lootedCrates.RemoveWhere(x => x == container.net.ID);
        }

        #endregion

        #region Commands

        private void cmdControlConsole(ConsoleSystem.Arg arg)
        {
            if (arg.IsAdmin == false)
            {
                SendReply(arg, "You don't have permission to do that!");
                return;
            }
            
            var args = arg.Args;
            if (args == null || args.Length == 0)
            {
                SendReply(arg, "Usage: score <add / remove / reset> <team name / player id / all>");
                return;
            }
            
            var action = args[0].ToLower();
            if (args.Length < 2)
            {
                SendReply(arg, "You need to define team name or player id!");
                return;
            }
            
            var key = args[1];
            if (string.Equals(key, "all", StringComparison.OrdinalIgnoreCase))
            {
                key = "*";
            }
            
            var amountDouble = 0.0d;
            var amountString = args.Length > 2 ? args[2] : "0";
            double.TryParse(amountString, out amountDouble);
            if (amountDouble < 0)
            {
                SendReply(arg, $"Wrong value for amount! '{amountString}'");
                return;
            }
            
            switch (action)
            {
                case "add":
                    AddScore(key, amountDouble);
                    SendReply(arg, $"Added {amountDouble} points to '{key}'");
                    break;
                
                case "remove":
                    RemoveScore(key, amountDouble);
                    SendReply(arg, $"Removed {amountDouble} points to '{key}'");
                    break;
                
                case "clear":
                case "reset":
                    ResetScore(key);           
                    SendReply(arg, $"Removed all points of '{key}'");
                    break;
            }
        }

        #endregion

        #region Core

        private void AddScore(BasePlayer player, string prefab)
        {
            if (player.IsValid() == false || player.userID.IsSteamId() == false)
            {
                return;
            }

            var key = player.UserIDString;
            var clan = GetPlayerClan(player);
            if (string.IsNullOrEmpty(clan) == false)
            {
                key = clan;
            }
            else
            {
                if (player.currentTeam > 0)
                {
                    var team = player.Team;
                    key = team.teamName ?? team.teamLeader.ToString();
                }
            }
            
            var amount = 0d;
            if (config.pointsByShortname.TryGetValue(prefab, out amount) == false)
            {
                return;
            }
            
            AddScore(key, amount);
            var current = 0d;
            scoreDictionary.TryGetValue(key, out current);
             
            Message.Send(player.IPlayer, MessageKey.PointsEarnedChat, "{amount}", amount, "{current}", current);
            var messageUI = Message.GetMessage(MessageKey.PointsEarnedUI, player.UserIDString, "{amount}", amount, "{current}", current);
            if (messageUI.Length > 0)
            {
                player.SendConsoleCommand($"gametip.showtoast 0 \"{messageUI}\"");
            }
        }

        private void AddScore(string key, double amount)
        {
            if (amount < 0)
            {
                return;
            }

            if (key == "*")
            {
                foreach (var pair in scoreDictionary.ToArray())
                {
                    scoreDictionary[pair.Key] += amount;
                }
            }
            else
            {
                if (scoreDictionary.ContainsKey(key) == true)
                {
                    scoreDictionary[key] += amount;
                }
                else
                {
                    scoreDictionary.Add(key, amount);
                }
            }
            
            RefreshUI();
        }

        private void RemoveScore(string key, double amount)
        {
            if (amount < 0)
            {
                return;
            }

            if (key == "*")
            {
                foreach (var pair in scoreDictionary.ToArray())
                {
                    scoreDictionary[pair.Key] -= amount;
                }
            }
            else
            {
                if (scoreDictionary.ContainsKey(key) == true)
                {
                    scoreDictionary[key] -= amount;
                }
            }
            
            RefreshUI();
        }

        private void ResetScore(string key)
        {
            if (key == "*")
            {
                SaveScore();
                scoreDictionary.Clear();
                helicopterAttackers.Clear();
                lootedCrates.Clear();
            }
            else
            {
                if (scoreDictionary.ContainsKey(key) == true)
                {
                    scoreDictionary.Remove(key);
                }
            }
            
            RefreshUI();
        }

        private void SaveScore()
        {
            if (scoreDictionary.Count > 0)
            {
                var name = $"{Name}//{DateTime.UtcNow:dd-MM-yyyy_HH-mm-ss}";
                Interface.Oxide.DataFileSystem.WriteObject(name, scoreDictionary);
                Debug.LogWarning($"Saved score as {name} with {scoreDictionary.Count} values");
            }
        }

        #endregion

        #region UI

        private void BuildUI()
        {
            var container = new CuiElementContainer();
            container.Add(new CuiButton
            { 
                Button =
                {
                    Color  = "1 0 0 0",
                    Close = elemMain,
                },
                Text =
                {
                    Text = "%TEXT%",
                    Align = TextAnchor.UpperLeft
                },
                RectTransform =
                {
                    AnchorMin = "1 1",
                    AnchorMax = "1 1",
                    OffsetMin = "-300 -200",
                    OffsetMax = "0 0"
                }
            }, "Under", elemMain);
            uiJson = container.ToString();
        }

        private void RefreshUI()
        {
            var text = string.Empty;
            var values = string.Empty;
            var listed = 0; 
            var ordered = scoreDictionary.OrderByDescending(x => x.Value);
            if (scoreDictionary.Count == 0)
            {
                text = Message.GetMessage(MessageKey.ScoreEmpty);
            }
            else
            {
                foreach (var pair in ordered)
                {
                    if (listed >= config.valuesInTop)
                    {
                        break;
                    }
                    
                    var name = pair.Key;
                    if (name.StartsWith("765"))
                    {
                        name = BasePlayer.Find(name)?.displayName ?? name;
                    }

                    var msg = Message.GetMessage(MessageKey.ScoreEntry);
                    var format = Message.FormattedMessage(msg, "{rank}", ++listed, "{name}", name, "{points}", pair.Value);
                    values += format;
                } 
                
                text = Message.GetMessage(MessageKey.ScoreList);
                text = Message.FormattedMessage(text, "{list}", values);
            }
            
            var ui = uiJson.Replace("%TEXT%", text);
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, elemMain);
                CuiHelper.AddUi(player, ui);
            }
        }

        #endregion
        
        #region Configuration | 09.2020

        private static ConfigData config = new ConfigData();

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Amount of values in scoreboard")]
            public int valuesInTop = 5;
            
            [JsonProperty(PropertyName = "Points")]
            public Dictionary<string, double> pointsByShortname = new Dictionary<string, double>();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<ConfigData>();
                if (config == null)
                {
                    LoadDefaultConfig();
                }
            }
            catch
            {
                for (var i = 0; i < 3; i++)
                {
                    PrintError("Configuration file is corrupt! Check your config file at https://jsonlint.com/");
                }
                
                LoadDefaultConfig();
                return;
            }

            ValidateConfig();
            SaveConfig();
        }

        private void ValidateConfig()
        {
            if (Interface.Oxide.CallHook("OnConfigValidate") != null)
            {
                PrintWarning("Using default configuration...");
                config = new ConfigData();
            }

            if (config.pointsByShortname.Count == 0)
            {
                config.pointsByShortname = new Dictionary<string, double>
                {
                    {"crate_normal", 2},
                    {"crate_normal2", 5},
                    {"player", 2},
                    {"cupboard.tool.deployed", 25},
                    {"patrolhelicopter", 15},
                };
            }
        }

        protected override void LoadDefaultConfig()
        {
            config = new ConfigData();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }

        #endregion
        
        #region Language System v2

        protected override void LoadDefaultMessages()
        {
            Message.Register(lang, this);
        }
        
        private enum MessageKey
        { 
            PointsEarnedChat,
            PointsEarnedUI,
            ScoreList, 
            ScoreEmpty,
            ScoreEntry, 
        } 

        private class Message   
        {
            private static Dictionary<MessageKey, object> langMessages = new Dictionary<MessageKey, object>
            {
                {MessageKey.PointsEarnedChat, "You earned <color=#ffff00>+{amount}</color> points! Now you have <color=#ffff00>{current}</color> points at total"},
                {MessageKey.PointsEarnedUI, "+{amount} points"},
                {MessageKey.ScoreList, "<size=20>Score:\n{list}</size>"},
                {MessageKey.ScoreEmpty, "<size=20>Score:\n   There are no score yet!\n   Earn points to reach top!</size>"},
                {MessageKey.ScoreEntry, "{rank}. {name} <color=#ffff00>({points})</color>\n"}
            };
             
            public enum Type
            {
                Normal,
                Warning,
                Error
            }
           
            private static RustPlugin plugin;
            private static Lang lang;

            public static void Register(Lang v1, RustPlugin v2)
            { 
                lang = v1;
                plugin = v2;

                var dictionary = new Dictionary<string, string>();
                foreach (var pair in langMessages)
                {
                    var key = pair.Key.ToString();
                    var value = pair.Value.ToString();
                    dictionary.TryAdd(key, value);
                }

                lang.RegisterMessages(dictionary, plugin);
            }

            public static void Dispose()
            {
                lang = null;
                plugin = null;
            }

            public static void Console(string message, Type type = Type.Normal)
            {
                message = $"[{plugin.Name}] {message}";
                switch (type)
                {
                    case Type.Normal:
                        Debug.Log(message);
                        break;
                    
                    case Type.Warning:
                        Debug.LogWarning(message);
                        break;
                    
                    case Type.Error:
                        Debug.LogError(message);
                        break;
                }
            }

            public static void Send(object receiver, string message, params object[] args)
            {
                message = FormattedMessage(message, args);
                SendMessage(receiver, message);
            }

            public static void Send(object receiver, MessageKey key, params object[] args)
            {
                var userID = (receiver as BasePlayer)?.UserIDString;
                var message = GetMessage(key, userID, args);
                SendMessage(receiver, message);
            }
            
            public static void Broadcast(string message, params object[] args)
            {
                foreach (var player in BasePlayer.activePlayerList)
                {
                    message = FormattedMessage(message, args);
                    SendMessage(player.IPlayer, message);
                }
            }
            
            public static void Broadcast(MessageKey key, params object[] args)
            {
                foreach (var player in BasePlayer.activePlayerList)
                {
                    var message = GetMessage(key, player.UserIDString);
                    SendMessage(player.IPlayer, message);
                }
            }
            
            public static string GetMessage(MessageKey key, string playerID = null, params object[] args)
            {
                var keyString = key.ToString();
                var message = lang.GetMessage(keyString, plugin, playerID);
                if (message == keyString)
                {
                    return $"{keyString} is not defined in plugin!";
                }

                return FormattedMessage(message, args);
            }
            
            public static string FormattedMessage(string message, params object[] args)
            {
                if (args != null && args.Length > 0)
                {
                    var organized = OrganizeArgs(args);
                    return ReplaceArgs(message, organized);
                }

                return message;
            }
            
            private static void SendMessage(object receiver, object message)
            {
                if (receiver == null || message == null)
                {
                    return;
                }

                var messageString = message.ToString();
                if (string.IsNullOrEmpty(messageString))
                {
                    return;
                }

                var console = receiver as ConsoleSystem.Arg;
                if (console != null)
                {
                    // TODO: Finish me!
                    return;
                }

                var iPlayer = receiver as IPlayer ?? (receiver as BasePlayer)?.IPlayer;
                if (iPlayer != null)
                {
                    iPlayer.Message(messageString);
                    return;
                }
            }

            private static Dictionary<string, object> OrganizeArgs(object[] args)
            {
                var dic = new Dictionary<string, object>();
                for (var i = 0; i < args.Length; i += 2)
                {
                    var value = args[i].ToString();
                    var nextValue = i + 1 < args.Length ? args[i + 1] : null;
                    dic.TryAdd(value, nextValue);
                }

                return dic;
            }

            private static string ReplaceArgs(string message, Dictionary<string, object> args)
            {
                if (args == null || args.Count < 1)
                {
                    return message;
                }

                foreach (var pair in args)
                {
                    var s0 = "{" + pair.Key + "}";
                    var s1 = pair.Key;
                    var s2 = pair.Value != null ? pair.Value.ToString() : "null";
                    message = message.Replace(s0, s2, StringComparison.InvariantCultureIgnoreCase);
                    message = message.Replace(s1, s2, StringComparison.InvariantCultureIgnoreCase);
                }

                return message;
            }
        }

        #endregion
        
        #region Clans Support
        
        [PluginReference] private Plugin Clans;
        
        private string GetPlayerClan(BasePlayer player)
        {
            return Clans?.Call<string>("GetClanOf", player.userID);
        }

        private string GetPlayerClan(ulong playerID)
        {
            return Clans?.Call<string>("GetClanOf", playerID);
        }
        
        private string GetPlayerClan(string playerID)
        {
            return Clans?.Call<string>("GetClanOf", playerID);
        }

        private bool InSameClan(BasePlayer player1, BasePlayer player2)
        {
            var clan1 = GetPlayerClan(player1);
            var clan2 = GetPlayerClan(player2);
            return string.IsNullOrEmpty(clan1) == false && string.Equals(clan1, clan2);
        }
        
        private bool InSameClan(ulong player1, ulong player2)
        {
            var clan1 = GetPlayerClan(player1);
            var clan2 = GetPlayerClan(player2);
            return string.IsNullOrEmpty(clan1) == false && string.Equals(clan1, clan2);
        }

        private bool InSameClan(BasePlayer player1, ulong player2)
        {
            var clan1 = GetPlayerClan(player1);
            var clan2 = GetPlayerClan(player2);
            return string.IsNullOrEmpty(clan1) == false && string.Equals(clan1, clan2);
        }
        
        private bool InSameClan(ulong player1, BasePlayer player2)
        {
            var clan1 = GetPlayerClan(player1);
            var clan2 = GetPlayerClan(player2);
            return string.IsNullOrEmpty(clan1) == false && string.Equals(clan1, clan2);
        }

        #endregion
    }
}
