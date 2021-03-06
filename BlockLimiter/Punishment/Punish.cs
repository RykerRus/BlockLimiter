using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlockLimiter.ProcessHandlers;
using BlockLimiter.Settings;
using BlockLimiter.Utility;
using NLog;
using Sandbox;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using VRage.Collections;
using VRage.Game;

namespace BlockLimiter.Punishment
{
    public class Punish : ProcessHandlerBase
    {
        private static readonly Logger Log = BlockLimiter.Instance.Log;
        private readonly HashSet<MySlimBlock> _blockCache = new HashSet<MySlimBlock>();

        public override int GetUpdateResolution()
        {
            return Math.Max(BlockLimiterConfig.Instance.PunishInterval,1) * 1000;
        }
        public override void Handle()
        {
            if (!BlockLimiterConfig.Instance.EnableLimits)return;
            var limitItems = BlockLimiterConfig.Instance.AllLimits;
            

            if (!limitItems.Any())
            {
                return;
            }
            

            _blockCache.Clear();
            GridCache.GetBlocks(_blockCache);


            if (!_blockCache.Any())
            {
                return;
            }
            
            var removeBlocks = new MyConcurrentDictionary<MySlimBlock,LimitItem.PunishmentType>();

            var punishCount = 0;
            var blocks = _blockCache.ToList();

            foreach (var item in limitItems)
            {
                if (!item.FoundEntities.Any() ||
                    item.Punishment == LimitItem.PunishmentType.None) continue;
                
                foreach (var (id,count) in item.FoundEntities)
                {
                    if (id == 0 || Utilities.IsExcepted(id, item.Exceptions))
                    {
                        item.FoundEntities.Remove(id);
                        continue;
                    }

                    if (count <= item.Limit) continue;

                    for (var i = blocks.Count; i --> 0;)
                    {
                        var block = blocks[i];
                        if (!Block.IsMatch(block.BlockDefinition, item)) continue;

                        if (Math.Abs(punishCount - count) <= item.Limit)
                        {
                            break;
                        }

                        if (item.IgnoreNpcs)
                        {
                            if (MySession.Static.Players.IdentityIsNpc(block.FatBlock.BuiltBy) || MySession.Static.Players.IdentityIsNpc(block.FatBlock.OwnerId))

                            {
                                item.FoundEntities.Remove(id);
                                continue;
                            }
                        }

                        if (item.LimitGrids && block.CubeGrid.EntityId == id)
                        {
                            punishCount++;
                            removeBlocks[block] = item.Punishment;
                            continue;
                        }

                        if (item.LimitPlayers)
                        {
                            if (Block.IsOwner(block, id))
                            {
                                punishCount++;
                                removeBlocks[block] = item.Punishment;
                                continue;
                            }
                        }

                        if (!item.LimitFaction) continue;
                        var faction = MySession.Static.Factions.TryGetFactionById(id);
                        if (faction == null || !block.FatBlock.GetOwnerFactionTag().Equals(faction.Tag)) continue;
                        punishCount++;
                        removeBlocks[block] = item.Punishment;
                    }

                    

                }
                
            }
            
            _blockCache.Clear();

            
            if (!removeBlocks.Keys.Any())
            {
                return;
            }
            
            Task.Run(() =>
            {
                
                MySandboxGame.Static.Invoke(() =>
                {
                    
                    foreach (var (block, punishment) in removeBlocks)
                    {
                        try
                        {
                            switch (punishment)
                            {
                                case LimitItem.PunishmentType.DeleteBlock:
                                    if (BlockLimiterConfig.Instance.EnableLog)
                                        Log.Info(
                                        $"removed {block.BlockDefinition.BlockPairName} from {block.CubeGrid.DisplayName}");
                                    block.CubeGrid.RemoveBlock(block);
                                    continue;
                                case LimitItem.PunishmentType.ShutOffBlock:
                                    if (!(block.FatBlock is MyFunctionalBlock funcBlock) || funcBlock.Enabled == false) continue;
                                    if (BlockLimiterConfig.Instance.EnableLog)
                                        Log.Info(
                                        $"Turned off {block.BlockDefinition.BlockPairName} from {block.CubeGrid.DisplayName}");
                                    funcBlock.Enabled = false;
                                    continue;
                                case LimitItem.PunishmentType.Explode:
                                    if (BlockLimiterConfig.Instance.EnableLog)
                                        Log.Info(
                                        $"Destroyed {block.BlockDefinition.BlockPairName} from {block.CubeGrid.DisplayName}");
                                    block.DoDamage(block.BlockDefinition.MaxIntegrity * 10, MyDamageType.Explosion);
                                    continue;
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }

                        }
                        catch (Exception e)
                        {
                            if(BlockLimiterConfig.Instance.EnableLog)Log.Error(e);
                        }
                    }
                }, "BlockLimiter");
            });
        }

    }
}