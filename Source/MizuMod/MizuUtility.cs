﻿using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;

using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace MizuMod
{
    public static class MizuUtility
    {
        public static Thing TryFindBestWaterSourceFor(Pawn getter, Pawn eater, bool canUseInventory = true, bool allowForbidden = false, bool allowSociallyImproper = false)
        {
            // ドラッグ嫌いではない
            //    →ドラッグを許可
            bool allowDrug = !eater.IsTeetotaler();

            Thing inventoryThing = null;
            if (canUseInventory && getter.CanManipulate())
            {
                // 所持品から探すフラグON、取得者は操作が可能
                //   →所持品からベストな飲み物を探す(まともな水の範囲で)
                inventoryThing = MizuUtility.BestWaterInInventory(getter, WaterPreferability.NormalWater, WaterPreferability.ClearWater, 0f, allowDrug);
            }

            // 所持品から見つかり、取得者はプレイヤーではない
            //   →そのまま飲む
            // プレイヤーだった場合はマップ中の飲み物も探して、より適切なものを選ぶため保留
            if (inventoryThing != null)
            {
                if (getter.Faction != Faction.OfPlayer) return inventoryThing;

                if (inventoryThing.IsRotSoonForWater()) return inventoryThing;
            }

            // マップからベストな飲み物を探す
            Thing mapThing = MizuUtility.BestWaterSourceOnMap(getter, eater, WaterPreferability.ClearWater, allowForbidden, allowSociallyImproper);
            if (inventoryThing == null && mapThing == null)
            {
                // 所持品にまともな水なし、マップからいかなる水も見つけられない
                //   →ランクを落として所持品から探しなおす
                if (canUseInventory && getter.CanManipulate())
                {
                    // 見つかっても見つからなくてもその結果を返す
                    return MizuUtility.BestWaterInInventory(getter, WaterPreferability.SeaWater, WaterPreferability.ClearWater, 0f, allowDrug);
                }

                // 所持品から探せる状態ではない
                return null;
            }

            // 所持品にまともな水なし、マップから水が見つかった
            //   →マップの水を取得
            if (inventoryThing == null && mapThing != null) return mapThing;

            // 所持品からまともな水が見つかった、マップからはいかなる水も見つけられない
            //   →所持品の水を取得
            if (inventoryThing != null && mapThing == null) return inventoryThing;

            // 所持品からまともな水が、マップからは何らかの水が見つかった
            //   →どちらが良いか評価(スコアが高い方が良い)
            float scoreMapThing = MizuUtility.GetWaterItemScore(eater, mapThing, (float)(getter.Position - mapThing.Position).LengthManhattan, false);
            float scoreInventoryThing = MizuUtility.GetWaterItemScore(eater, inventoryThing, 0f, false);

            // 所持品アイテムは距離32相当のマイナス(所持品より備蓄をやや優先する)
            //   →水と食事の違いを考えて無効化してみる
            //scoreInventoryThing -= 32f;

            // マップの水のほうが高スコア
            if (scoreMapThing > scoreInventoryThing) return mapThing;

            // 所持品の水のほうが高スコア
            return inventoryThing;
        }

        public static Thing BestWaterInInventory(Pawn holder, WaterPreferability minWaterPref = WaterPreferability.SeaWater, WaterPreferability maxWaterPref = WaterPreferability.ClearWater, float minStackWaterAmount = 0.0f, bool allowDrug = false)
        {
            // 所持品から探すのに所持品オブジェクトなし
            if (holder == null || holder.inventory == null) return null;

            foreach (var thing in holder.inventory.innerContainer)
            {
                // 所持品をひとつずつチェック
                float waterAmount = thing.GetWaterAmount();
                WaterPreferability waterPreferability = thing.GetWaterPreferability();

                if (thing.CanGetWater() // 飲み物として飲めるもの
                    && thing.CanDrinkWaterNow() // 現在飲める状態にある
                    && waterPreferability >= minWaterPref && waterPreferability <= maxWaterPref // 品質が指定範囲内
                    && (allowDrug || !thing.def.IsDrug) // ドラッグ許可か、そもそもドラッグでない
                    && (waterAmount * thing.stackCount >= minStackWaterAmount)) // 水の量の最低値指定を満たしている
                {
                    return thing;
                }
            }

            // 条件に合うものが1個も見つからなかった
            return null;
        }

        public static Thing BestWaterSourceOnMap(Pawn getter, Pawn eater, WaterPreferability maxPref = WaterPreferability.ClearWater, bool allowDrug = false, bool allowForbidden = false, bool allowSociallyImproper = false)
        {
            if (!getter.CanManipulate() && getter != eater)
            {
                // 取得者は操作不可、取得者と摂取者が違う
                //   →マップから取得して持ち運ぶことができない
                //   →エラー
                Log.Error(string.Concat(new object[]
                {
                    getter,
                    " tried to find food to bring to ",
                    eater,
                    " but ",
                    getter,
                    " is incapable of Manipulation."
                }));
                return null;
            }

            Predicate<Thing> waterValidator = (t) =>
            {
                // 禁止されている＆禁止を無視して取得してはいけない
                if (!allowForbidden && t.IsForbidden(getter)) return false;

                // ドラッグ禁止＆対象はドラッグ
                if (!allowDrug && t.def.IsDrug) return false;

                // 水分を持っていない(摂取しても水分を得られない)
                if (!t.CanGetWater()) return false;

                WaterPreferability waterPreferability = t.GetWaterPreferability();

                // 水の品質が範囲外
                if (waterPreferability < WaterPreferability.SeaWater || waterPreferability > maxPref) return false;

                // 現在飲める状態には無い
                if (!t.CanDrinkWaterNow()) return false;

                // 入植者は囚人部屋のアイテムを扱えないことがあるが、そのことに関するチェックでダメならfalse
                if (!MizuUtility.IsWaterSourceOnMapSociallyProper(t, getter, eater, allowSociallyImproper)) return false;

                // 取得者がそれに気づいていない
                if (!getter.AnimalAwareOf(t)) return false;

                // 取得者が予約できない
                if (!getter.CanReserve(t)) return false;

                return true;
            };

            if (getter.RaceProps.Humanlike)
            {
                // 取得者はHumanlikeである
                //   →条件を満たすものの中から最適な物を探す
                return MizuUtility.SpawnedWaterSearchInnerScan(
                    eater,
                    getter.Position,
                    getter.Map.listerThings.ThingsInGroup(ThingRequestGroup.Everything).FindAll((t) => t.CanDrinkWaterNow()),
                    PathEndMode.ClosestTouch,
                    TraverseParms.For(getter),
                    9999f, waterValidator);
            }

            // 取得者はHumanlikeではない

            // プレイヤー派閥に所属しているかどうかでリージョン数を変える
            int searchRegionsMax = 30;
            if (getter.Faction == Faction.OfPlayer)
            {
                searchRegionsMax = 100;
            }

            HashSet<Thing> filtered = new HashSet<Thing>();
            foreach (Thing current in GenRadial.RadialDistinctThingsAround(getter.Position, getter.Map, 2f, true))
            {
                // 自分を中心に半径2以内の物をチェック

                Pawn pawn = current as Pawn;
                if (pawn != null && pawn != getter && pawn.RaceProps.Animal && pawn.CurJob != null && pawn.CurJob.def == MizuDef.Job_DrinkWater && pawn.CurJob.GetTarget(TargetIndex.A).HasThing)
                {
                    // 自分ではない動物が現在水アイテムを摂取している
                    //   →今まさに摂取している物は探索対象から除外
                    filtered.Add(pawn.CurJob.GetTarget(TargetIndex.A).Thing);
                }
            }

            bool ignoreEntirelyForbiddenRegions = !allowForbidden  // 禁止物のアクセスは許可されていない
                && ForbidUtility.CaresAboutForbidden(getter, true) // 禁止設定を守ろうとする
                && (getter.playerSettings != null && getter.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap != null); // 有効な制限エリアなし

            Predicate<Thing> predicate = (t) =>
            {
                return waterValidator(t) // アイテムが条件を満たしている
                    && !filtered.Contains(t)  // すぐ近くで他の動物が飲んでいる水のリストに入っていない
                    && t.GetWaterPreferability() >= WaterPreferability.SeaWater; // 水の品質が最低値より上
            };

            // 指定の条件下でアクセスできるものを探す
            Thing thing = GenClosest.ClosestThingReachable(
                getter.Position,
                getter.Map,
                ThingRequest.ForGroup(ThingRequestGroup.HaulableEver),
                PathEndMode.ClosestTouch,
                TraverseParms.For(getter),
                9999f,
                predicate,
                null,
                0,
                searchRegionsMax,
                false,
                RegionType.Set_Passable,
                ignoreEntirelyForbiddenRegions);

            // 物が見つかった
            if (thing != null) return thing;

            // 条件を変えて再探索
            return GenClosest.ClosestThingReachable(
                getter.Position,
                getter.Map,
                ThingRequest.ForGroup(ThingRequestGroup.HaulableEver),
                PathEndMode.ClosestTouch,
                TraverseParms.For(getter),
                9999f,
                waterValidator,  // ここが変わった
                null,
                0,
                searchRegionsMax,
                false,
                RegionType.Set_Passable,
                ignoreEntirelyForbiddenRegions);
        }

        private static bool IsWaterSourceOnMapSociallyProper(Thing t, Pawn getter, Pawn eater, bool allowSociallyImproper)
        {
            // 囚人部屋にあっても強引に使用して良い
            if (allowSociallyImproper) return true;

            // 適切な場所にある
            if (t.IsSociallyProper(getter) || t.IsSociallyProper(eater, eater.IsPrisonerOfColony, !getter.RaceProps.Animal)) return true;

            return false;
        }

        private static Thing SpawnedWaterSearchInnerScan(Pawn eater, IntVec3 root, List<Thing> searchSet, PathEndMode peMode, TraverseParms traverseParams, float maxDistance = 9999f, Predicate<Thing> validator = null)
        {
            // 探索対象リストなし
            if (searchSet == null) return null;

            // 対象のポーンを決める(取得者優先、次点で摂取者)
            Pawn pawn = traverseParams.pawn ?? eater;

            Thing result = null;
            float maxScore = float.MinValue;

            foreach (var thing in searchSet)
            {
                // アイテムとの距離が限界以上離れていたらダメ
                float lengthManhattan = (float)(root - thing.Position).LengthManhattan;
                if (lengthManhattan > maxDistance) continue;

                // 現時点での候補アイテムのスコア(摂取者にとって)を超えていないならダメ
                float thingScore = MizuUtility.GetWaterItemScore(eater, thing, lengthManhattan, false);
                if (thingScore < maxScore) continue;

                // ポーンがそこまでたどり着けなければだめ
                if (!pawn.Map.reachability.CanReach(root, thing, peMode, traverseParams)) continue;

                // まだ出現していない場合はダメ
                if (!thing.Spawned) continue;

                // アイテムが指定の条件を満たしていないならダメ
                if (validator != null && !validator(thing)) continue;

                // すべての条件を満足
                result = thing;
                maxScore = thingScore;
            }
            return result;
        }

        public static float GetWaterItemScore(Pawn eater, Thing t, float dist, bool takingToInventory = false)
        {
            // 水ではない、もしくは水だけど水の種類データが未設定
            //   →最低スコア
            if (t.GetWaterPreferability() == WaterPreferability.Undefined) return float.MinValue;

            // 基本点
            float score = 300f;

            // 距離が遠いと減点
            score -= dist;

            if (!eater.story.traits.HasTrait(TraitDefOf.Ascetic))
            {
                // 水の種類による加算点
                // 通常：品質1=10点
                score += (int)t.GetWaterPreferability();
            }
            else
            {
                // 水の種類による加算点
                // 禁欲主義：品質1=-10点
                score += (int)WaterPreferability.ClearWater - (int)t.GetWaterPreferability();
            }

            // 海水は健康被害を及ぼすので減点
            if (t.GetWaterPreferability() == WaterPreferability.SeaWater) score -= 100f;

            // 腐りかけの場合10点加算(品質1相当)
            if (t.IsRotSoonForWater()) score += 10f;

            return score;
        }

        public static float GetWater(Pawn getter, Thing thing, float waterWanted)
        {
            // 摂取しようとしているものが既に消滅している(エラー)
            if (thing.Destroyed)
            {
                Log.Error(getter + " drank destroyed thing " + thing);
                return 0f;
            }

            // 現在飲めないはずのものを飲もうとしている(エラー)
            if (!thing.CanDrinkWaterNow())
            {
                Log.Error(getter + " drank CanDrinkWaterNow()=false thing " + thing);
                return 0f;
            }

            if (getter.needs.mood != null)
            {
                // 水分摂取による心情変化
                foreach (var thoughtDef in MizuUtility.ThoughtsFromGettingWater(getter, thing))
                {
                    getter.needs.mood.thoughts.memories.TryGainMemory(thoughtDef);
                }
            }

            // 健康状態の変化
            CompWater comp = thing.TryGetComp<CompWater>();
            if (comp == null)
            {
                Log.Error("comp is null");
                return 0.0f;
            }
            // 指定された健康状態になる
            if (comp.DrinkHediff != null)
            {
                getter.health.AddHediff(HediffMaker.MakeHediff(comp.DrinkHediff, getter));
            }
            // 確率で食中毒
            if (Rand.Value < comp.FoodPoisonChance)
            {
                FoodUtility.AddFoodPoisoningHediff(getter, thing);
            }

            int drankWaterItemCount;
            float gotWaterAmount;

            // 摂取個数と摂取水分量の計算
            thing.GetWaterCalculateAmounts(getter, waterWanted, out drankWaterItemCount, out gotWaterAmount);

            if (drankWaterItemCount > 0)
            {
                if (drankWaterItemCount == thing.stackCount)
                {
                    // アイテム消費数とスタック数が同じ
                    //   →完全消滅
                    if (getter.Map.reservationManager.ReservedBy(thing, getter, getter.CurJob))
                    {
                        getter.Map.reservationManager.Release(thing, getter, getter.CurJob);
                    }
                    thing.Destroy(DestroyMode.Vanish);
                }
                else
                {
                    // スタック数と異なる
                    //   →消費した数だけ減らす
                    thing.SplitOff(drankWaterItemCount);
                }
            }
            return gotWaterAmount;
        }

        public static List<ThoughtDef> ThoughtsFromGettingWater(Pawn getter, Thing t)
        {
            // 空のリスト
            List<ThoughtDef> thoughtList = new List<ThoughtDef>();

            // 心情ステータスの無いポーンには空のリストを返す
            if (getter.needs == null || getter.needs.mood == null) return thoughtList;

            // アイテムが水でないなら空のリストを返す
            CompWater comp = t.TryGetComp<CompWater>();
            if (comp == null) return thoughtList;

            // 禁欲主義ではない＆飲んだ時の心情が設定されていたら、それを与える
            if (!getter.story.traits.HasTrait(TraitDefOf.Ascetic) && comp.DrinkThought != null)
            {
                thoughtList.Add(comp.DrinkThought);
            }

            return thoughtList;
        }

        public static int WillGetStackCountOf(Pawn getter, Thing thing)
        {
            CompWater comp = thing.TryGetComp<CompWater>();

            // 水ではない→摂取数0
            if (comp == null) return 0;

            // それを一度に摂取できる数と、何個摂取すれば水分が100%になるのか、の小さい方
            int wantedWaterItemCount = Math.Min(comp.MaxNumToGetAtOnce, MizuUtility.StackCountForWater(thing, getter.needs.water().WaterWanted));

            // 1個未満なら1個にする
            if (wantedWaterItemCount < 1) return 1;

            return wantedWaterItemCount;
        }

        public static int StackCountForWater(Thing thing, float waterWanted)
        {
            CompWater comp = thing.TryGetComp<CompWater>();

            // 水ではない
            if (comp == null) return 0;

            // 必要な水分がほぼゼロ
            if (waterWanted <= 0.0001f) return 0;

            // それを何個摂取すれば水分が十分になるのかを返す(最低値1)
            return Math.Max((int)Math.Round(waterWanted / comp.WaterAmount), 1);
        }

        public static ThingDef GetWaterThingDefFromTerrainType(WaterTerrainType waterTerrainType)
        {
            // 地形タイプ→水アイテム
            switch (waterTerrainType)
            {
                case WaterTerrainType.RawWater:
                    return MizuDef.Thing_RawWater;
                case WaterTerrainType.MudWater:
                    return MizuDef.Thing_MudWater;
                case WaterTerrainType.SeaWater:
                    return MizuDef.Thing_SeaWater;
                default:
                    return null;
            }
        }

        public static ThoughtDef GetThoughtDefFromTerrainType(WaterTerrainType waterTerrainType)
        {
            // 地形タイプ→心情
            switch (waterTerrainType)
            {
                case WaterTerrainType.MudWater:
                    return MizuDef.Thought_DrankMudWater;
                case WaterTerrainType.SeaWater:
                    return MizuDef.Thought_DrankSeaWater;
                default:
                    return null;
            }
        }

        public static ThingDef GetWaterThingDefFromWaterType(WaterType waterType)
        {
            // 水の種類→水アイテム
            switch (waterType)
            {
                case WaterType.ClearWater:
                    return MizuDef.Thing_ClearWater;
                case WaterType.NormalWater:
                    return MizuDef.Thing_NormalWater;
                case WaterType.RawWater:
                    return MizuDef.Thing_RawWater;
                case WaterType.MudWater:
                    return MizuDef.Thing_MudWater;
                case WaterType.SeaWater:
                    return MizuDef.Thing_SeaWater;
                default:
                    return null;
            }
        }
    }
}