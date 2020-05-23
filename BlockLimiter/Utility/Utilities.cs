﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Timers;
using BlockLimiter.Patch;
using BlockLimiter.Settings;
using Sandbox.Definitions;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using Torch;
using Torch.Managers;
using Torch.Mod;
using Torch.Mod.Messages;
using Torch.Utils;
using VRage;
using VRage.Collections;
using VRage.Dedicated.Configurator;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Network;
using VRage.Utils;

namespace BlockLimiter.Utility
{
    public static class Utilities
    {
        [ReflectedStaticMethod(Type = typeof(MyCubeBuilder), Name = "SpawnGridReply", OverrideTypes = new []{typeof(bool), typeof(ulong)})]
        private static Action<bool, ulong> _spawnGridReply;
        public static string GetPlayerNameFromSteamId(ulong steamId)
        {
            var pid = MySession.Static.Players.TryGetIdentityId(steamId);
            if (pid == 0)
                return null;
            var id = MySession.Static.Players.TryGetIdentity(pid);
            return id?.DisplayName;
        }


        public static long GetPlayerIdFromSteamId(ulong steamId)
        {
            return MySession.Static.Players.TryGetIdentityId(steamId);
        }

        public static ulong GetSteamIdFromPlayerId(long playerId)
        {
            return MySession.Static.Players.TryGetSteamId(playerId);
        }

        public static void SendFailSound(ulong target)
        {
            _spawnGridReply(false, target);
        }

        /// <summary>
        /// Gets the entity with name or ID given
        /// </summary>
        /// <param name="nameOrId"></param>
        /// <param name="entity"></param>
        /// <returns></returns>
        public static bool TryGetEntityByNameOrId(string nameOrId, out IMyEntity entity)
        {
            
            if (long.TryParse(nameOrId, out var id))
                return MyAPIGateway.Entities.TryGetEntityById(id, out entity);


            foreach (var ent in MyEntities.GetEntities())
            {
                if (string.IsNullOrEmpty(ent.DisplayName)) continue;
                if (!ent.DisplayName.Equals(nameOrId)) continue;
                entity = ent;
                return true;
            }
            
            entity = null;
            return false;
        }

        public static bool TryGetPlayerByNameOrId(string nameOrId, out MyIdentity identity)
        {
            identity = null;
            if (ulong.TryParse(nameOrId, out var steamId))
            {
                var id0 = MySession.Static.Players.TryGetIdentityId(steamId);
                identity = MySession.Static.Players.TryGetIdentity(id0);
                
                return identity != null;
            }

            if (long.TryParse(nameOrId, out var id1))
            {
                identity = MySession.Static.Players.TryGetIdentity(id1);

                return identity != null;
            }

            foreach (var id3 in MySession.Static.Players.GetAllIdentities())
            {
                if (string.IsNullOrEmpty(id3.DisplayName) || !id3.DisplayName.Equals(nameOrId)) continue;
                identity = id3;
                return identity != null;
            }

            identity = null;
            return false;

        }

        public static bool TryGetPlayerById(long id, out MyPlayer player)
        {
            player = null;
            if (id == 0) return false;
            foreach (var ident in MySession.Static.Players.GetOnlinePlayers())
            {
                if (ident.Identity.IdentityId != id) continue;
                player = ident;
                return true;
            }

            return false;
        }

        public static long NextInt64(Random rnd)
        {
            var buffer = new byte[sizeof(long)];
            rnd.NextBytes(buffer);
            return BitConverter.ToInt64(buffer, 0);
        }

        public static void ValidationFailed()
        {
            ((MyMultiplayerServerBase)MyMultiplayer.Static).ValidationFailed(MyEventContext.Current.Sender.Value);
        }
        
        private static MyConcurrentDictionary<MyStringHash, MyCubeBlockDefinition> _defCache = new MyConcurrentDictionary<MyStringHash, MyCubeBlockDefinition>();

        public static MyCubeBlockDefinition GetDefinition(MyObjectBuilder_CubeBlock block)
        {
            if (_defCache.TryGetValue(block.SubtypeId, out var def))
                return def;

            var blockDefinition = MyDefinitionManager.Static.GetCubeBlockDefinition(block);
            _defCache[block.SubtypeId] = blockDefinition;
            return blockDefinition;
        }


