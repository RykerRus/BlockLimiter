﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Authentication.ExtendedProtection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using BlockLimiter.ProcessHandlers;
using BlockLimiter.Settings;
using BlockLimiter.Utility;
using NLog;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using Torch;
using Torch.Managers;
using Torch.Managers.PatchManager;
using Torch.Mod;
using Torch.Mod.Messages;
using VRage.Game;
using VRage.Game.Components;
using VRage.Network;

namespace BlockLimiter.Patch
{
    [PatchShim]
    public static class GridSpawnPatch
    {
        private static MethodInfo _showPasteFailed =
            typeof(MyCubeGrid).GetMethod("ShowPasteFailedOperation", BindingFlags.Static | BindingFlags.Public);
        public static void Patch(PatchContext ctx)
        {
            ctx.GetPattern(typeof(MyCubeBuilder).GetMethod("RequestGridSpawn", BindingFlags.NonPublic | BindingFlags.Static))
                .Prefixes.Add(typeof(GridSpawnPatch).GetMethod(nameof(Prefix),BindingFlags.NonPublic|BindingFlags.Static));
            
            ctx.GetPattern(typeof(MyCubeGrid).GetMethod("TryPasteGrid_Implementation",  BindingFlags.Public  |  BindingFlags.Static)).
                Prefixes.Add(typeof(GridSpawnPatch).GetMethod(nameof(AttemptSpawn), BindingFlags.Static |  BindingFlags.NonPublic));

        }

        /// <summary>
        /// Decides if grid being spawned is permitted
        /// </summary>
        /// <param name="entities"></param>
        /// <returns></returns>
        private static bool AttemptSpawn(List<MyObjectBuilder_CubeGrid> entities)
        {
            if (!BlockLimiterConfig.Instance.EnableLimits) return true;

            var grids = entities;

            if (grids.Count == 0) return false;

            var remoteUserId = MyEventContext.Current.Sender.Value;

            if (remoteUserId == 0) return true;

            var playerId = Utilities.GetPlayerIdFromSteamId(remoteUserId);

            if (grids.All(x => Grid.CanSpawn(x, playerId)))
            {
                var blocks = grids.SelectMany(x => x.CubeBlocks).ToList();
                var count = 0;
                foreach (var block in blocks)
                {
                    //Block.IncreaseCount(Utilities.GetDefinition(block),playerId,1);
                    count++;
                }
                BlockLimiter.Instance.Log.Warn($"{count} blocks given spawned for {playerId}");
                return true;
            }
            if (BlockLimiterConfig.Instance.EnableLog)
                BlockLimiter.Instance.Log.Info($"Blocked {remoteUserId} from spawning a grid");
            
            MyVisualScriptLogicProvider.SendChatMessage($"{BlockLimiterConfig.Instance.DenyMessage}", BlockLimiterConfig.Instance.ServerName, playerId, MyFontEnum.Red);
            NetworkManager.RaiseStaticEvent(_showPasteFailed);
            Utilities.ValidationFailed();
            Utilities.SendFailSound(remoteUserId);
            return false;
        }

        /// <summary>
        /// Decides if a block about to be spawned is permitted by the player
        /// </summary>
        /// <param name="definition"></param>
        /// <returns></returns>
        private static bool Prefix(DefinitionIdBlit definition)
        {
            if (!BlockLimiterConfig.Instance.EnableLimits) return true;
            
            var block = MyDefinitionManager.Static.GetCubeBlockDefinition(definition);
            
            if (block == null)
            {
                return true;
            }
            
            var remoteUserId = MyEventContext.Current.Sender.Value;
            var player = MySession.Static.Players.TryGetPlayerBySteamId(remoteUserId);
            var playerId = player.Identity.IdentityId;

            if (Block.IsWithinLimits(block, playerId, null))
            {
                return true;
            }


            var b = block.BlockPairName;
            var p = player.DisplayName;
            if (BlockLimiterConfig.Instance.EnableLog)
                BlockLimiter.Instance.Log.Info($"Blocked {p} from placing a {b}");
            
            //ModCommunication.SendMessageTo(new NotificationMessage($"You've reach your limit for {b}",5000,MyFontEnum.Red),remoteUserId );
            var msg = BlockLimiterConfig.Instance.DenyMessage.Replace("{BN}", $"{b}");
            MyVisualScriptLogicProvider.SendChatMessage($"{msg}", BlockLimiterConfig.Instance.ServerName, playerId, MyFontEnum.Red);

            Utilities.SendFailSound(remoteUserId);
            Utilities.ValidationFailed();

            return false;
        }


    }
}