        public static bool IsExcepted(long obj, List<string> exceptions)
        {
            var excepted = false;

            var allExceptions = new HashSet<string>();
            allExceptions.UnionWith(exceptions);
            allExceptions.UnionWith(BlockLimiterConfig.Instance.GeneralException);

            if (allExceptions.Contains(obj.ToString()))
            {
                return true;
            }

            var faction = MySession.Static.Factions.TryGetFactionById(obj);

            if (faction != null)
            {
                return allExceptions.Contains(faction.Tag);
            }

            var id = MySession.Static.Players.TryGetIdentity(obj);
            if (id != null)
            {
                var steamId = MySession.Static.Players.TryGetSteamId(obj);
                return (allExceptions.Contains(id.DisplayName)) || steamId > 0 &&
                       allExceptions.Contains(steamId.ToString());
            }
            

            if (!GridCache.TryGetGridById(obj, out var grid)) return false;
            if (allExceptions.Contains(grid.DisplayName)) excepted = true;
            if (grid.BigOwners.Any(x => allExceptions.Contains(x.ToString()))) return true;

            return excepted;
        }



        #region Limits

        public static void UpdateLimits(bool useVanilla, out HashSet<LimitItem> items)
        {
            items = new HashSet<LimitItem>();
            if (useVanilla && BlockLimiter.Instance.VanillaLimits.Count > 0)
            {
                items.UnionWith(BlockLimiter.Instance.VanillaLimits);
            }

            items.UnionWith(BlockLimiterConfig.Instance.LimitItems);
        }

        public static StringBuilder GetLimit(long playerId)
        {
            
            var sb = new StringBuilder();
            if (playerId == 0)
            {
                sb.AppendLine("Player not found");
                return sb;
            }

            var limitItems = BlockLimiterConfig.Instance.AllLimits;
            
            if (limitItems.Count < 1)
            {
                sb.AppendLine("No limit found");
                return sb;
            }
            

            var playerFaction = MySession.Static.Factions.GetPlayerFaction(playerId);


            
            foreach (var item in limitItems)
            {
                if (item.BlockList.Count == 0 || item.FoundEntities.Count == 0) continue;
                
                var itemName = string.IsNullOrEmpty(item.Name) ? item.BlockList.FirstOrDefault() : item.Name;
                sb.AppendLine();
                sb.AppendLine($"----->{itemName}<-----");

                if (item.LimitPlayers && item.FoundEntities.TryGetValue(playerId, out var pCount))
                {
                    if (pCount < 1) continue;
                    sb.AppendLine($"Player Limit = {pCount}/{item.Limit}");
                }

                if (item.LimitFaction && playerFaction != null &&
                    item.FoundEntities.TryGetValue(playerFaction.FactionId, out var fCount))
                {
                    if (fCount < 1) continue;
                    sb.AppendLine($"Faction Limit = {fCount}/{item.Limit} ");
                }

                if (!item.LimitGrids || (!item.FoundEntities.Any(x =>
                    GridCache.TryGetGridById(x.Key, out var grid) && grid.BigOwners.Contains(playerId)))) continue;

                sb.AppendLine("Grid Limits:");

                foreach (var (id,gCount) in item.FoundEntities)
                {
                    if (!GridCache.TryGetGridById(id, out var grid) || !grid.BigOwners.Contains(playerId)) continue;
                    if (gCount < 1) continue;
                    sb.AppendLine($"->{grid.DisplayName} = {gCount} / {item.Limit}");
                }
            }
            

            return sb;

        }

        public static bool TryGetAimedTextPanel(IMyPlayer player, out MyTextPanel textpanel)
        {
            if (player.Character is MyCharacter character && character.AimedGrid != 0)
            {
                var aimedBlock = (MyEntities.GetEntityById(character.AimedGrid) as MyCubeGrid)
                    ?.GetCubeBlock(character.AimedBlock);
                if (aimedBlock?.BlockDefinition is MyTextPanelDefinition)
                {
                    textpanel = aimedBlock.FatBlock as MyTextPanel; 
                    return true;
                }
            }
            textpanel = null;
            return false;
        }

        public static bool CheckPermissionTextPanel(IMyPlayer player, MyCubeBlock textpanel)
        {
            if (textpanel.OwnerId == 0) return true;

            var relation = textpanel.GetUserRelationToOwner(player.IdentityId);
            if (relation != MyRelationsBetweenPlayerAndBlock.Owner &&
                relation != MyRelationsBetweenPlayerAndBlock.FactionShare) return false;

            return true;
        }

        #endregion


    }
}
