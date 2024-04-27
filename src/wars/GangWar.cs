﻿using GTA.Math;
using GTA.Native;

using System;
using System.Collections.Generic;
using System.Drawing;

namespace GTA.GangAndTurfMod
{
    public class GangWar : UpdatedClass
    {

        public int attackerReinforcements, defenderReinforcements;

        /// <summary>
        /// numbers closer to 1 for defender advantage, less than 0.5 for attacker advantage.
        /// this advantage affects the member respawns:
        /// whoever has the greater advantage tends to have priority when spawning
        /// </summary>
        private float defenderReinforcementsAdvantage = 0.0f;

        private const int MS_TIME_BETWEEN_CAR_SPAWNS = 2400;

        //balance checks are what tries to ensure that reinforcement advantage is something meaningful in battle.
        //we try to reduce the amount of spawned members of one gang if they were meant to have less members defending/attacking than their enemy
        private const int MS_TIME_BETWEEN_BALANCE_CHECKS = 5250;

        private const int MIN_LOSSES_PER_AUTORESOLVE_STEP = 6, MAX_LOSSES_PER_AUTORESOLVE_STEP = 17;

        private int msTimeWarStarted;

        private int msTimeOfLastAutoResolveStep = 0;

        private int msTimeOfLastCarSpawn = 0;

        private int msTimeOfLastBalanceCheck = 0;

        private int msTimeOfLastNoSpawnsPunishment = 0;

        private int maxSpawnedDefenders, maxSpawnedAttackers;

        private int spawnedDefenders = 0, spawnedAttackers = 0;

        public TurfZone warZone;

        public bool playerNearWarzone = false, isFocused = false;

        public Gang attackingGang, defendingGang;

        private Blip warBlip;

        /// <summary>
        /// index 0 is the area around the zone blip; 1 is the area around the player when the war starts
        /// (this one may not be used if the war was started by the AI and the player was away)
        /// </summary>
        private readonly Blip[] warAreaBlips;

        public List<WarControlPoint> attackerSpawnPoints, defenderSpawnPoints;

        private GangWarManager.AttackStrength curWarAtkStrength = GangWarManager.AttackStrength.light;

        public List<WarControlPoint> controlPoints = new List<WarControlPoint>();

        private List<Vector3> availableNearbyPresetSpawns;


        private int desiredNumberOfControlPointsForThisWar = 0;
        private int nextCPIndexToCheckForCapture = 0;

        private int allowedSpawnLimit = 0;

        private Vector3 attackerVehicleSpawnDirection, defenderVehicleSpawnDirection;

        public Action<GangWar> OnReinforcementsChanged;
        public Action<GangWar> OnPlayerEnteredWarzone;
        public Action<GangWar> OnPlayerLeftWarzone;

        public delegate void OnWarEnded(GangWar endedWar, bool defenderVictory);

        public OnWarEnded onWarEnded;

        public GangWar()
        {
            warAreaBlips = new Blip[2];
            ResetUpdateInterval();
        }


        #region start/end/skip war
        public bool StartWar(Gang attackerGang, Gang defenderGang, TurfZone warZone, GangWarManager.AttackStrength attackStrength)
        {
            attackingGang = attackerGang;
            defendingGang = defenderGang;
            this.warZone = warZone;
            curWarAtkStrength = attackStrength;
            playerNearWarzone = false;

            warBlip = World.CreateBlip(warZone.zoneBlipPosition);
            warBlip.Sprite = BlipSprite.Deathmatch;
            warBlip.Color = BlipColor.Red;


            attackerSpawnPoints = new List<WarControlPoint>();
            defenderSpawnPoints = new List<WarControlPoint>();

            if (warAreaBlips[1] != null)
            {
                warAreaBlips[1].Delete();
                warAreaBlips[1] = null;
            }

            bool playerGangInvolved = IsPlayerGangInvolved();

            spawnedDefenders = SpawnManager.instance.GetSpawnedMembersOfGang(defenderGang).Count;
            spawnedAttackers = SpawnManager.instance.GetSpawnedMembersOfGang(attackerGang).Count;

            defenderReinforcements = GangCalculations.CalculateDefenderReinforcements(defenderGang, warZone);
            attackerReinforcements = GangCalculations.CalculateAttackerReinforcements(attackerGang, attackStrength);


            defenderReinforcementsAdvantage = defenderReinforcements / (float)(attackerReinforcements + defenderReinforcements);


            warAreaBlips[0] = World.CreateBlip(warZone.zoneBlipPosition,
                ModOptions.instance.maxDistToWarBlipBeforePlayerLeavesWar);
            warAreaBlips[0].Sprite = BlipSprite.BigCircle;
            warAreaBlips[0].Color = BlipColor.Red;
            warAreaBlips[0].Alpha = playerGangInvolved ? 175 : 25;

            if (playerGangInvolved)
            {
                warBlip.IsShortRange = false;
                warBlip.IsFlashing = true;

                //BANG-like sound
                Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "PROPERTY_PURCHASE", "HUD_AWARDS");

                if (ModOptions.instance.notificationsEnabled && defenderGang == GangManager.instance.PlayerGang)
                {
                    //if the player is defending and already was inside the zone, we should take their current spawned members in consideration
                    if (ModOptions.instance.addAlreadySpawnedMembersToWarRequiredKills)
                    {
                        defenderReinforcements = RandoMath.Max(defenderReinforcements, spawnedDefenders);
                    }

                    UI.Notification.Show(string.Concat("The ", attackerGang.name, " are attacking ", warZone.zoneName, "! They are ",
                    attackerReinforcements.ToString(),
                    " against our ",
                    defenderReinforcements.ToString()));
                }
                    

                GangWarManager.instance.timeLastWarAgainstPlayer = ModCore.curGameTime;
                allowedSpawnLimit = ModOptions.instance.spawnedMemberLimit;
            }
            else
            {
                warBlip.IsShortRange = true;
                allowedSpawnLimit = (int)RandoMath.Max(ModOptions.instance.spawnedMemberLimit * ModOptions.instance.spawnLimitPercentToUseInAIOnlyWar,
                    ModOptions.instance.minSpawnsForEachSideDuringWars * 2);
            }

            warBlip.Name = string.Concat("Gang War (", attackerGang.name, " attacking ", defenderGang.name + ")");

            msTimeOfLastAutoResolveStep = ModCore.curGameTime;
            msTimeWarStarted = ModCore.curGameTime;
            

            maxSpawnedDefenders = (int)RandoMath.ClampValue(allowedSpawnLimit * defenderReinforcementsAdvantage,
                ModOptions.instance.minSpawnsForEachSideDuringWars,
                allowedSpawnLimit - ModOptions.instance.minSpawnsForEachSideDuringWars);

            maxSpawnedAttackers = RandoMath.Max
                (allowedSpawnLimit - maxSpawnedDefenders, ModOptions.instance.minSpawnsForEachSideDuringWars);

            Logger.Log(string.Concat("war started at ", warZone.zoneName, "! Reinf advantage: ", defenderReinforcementsAdvantage.ToString(),
                " maxDefenders: ", maxSpawnedDefenders.ToString(), " maxAttackers: ", maxSpawnedAttackers.ToString()), 3);

            //this number is properly set once we're inside the zone and PrepareAndSetupInitialSpawnPoint is run
            desiredNumberOfControlPointsForThisWar = 1;

            RefreshVehicleSpawnDirections();

            SetHateRelationsBetweenGangs();

            return true;
        }

        /// <summary>
        /// checks both gangs' situations and the amount of reinforcements left for each side.
        /// also considers their strength (with variations) in order to decide the likely outcome of this battle.
        /// returns true for a defender victory, false if the attackers won
        /// </summary>
        public bool GetSkippedWarResult(float playerGangStrengthFactor = 1.0f)
        {
            float defenderBaseStr = defendingGang.GetGangVariedStrengthValue(),
                attackerBaseStr = attackingGang.GetGangVariedStrengthValue();

            //the amount of reinforcements counts here
            float totalDefenderStrength = defenderReinforcements / attackerBaseStr,
                totalAttackerStrength = attackerReinforcements / defenderBaseStr;

            bool playerGangInvolved = IsPlayerGangInvolved();

            if (playerGangInvolved)
            {
                if (defendingGang == GangManager.instance.PlayerGang)
                {
                    totalDefenderStrength *= playerGangStrengthFactor;
                }
                else
                {
                    totalAttackerStrength *= playerGangStrengthFactor;
                }
            }

            bool defenderVictory = totalDefenderStrength > totalAttackerStrength;

            if (playerGangInvolved)
            {
                Gang enemyGang = attackingGang;
                float strengthProportion = totalDefenderStrength / totalAttackerStrength;

                if (attackingGang.isPlayerOwned)
                {
                    enemyGang = defendingGang;
                    strengthProportion = totalAttackerStrength / totalDefenderStrength;
                }


                string battleReport = "Battle report: We";

                //we attempt to provide a little report on what happened
                if (defenderVictory)
                {
                    battleReport = string.Concat(battleReport, " won the battle against the ", enemyGang.name, "! ");

                    if (strengthProportion > 2f)
                    {
                        battleReport = string.Concat(battleReport, "They were crushed!");
                    }
                    else if (strengthProportion > 1.75f)
                    {
                        battleReport = string.Concat(battleReport, "We had the upper hand!");
                    }
                    else if (strengthProportion > 1.5f)
                    {
                        battleReport = string.Concat(battleReport, "We fought well!");
                    }
                    else if (strengthProportion > 1.25f)
                    {
                        battleReport = string.Concat(battleReport, "It was a bit tough, but we won!");
                    }
                    else
                    {
                        battleReport = string.Concat(battleReport, "It was a tough battle, but we prevailed in the end.");
                    }
                }
                else
                {
                    battleReport = string.Concat(battleReport, " lost the battle against the ", enemyGang.name, ". ");

                    if (strengthProportion < 0.5f)
                    {
                        battleReport = string.Concat(battleReport, "We were crushed!");
                    }
                    else if (strengthProportion < 0.625f)
                    {
                        battleReport = string.Concat(battleReport, "We had no chance!");
                    }
                    else if (strengthProportion < 0.75f)
                    {
                        battleReport = string.Concat(battleReport, "We just couldn't beat them!");
                    }
                    else if (strengthProportion < 0.875f)
                    {
                        battleReport = string.Concat(battleReport, "We fought hard, but they pushed us back.");
                    }
                    else
                    {
                        battleReport = string.Concat(battleReport, "We almost won, but they got us in the end.");
                    }
                }

                if (ModOptions.instance.notificationsEnabled)
                    UI.Notification.Show(battleReport);
            }



            return defenderVictory;
        }

        public void EndWar(bool defenderVictory)
        {
            Gang loserGang = defenderVictory ? attackingGang : defendingGang;

            int battleProfit = GangCalculations.CalculateBattleRewards
                (loserGang, defenderVictory ? (int)curWarAtkStrength : warZone.value, defenderVictory);

            if (IsPlayerGangInvolved())
            {
                bool playerWon = !loserGang.isPlayerOwned;

                if (playerWon)
                {
                    //player gang was involved and won
                    AmbientGangMemberSpawner.instance.postWarBackupsRemaining = ModOptions.instance.postWarBackupsAmount;

                    MindControl.AddOrSubtractMoneyToProtagonist
                        (battleProfit);

                    if (ModOptions.instance.notificationsEnabled)
                        UI.Notification.Show("Victory rewards: $" + battleProfit.ToString());

                    if (defenderVictory)
                    {
                        UI.Screen.ShowSubtitle(warZone.zoneName + " remains ours!");
                    }
                    else
                    {
                        UI.Screen.ShowSubtitle(warZone.zoneName + " is ours!");
                    }
                }
                else
                {
                    //player was involved and lost!
                    if (defenderVictory)
                    {
                        UI.Screen.ShowSubtitle("We've lost this battle. They keep the turf.");
                    }
                    else
                    {
                        UI.Screen.ShowSubtitle(warZone.zoneName + " has been taken by the " + attackingGang.name + "!");
                    }
                }

                Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "ScreenFlash", "WastedSounds");
            }
            else
            {
                if (defenderVictory)
                {
                    defendingGang.AddMoney(battleProfit);
                }
                else
                {
                    attackingGang.AddMoney(battleProfit);
                }
            }

            if (warBlip != null)
            {
                warBlip.Delete();

                foreach (Blip areaBlip in warAreaBlips)
                {
                    if (areaBlip != null)
                        areaBlip.Delete();
                }
            }

            if (isFocused)
            {
                StoreControlPointRecommendations(defenderVictory);
            }


            playerNearWarzone = false;
            OnPlayerLeftWarzone?.Invoke(this);

            PoolAllControlPoints();

            if (!defenderVictory)
            {
                attackingGang.TakeZone(warZone);
                if (ModOptions.instance.survivorsBecomeZoneValueOnAttackerVictory)
                {
                    // add some levels based on how many attackers survived and the attack size
                    int startingAttackers = GangCalculations.CalculateAttackerReinforcements(attackingGang, curWarAtkStrength);
                    float remainingPercent = attackerReinforcements / (float)startingAttackers;
                    int valueIfAllSurvived = GangCalculations.CalculateTurfValueEquivalentToGangAttack(curWarAtkStrength);
                    warZone.ChangeValue((int)(valueIfAllSurvived * remainingPercent));

                    Logger.Log($"atker victory! starting atkers: {startingAttackers}, remainingPct: {remainingPercent}, valueIfAllSurvived: {valueIfAllSurvived}, final value: {(int)(valueIfAllSurvived * remainingPercent)}", 3);
                }
            }
            else
            {
                if (ModOptions.instance.zonesCanLoseValueOnDefenderVictory && warZone.value > 0)
                {
                    // the zone loses some levels based on how many defenders died
                    int startingDefenders = GangCalculations.CalculateDefenderReinforcements(defendingGang, warZone);
                    float remainingDefendersPercent = RandoMath.ClampValue(defenderReinforcements / (float) startingDefenders, 0.0f, 1.0f);
                    warZone.ChangeValue(RandoMath.CeilToInt(warZone.value * remainingDefendersPercent));

                    Logger.Log($"defender victory! starting defers: {startingDefenders}, remainingPct: {remainingDefendersPercent}, final value: {RandoMath.CeilToInt(warZone.value * remainingDefendersPercent)}", 3);
                }
            }

            onWarEnded?.Invoke(this, defenderVictory);

            

            //reset relations to whatever is set in modoptions
            GangManager.instance.SetGangRelationsAccordingToAggrLevel();


        }

        /// <summary>
        /// reduces reinforcements on both sides (optionally applying the multiplier on the player gang if it's involved) and then checks if the war should end
        /// </summary>
        /// <param name="lossMultiplierOnPlayerGang"></param>
        public void RunAutoResolveStep(float lossMultiplierOnPlayerGang = 1.0f)
        {
            float defenderLosses = RandoMath.CachedRandom.Next(MIN_LOSSES_PER_AUTORESOLVE_STEP, MAX_LOSSES_PER_AUTORESOLVE_STEP);

            float attackerLosses = RandoMath.CachedRandom.Next(MIN_LOSSES_PER_AUTORESOLVE_STEP, MAX_LOSSES_PER_AUTORESOLVE_STEP);

            float biasTowardDefenders = defendingGang.GetGangVariedStrengthValue() / attackingGang.GetGangVariedStrengthValue();

            defenderLosses = RandoMath.ClampValue(defenderLosses / biasTowardDefenders, MIN_LOSSES_PER_AUTORESOLVE_STEP, MAX_LOSSES_PER_AUTORESOLVE_STEP);
            attackerLosses = RandoMath.ClampValue(attackerLosses * biasTowardDefenders, MIN_LOSSES_PER_AUTORESOLVE_STEP, MAX_LOSSES_PER_AUTORESOLVE_STEP);

            if (defendingGang.isPlayerOwned) defenderLosses *= lossMultiplierOnPlayerGang;
            if (attackingGang.isPlayerOwned) attackerLosses *= lossMultiplierOnPlayerGang;

            attackerReinforcements -= (int)attackerLosses;
            defenderReinforcements -= (int)defenderLosses;

            if (attackerReinforcements <= 0 || defenderReinforcements <= 0)
            {
                EndWar(attackerReinforcements <= 0); //favor the defenders if both sides ran out of reinforcements
            }
            else
            {
                //alliedNumText.Caption = defenderReinforcements.ToString();
                OnReinforcementsChanged?.Invoke(this);
            }

            msTimeOfLastAutoResolveStep = ModCore.curGameTime;
        }

        #endregion

        #region control point related

        /// <summary>
        /// stores nearby preset spawn points and attempts to set one spawn, returning true if it succeeded
        /// </summary>
        /// <param name="initialReferencePoint"></param>
        private bool PrepareAndSetupInitialSpawnPoint(Vector3 initialReferencePoint)
        {
            Logger.Log("setSpawnPoints: begin", 3);
            //spawn points for both sides should be a bit far from each other, so that the war isn't just pure chaos

            availableNearbyPresetSpawns = PotentialSpawnsForWars.GetAllPotentialSpawnsInRadiusFromPos
                (initialReferencePoint, ModOptions.instance.maxDistanceBetweenWarSpawns / 2);

            desiredNumberOfControlPointsForThisWar = RandoMath.ClampValue(availableNearbyPresetSpawns.Count,
                RandoMath.Max(ModOptions.instance.warsMinNumControlPoints, 0),
                ModOptions.instance.warsMinNumControlPoints + (int)(warZone.GetUpgradePercentage() * ModOptions.instance.warsMaxExtraControlPoints));

            //if (availableNearbyPresetSpawns.Count < 2)
            //{
            //    UI.Notification.Show("Less than 2 preset potential spawns were found nearby. One or both teams' spawns will be generated.");
            //}

            if(desiredNumberOfControlPointsForThisWar > 0)
            {
                if (availableNearbyPresetSpawns.Count > 0)
                {
                    //find the closest preset spawn and set it as the allied CP
                    int indexOfClosestSpawn = 0;
                    float smallestDistance = ModOptions.instance.maxDistToWarBlipBeforePlayerLeavesWar;

                    for (int i = 0; i < availableNearbyPresetSpawns.Count; i++)
                    {
                        float candidateDistance = initialReferencePoint.DistanceTo(availableNearbyPresetSpawns[i]);
                        if (candidateDistance < smallestDistance)
                        {
                            smallestDistance = candidateDistance;
                            indexOfClosestSpawn = i;
                        }
                    }

                    if (TrySetupAControlPoint(
                        availableNearbyPresetSpawns[indexOfClosestSpawn],
                        IsPlayerGangInvolved() ?
                            GangManager.instance.PlayerGang :
                            GangWarManager.instance.PickOwnerGangForControlPoint(availableNearbyPresetSpawns[indexOfClosestSpawn], this)))
                    {
                        availableNearbyPresetSpawns.RemoveAt(indexOfClosestSpawn);
                    }
                }
                else
                {

                    TrySetupAControlPoint(SpawnManager.instance.FindCustomSpawnPoint
                                    (initialReferencePoint,
                                    ModOptions.instance.GetAcceptableMemberSpawnDistance(10),
                                    10,
                                    5),
                                    IsPlayerGangInvolved() ? GangManager.instance.PlayerGang : defendingGang);
                }
            }
            

            Logger.Log("setSpawnPoints: end", 3);

            return controlPoints.Count > 0 || desiredNumberOfControlPointsForThisWar == 0;

        }

        /// <summary>
        /// if spawns are set, returns a random spawn that can be allied or hostile
        /// </summary>
        /// <returns></returns>
        public Vector3 GetRandomSpawnPoint()
        {
            if (controlPoints.Count > 0)
            {
                WarControlPoint pickedCP;

                if (RandoMath.RandomBool())
                {
                    pickedCP = RandoMath.RandomElement(defenderSpawnPoints);
                }
                else
                {
                    pickedCP = RandoMath.RandomElement(attackerSpawnPoints);
                }

                return pickedCP.position;
            }
            else
            {
                return Vector3.Zero;
            }

        }


        /// <summary>
        /// returns true if the provided position is not zero and not too close to existing spawns
        /// and the point was set up successfully
        /// </summary>
        private bool TrySetupAControlPoint(Vector3 targetPos, Gang ownerGang)
        {
            if (targetPos != Vector3.Zero)
            {
                //at least one existing CP must be close enough (below maxDistanceBetweenWarSpawns)
                //...unless we don't have any CPs set up yet
                bool atLeastOneCpIsClose = controlPoints.Count == 0;
                foreach(WarControlPoint cp in controlPoints)
                {
                    if(cp.position.DistanceTo(targetPos) < ModOptions.instance.minDistanceBetweenWarSpawns)
                    {
                        return false;
                    }

                    if (!atLeastOneCpIsClose && cp.position.DistanceTo(targetPos) < ModOptions.instance.maxDistanceBetweenWarSpawns)
                    {
                        atLeastOneCpIsClose = true;
                    }
                }

                if (!atLeastOneCpIsClose)
                {
                    return false;
                }

                WarControlPoint newPoint = GangWarManager.instance.GetUnusedWarControlPoint();

                newPoint.SetupAtPosition(targetPos, ownerGang, this);
                if (ownerGang != null) ControlPointHasBeenCaptured(newPoint);
                controlPoints.Add(newPoint);

                Logger.Log(string.Concat("Set up new spawn point for gang: ", ownerGang != null ? ownerGang.name : "(neutral point)"), 3);

                return true;
            }

            return false;
        }

        public void ControlPointHasBeenCaptured(WarControlPoint capturedCP)
        {
            if (capturedCP.ownerGang != defendingGang)
            {
                defenderSpawnPoints.Remove(capturedCP);
            }

            if (capturedCP.ownerGang != attackingGang)
            {
                attackerSpawnPoints.Remove(capturedCP);
            }

            capturedCP.onCaptureCooldown = true;

        }

        /// <summary>
        /// a CP must "cool down" before being used as a spawn point
        /// </summary>
        /// <param name="capturedCP"></param>
        public void ControlPointHasCooledDown(WarControlPoint capturedCP)
        {
            if (capturedCP.ownerGang == attackingGang)
            {
                attackerSpawnPoints.Add(capturedCP);
            }
            else if (capturedCP.ownerGang == defendingGang)
            {
                defenderSpawnPoints.Add(capturedCP);
            }

            capturedCP.onCaptureCooldown = false;

        }

        private void PoolAllControlPoints()
        {
            foreach (WarControlPoint cp in controlPoints)
            {
                GangWarManager.instance.PoolControlPoint(cp);
            }

            controlPoints.Clear();
        }

        private void HideAllControlPoints()
        {
            foreach (WarControlPoint cp in controlPoints)
            {
                cp.HideBlip();
            }
        }

        private void UpdateDisplayForAllControlPoints()
        {
            foreach (WarControlPoint cp in controlPoints)
            {
                cp.CreateAttachedBlip();
                cp.UpdateBlipAppearance();
            }
        }

        /// <summary>
        /// stores recommendations for control point ownership in future wars in the same location
        /// </summary>
        private void StoreControlPointRecommendations(bool defendersWon)
        {
            foreach(WarControlPoint cp in controlPoints)
            {
                if(defendersWon && cp.ownerGang == defendingGang || 
                    !defendersWon && cp.ownerGang == attackingGang)
                {
                    GangWarManager.instance.AddRecommendedControlPoint(cp.position, cp.ownerGang);
                }
            }
        }

        /// <summary>
        /// gets a neutral or enemy point's position for this gang's members to head to (it must be a position different from previousMoveTarget)
        /// </summary>
        /// <param name="gang"></param>
        /// <returns></returns>
        public Vector3 GetMoveTargetForGang(Gang gang, Vector3? previousMoveTarget = null)
        {
            WarControlPoint targetPoint = null;

            for (int i = 0; i < controlPoints.Count; i++)
            {
                if (targetPoint == null ||
                    (controlPoints[i].ownerGang != gang && targetPoint.ownerGang == gang) ||
                    (controlPoints[i].ownerGang != gang && targetPoint.ownerGang != gang && RandoMath.RandomBool()))
                {
                    targetPoint = controlPoints[i];
                }
            }

            if (targetPoint == null || (previousMoveTarget.HasValue && previousMoveTarget == targetPoint.position))
            {
                return MindControl.SafePositionNearPlayer + RandoMath.RandomDirection(true) * 
                    ((float)RandoMath.CachedRandom.NextDouble() * ModOptions.instance.distanceToCaptureWarControlPoint);
            }

            return targetPoint.position;
        }

        /// <summary>
        /// returns the position of one of the control points for the provided gang
        /// </summary>
        /// <param name="gang"></param>
        /// <returns></returns>
        public Vector3 GetSpawnPositionForGang(Gang gang, out WarControlPoint pickedPoint)
        {
            pickedPoint = gang == attackingGang ? attackerSpawnPoints.RandomElement() : defenderSpawnPoints.RandomElement();

            if (pickedPoint != null)
            {
                return pickedPoint.position;
            }
            else
            {
                return Vector3.Zero;
            }
        }

        /// <summary>
        /// updates the "recommended" vehicle spawn directions.
        /// The directions are intended as "incentives" for the attackers and defenders to come from different sides
        /// instead of on top of each other
        /// </summary>
        public void RefreshVehicleSpawnDirections(bool forceRandom = false)
        {
            if (forceRandom)
            {
                defenderVehicleSpawnDirection = RandoMath.RandomDirection(true);
            }
            else
            {
                // if the war is using spawn points, consider them for the directions.
                // if not, consider the gang's owned zones.
                // if the gang has no zones... yeah, just randomize haha
                defenderVehicleSpawnDirection = Vector3.Zero;
                if (defenderSpawnPoints.Count > 0)
                {
                    foreach(var spawn in defenderSpawnPoints)
                    {
                        defenderVehicleSpawnDirection += spawn.position;
                    }

                    defenderVehicleSpawnDirection.Z = 0;
                    defenderVehicleSpawnDirection.Normalize();

                }
                else
                {
                    var defenderZones = ZoneManager.instance.GetZonesControlledByGang(defendingGang.name);
                    if (defenderZones.Count > 0)
                    {
                        foreach (var defZone in defenderZones)
                        {
                            defenderVehicleSpawnDirection += defZone.zoneBlipPosition;
                        }

                        defenderVehicleSpawnDirection.Z = 0;
                        defenderVehicleSpawnDirection.Normalize();
                    }
                    else
                    {
                        defenderVehicleSpawnDirection = RandoMath.RandomDirection(true);
                    }
                }
                
            }
            
            attackerVehicleSpawnDirection = defenderVehicleSpawnDirection * -1;
        }

        #endregion


        #region spawn/death/culling handlers
        /// <summary>
        /// spawns a vehicle that has the player as destination
        /// </summary>
        public SpawnedDrivingGangMember SpawnAngryVehicle(bool isDefender)
        {

            int maxPeopleToSpawnInVehicle = isDefender ?
                maxSpawnedDefenders - spawnedDefenders :
                maxSpawnedAttackers - spawnedAttackers;

            if (maxPeopleToSpawnInVehicle < RandoMath.Max(1, ModOptions.instance.warMinAvailableSpawnsBeforeSpawningVehicle)) return null;

            if (SpawnManager.instance.HasThinkingDriversLimitBeenReached()) return null;

            Vector3 playerPos = MindControl.SafePositionNearPlayer;
            
            Vector3 spawnPos = SpawnManager.instance.FindGoodSpawnPointWithHeadingForCar(playerPos, isDefender?
                defenderVehicleSpawnDirection : attackerVehicleSpawnDirection, out float carHeading);

            if(World.GetDistance(playerPos, spawnPos) < ModOptions.instance.minDistanceCarSpawnFromPlayer / 4)
            {
                Logger.Log("War: vehicle spawned too close to player! Try reset vehicle spawn directions", 3);
                RefreshVehicleSpawnDirections(true);
            }

            if (spawnPos == Vector3.Zero) return null;

            SpawnedDrivingGangMember spawnedVehicle = null;

            
            if (isDefender)
            {
                spawnedVehicle = SpawnManager.instance.SpawnGangVehicle(defendingGang,
                    spawnPos, playerPos, false, false, IncrementDefendersCount, maxPeopleToSpawnInVehicle);
            }
            else
            {
                spawnedVehicle = SpawnManager.instance.SpawnGangVehicle(attackingGang,
                    spawnPos, playerPos, false, false, IncrementAttackersCount, maxPeopleToSpawnInVehicle);
            }
            
            if(spawnedVehicle != null)
            {
                //SpawnManager.instance.TryPlaceVehicleOnStreet(spawnedVehicle.vehicleIAmDriving, spawnPos);
                spawnedVehicle.vehicleIAmDriving.Heading = carHeading;
            }

            return spawnedVehicle;
        }

        public SpawnedGangMember SpawnMember(bool isDefender)
        {
            Vector3 spawnPos = GetSpawnPositionForGang(isDefender ? defendingGang : attackingGang, out WarControlPoint pickedPoint);

            SpawnedGangMember spawnedGangMember = null;

            if (isDefender)
            {
                if (spawnedDefenders < maxSpawnedDefenders)
                {
                    Logger.Log("war: try spawn defender", 4);

                    if(pickedPoint == null)
                    {
                        spawnedGangMember = SpawnMemberInsideExistingVehicle(true);
                    }
                    else
                    {
                        spawnedGangMember = SpawnManager.instance.SpawnGangMember(defendingGang, spawnPos, onSuccessfulMemberSpawn: IncrementDefendersCount, true);
                        if(spawnedGangMember != null)
                        {
                            pickedPoint.AttachDeathCheckEventToSpawnedMember(spawnedGangMember);
                        }
                    }
                }

            }
            else
            {
                if (spawnedAttackers < maxSpawnedAttackers)
                {
                    Logger.Log("war: try spawn attacker", 4);

                    if (pickedPoint == null)
                    {
                        spawnedGangMember = SpawnMemberInsideExistingVehicle(false);
                    }
                    else
                    {
                        spawnedGangMember = SpawnManager.instance.SpawnGangMember(attackingGang, spawnPos, onSuccessfulMemberSpawn: IncrementAttackersCount, true);
                        if (spawnedGangMember != null)
                        {
                            pickedPoint.AttachDeathCheckEventToSpawnedMember(spawnedGangMember);
                        }
                    }
                }
            }

            return spawnedGangMember;
        }

        /// <summary>
        /// attempts to spawn a new member inside a "thinking" vehicle, if it has a free seat
        /// </summary>
        /// <param name="isDefender"></param>
        /// <returns></returns>
        private SpawnedGangMember SpawnMemberInsideExistingVehicle(bool isDefender)
        {
            Logger.Log("war: try spawn new passenger inside existing vehicle", 4);

            Gang ownerGang = isDefender ? defendingGang : attackingGang;

            SpawnedDrivingGangMember randomDriver = SpawnManager.instance.GetSpawnedDriversOfGang(ownerGang).RandomElement();
            if (randomDriver != default)
            {
                if (randomDriver.vehicleIAmDriving.IsSeatFree(VehicleSeat.Any))
                {
                    SpawnManager.SuccessfulMemberSpawnDelegate onSpawn;
                    if (isDefender) onSpawn = IncrementDefendersCount; else onSpawn = IncrementAttackersCount;
                    SpawnedGangMember spawnedPassenger =
                        SpawnManager.instance.SpawnGangMember(ownerGang, randomDriver.vehicleIAmDriving.Position, onSuccessfulMemberSpawn: onSpawn, true);
                    if(spawnedPassenger != null)
                    {
                        spawnedPassenger.curStatus = SpawnedGangMember.MemberStatus.inVehicle;
                        spawnedPassenger.watchedPed.SetIntoVehicle(randomDriver.vehicleIAmDriving, VehicleSeat.Any);
                        randomDriver.myPassengers.Add(spawnedPassenger.watchedPed);
                        if (spawnedPassenger.watchedPed.IsUsingAnyVehicleWeapon())
                        {
                            Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, spawnedPassenger.watchedPed, 3, false); // BF_CanLeaveVehicle  
                        }
                        return spawnedPassenger;
                    }
                }
            }
            
            return null;
        }

        private void IncrementDefendersCount() { spawnedDefenders++; }

        private void IncrementAttackersCount() { spawnedAttackers++; }

        public void MemberHasDiedNearWar(Gang memberGang)
        {
            if (memberGang == defendingGang)
            {
                DecrementDefenderReinforcements();
            }
            else if (memberGang == attackingGang)
            {
                DecrementAttackerReinforcements();
            }
        }

        public void DecrementAttackerReinforcements()
        {
            if (ModOptions.instance.lockCurWarReinforcementCount) return;

            attackerReinforcements--;

            //have we lost too many? its a victory for the defenders then
            if (attackerReinforcements <= 0)
            {
                EndWar(true);
            }
            else
            {
                //attack.Caption = attackerReinforcements.ToString();
                OnReinforcementsChanged?.Invoke(this);
            }
        }

        public void DecrementDefenderReinforcements()
        {
            if (ModOptions.instance.lockCurWarReinforcementCount) return;

            defenderReinforcements--;

            if (defenderReinforcements <= 0)
            {
                EndWar(false);
            }
            else
            {
                //alliedNumText.Caption = defenderReinforcements.ToString();
                OnReinforcementsChanged?.Invoke(this);
            }
        }

        public void DecrementSpawnedsFromGang(Gang gang)
        {
            if(gang == defendingGang)
            {
                DecrementSpawnedsNumber(true);
            }
            else if(gang == attackingGang)
            {
                DecrementSpawnedsNumber(false);
            }
        }

        public void DecrementSpawnedsNumber(bool memberWasDefender)
        {
            if (memberWasDefender)
            {
                spawnedDefenders--;
                if (spawnedDefenders < 0) spawnedDefenders = 0;
            }
            else
            {
                spawnedAttackers--;
                if (spawnedAttackers < 0) spawnedAttackers = 0;
            }
        }

        /// <summary>
        /// if one of the involved gangs has too many or too few members,
        /// attempts to remove exceeding members from the involved gangs or any "interfering" ones
        /// </summary>
        public void ReassureWarBalance()
        {
            Logger.Log("war balancing: start", 3);

            if (!ModOptions.instance.warMemberCullingForBalancingEnabled)
            {
                Logger.Log("war balancing: abort, warMemberCullingForBalancingEnabled is disabled", 3);
                return;
            }

            List<SpawnedGangMember> allLivingMembers =
                SpawnManager.instance.GetAllLivingMembers();

            int minSpawns = ModOptions.instance.minSpawnsForEachSideDuringWars;

            Logger.Log("war balancing: maxAtkers = " + maxSpawnedAttackers.ToString(), 4);
            Logger.Log("war balancing: maxDefers = " + maxSpawnedDefenders.ToString(), 4);
            Logger.Log("war balancing: spawnedAttackers = " + spawnedAttackers.ToString(), 4);
            Logger.Log("war balancing: spawnedDefenders = " + spawnedDefenders.ToString(), 4);

            foreach (SpawnedGangMember member in allLivingMembers)
            {
                if((spawnedAttackers >= minSpawns && spawnedAttackers <= maxSpawnedAttackers) &&
                   (spawnedDefenders >= minSpawns && spawnedDefenders <= maxSpawnedDefenders))
                {
                    Logger.Log("war balancing: done culling, spawns are balanced", 4);
                    break;
                }

                if (member.watchedPed == null) continue;
                //don't attempt to cull a friendly driving member because they could be a backup car called by the player...
                //and the player can probably take more advantage of any stuck friendly vehicle than the AI can
                if ((!member.myGang.isPlayerOwned || !Function.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, member.watchedPed, false)) &&
                    !member.watchedPed.IsOnScreen)
                {
                    //ok, it's fine to cull this member...
                    //but is it necessary right now?
                    int numThirdPartyMembers = SpawnManager.instance.livingMembersCount - spawnedAttackers - spawnedDefenders;
                    if((member.myGang == attackingGang && spawnedAttackers > maxSpawnedAttackers) ||
                       (member.myGang == defendingGang && spawnedDefenders > maxSpawnedDefenders) ||
                       (!IsGangFightingInThisWar(member.myGang) && 
                       numThirdPartyMembers / (float) SpawnManager.instance.livingMembersCount >= ModOptions.instance.maxThirdPartyMemberPercentIfCullingEnabled &&
                            (spawnedAttackers < minSpawns ||
                             spawnedDefenders < minSpawns)))
                    {
                        Logger.Log("war balancing: culled member from " + member.myGang.name, 4);
                        member.Die(true);
                    }
                }
            }

            Logger.Log("war balancing: end", 3);
        }

        /// <summary>
        /// true if one of the sides has no spawns and all car spawns are occupied by other gangs' cars
        /// </summary>
        /// <returns></returns>
        public bool IsOneOfTheSidesInNeedOfACarSpawn()
        {
            if (SpawnManager.instance.HasThinkingDriversLimitBeenReached())
            {
                return (defenderSpawnPoints.Count == 0 && SpawnManager.instance.GetSpawnedDriversOfGang(defendingGang).Count == 0) ||
                        (attackerSpawnPoints.Count == 0 && SpawnManager.instance.GetSpawnedDriversOfGang(attackingGang).Count == 0);
            }
            

            return false;
        }


        #endregion



        /// <summary>
        /// true if the position is in the war zone or close enough to one of the war area blips
        /// </summary>
        /// <returns></returns>
        public bool IsPositionInsideWarzone(Vector3 position)
        {
            if (warZone.IsLocationInside(ZoneManager.LegacyGetZoneName(World.GetZoneDisplayName(position)), position)) return true;

            foreach (Blip warAreaBlip in warAreaBlips)
            {
                if (warAreaBlip != null && warAreaBlip.Position != default)
                {
                    if (warAreaBlip.Position.DistanceTo2D(position) < ModOptions.instance.maxDistToWarBlipBeforePlayerLeavesWar)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// forces the hate relation level between the involved gangs (includes the player, if not a spectator)
        /// </summary>
        public void SetHateRelationsBetweenGangs()
        {
            attackingGang.relGroup.SetRelationshipBetweenGroups(defendingGang.relGroup, Relationship.Hate, true);

            if (!ModOptions.instance.protagonistsAreSpectators && IsPlayerGangInvolved())
            {
                Gang enemyGang = defendingGang == GangManager.instance.PlayerGang ? attackingGang : defendingGang;
                enemyGang.relGroup.SetRelationshipBetweenGroups(Game.Player.Character.RelationshipGroup, Relationship.Hate, true);
            }
        }

        

        public bool IsPlayerGangInvolved()
        {
            return attackingGang == GangManager.instance.PlayerGang || defendingGang == GangManager.instance.PlayerGang;
        }

        /// <summary>
        /// always returns the defender if the player's gang isn't involved
        /// </summary>
        /// <returns></returns>
        public Gang GetEnemyGangIfPlayerInvolved()
        {
            return defendingGang == GangManager.instance.PlayerGang ? attackingGang : defendingGang;
        }

        public bool IsGangFightingInThisWar(Gang gang)
        {
            return defendingGang == gang || attackingGang == gang;
        }

        /// <summary>
        /// true if the player is in the war zone or close enough to one of the war area blips
        /// </summary>
        /// <returns></returns>
        public bool IsPlayerCloseToWar()
        {
            return IsPositionInsideWarzone(MindControl.CurrentPlayerCharacter.Position);
        }


        public void OnBecameFocusedWar()
        {
            isFocused = true;
            UpdateDisplayForAllControlPoints();

            bool playerGangInvolved = IsPlayerGangInvolved();

            spawnedDefenders = SpawnManager.instance.GetSpawnedMembersOfGang(defendingGang).Count;
            spawnedAttackers = SpawnManager.instance.GetSpawnedMembersOfGang(attackingGang).Count;

            //if it's an AIvsAI fight, add the number of currently spawned members to the tickets!
            //this should prevent large masses of defenders from going poof when defending their newly taken zone
            if (!playerGangInvolved && controlPoints.Count < desiredNumberOfControlPointsForThisWar &&
                ModOptions.instance.addAlreadySpawnedMembersToWarRequiredKills)
            {
                defenderReinforcements += spawnedDefenders;
                attackerReinforcements += spawnedAttackers;
            }

            if (warAreaBlips[1] == null)
            {
                warAreaBlips[1] = World.CreateBlip(MindControl.SafePositionNearPlayer,
                ModOptions.instance.maxDistToWarBlipBeforePlayerLeavesWar);
                warAreaBlips[1].Sprite = BlipSprite.BigCircle;
                warAreaBlips[1].Color = BlipColor.Red;
                warAreaBlips[1].Alpha = 175;
            }
        }

        public void OnNoLongerFocusedWar()
        {
            isFocused = false;
            HideAllControlPoints();
            //hide the "redder" area blip
            if (warAreaBlips[1] != null)
            {
                warAreaBlips[1].Delete();
                warAreaBlips[1] = null;
            }
        }

        public override void Update()
        {
            if (IsPlayerCloseToWar())
            {
                Logger.Log("warmanager inside war tick: begin. spDefenders: " + spawnedDefenders.ToString() + " spAttackers: " + spawnedAttackers.ToString(), 5);
                if (!playerNearWarzone)
                {
                    OnPlayerEnteredWarzone?.Invoke(this);
                }
                playerNearWarzone = true;

                if (isFocused)
                {
                    int curTime = ModCore.curGameTime;
                    msTimeOfLastAutoResolveStep = curTime;

                    if (ModOptions.instance.freezeWantedLevelDuringWars)
                    {
                        Function.Call(Hash.SET_WANTED_LEVEL_MULTIPLIER, 0.0f);
                    }


                    if (curTime - msTimeOfLastCarSpawn > MS_TIME_BETWEEN_CAR_SPAWNS && RandoMath.RandomBool())
                    {
                        SpawnAngryVehicle((spawnedAttackers > spawnedDefenders || spawnedAttackers >= maxSpawnedAttackers) && spawnedDefenders < maxSpawnedDefenders);

                        msTimeOfLastCarSpawn = curTime;
                    }

                    if (curTime - msTimeOfLastBalanceCheck > MS_TIME_BETWEEN_BALANCE_CHECKS)
                    {
                        msTimeOfLastBalanceCheck = curTime;

                        int maxSpawns = allowedSpawnLimit - ModOptions.instance.minSpawnsForEachSideDuringWars;
                        //control max spawns, so that a gang with 5 tickets won't spawn as much as before
                        defenderReinforcementsAdvantage = defenderReinforcements / (float)(attackerReinforcements + defenderReinforcements);

                        maxSpawnedDefenders = RandoMath.ClampValue((int)(maxSpawns * defenderReinforcementsAdvantage),
                            ModOptions.instance.minSpawnsForEachSideDuringWars,
                            RandoMath.ClampValue(defenderReinforcements, ModOptions.instance.minSpawnsForEachSideDuringWars, maxSpawns));

                        maxSpawnedAttackers = RandoMath.ClampValue(allowedSpawnLimit - maxSpawnedDefenders,
                            ModOptions.instance.minSpawnsForEachSideDuringWars,
                            RandoMath.ClampValue
                                (attackerReinforcements,
                                ModOptions.instance.minSpawnsForEachSideDuringWars,
                                maxSpawns));

                        ReassureWarBalance();

                    }


                    if (controlPoints.Count < desiredNumberOfControlPointsForThisWar)
                    {
                        if (controlPoints.Count > 0)
                        {
                            if (availableNearbyPresetSpawns.Count > 0)
                            {
                                int presetSpawnIndex = RandoMath.CachedRandom.Next(availableNearbyPresetSpawns.Count);
                                //we consider control point ownership recommendations only after giving at least 1 spawn for each side
                                bool considerRecommendations = attackerSpawnPoints.Count >= 1 && defenderSpawnPoints.Count >= 1;

                                Gang targetOwnerGang = considerRecommendations ?
                                    GangWarManager.instance.PickOwnerGangForControlPoint(availableNearbyPresetSpawns[presetSpawnIndex], this) :
                                    attackerSpawnPoints.Count >= 1 ? defendingGang : attackingGang;

                                TrySetupAControlPoint(availableNearbyPresetSpawns[presetSpawnIndex],
                                    targetOwnerGang);
                                
                                //remove this potential spawn, even if we fail,
                                //so that we don't spend time testing (and failing) again
                                availableNearbyPresetSpawns.RemoveAt(presetSpawnIndex);
                            }
                            else
                            {
                                TrySetupAControlPoint(SpawnManager.instance.FindCustomSpawnPoint
                                    (controlPoints[0].position,
                                    RandoMath.CachedRandom.Next(ModOptions.instance.minDistanceBetweenWarSpawns, ModOptions.instance.maxDistanceBetweenWarSpawns),
                                    ModOptions.instance.minDistanceBetweenWarSpawns,
                                    5),
                                    defenderSpawnPoints.Count <= desiredNumberOfControlPointsForThisWar * defenderReinforcementsAdvantage ? defendingGang : attackingGang);
                            }
                        }
                        else
                        {
                            if (PrepareAndSetupInitialSpawnPoint(MindControl.SafePositionNearPlayer))
                            {
                                //if setting spawns succeeded this time, place the second war area here if it still hasn't been placed
                                if (warAreaBlips[1] == null)
                                {
                                    warAreaBlips[1] = World.CreateBlip(MindControl.SafePositionNearPlayer,
                                    ModOptions.instance.maxDistToWarBlipBeforePlayerLeavesWar);
                                    warAreaBlips[1].Sprite = BlipSprite.BigCircle;
                                    warAreaBlips[1].Color = BlipColor.Red;
                                    warAreaBlips[1].Alpha = 175;
                                }
                            }
                        }

                    }
                    else
                    {
                        //(we don't start punishing before setting up the desired number of CPs)
                        if(ModOptions.instance.msTimeBetweenWarPunishingForNoSpawns > 0 &&
                            curTime - msTimeOfLastNoSpawnsPunishment > ModOptions.instance.msTimeBetweenWarPunishingForNoSpawns)
                        {
                            msTimeOfLastNoSpawnsPunishment = curTime;

                            //decrement reinforcements of any side with no spawn points!
                            if(attackerSpawnPoints.Count == 0)
                            {
                                DecrementAttackerReinforcements();
                            }

                            if(defenderSpawnPoints.Count == 0)
                            {
                                DecrementDefenderReinforcements();
                            }
                        }
                    }


                    if (SpawnManager.instance.livingMembersCount < allowedSpawnLimit)
                    {
                        SpawnMember((spawnedAttackers > spawnedDefenders || spawnedAttackers >= maxSpawnedAttackers) && spawnedDefenders < maxSpawnedDefenders);
                    }

                    //check one of the control points for capture
                    if (controlPoints.Count > 0)
                    {
                        if (nextCPIndexToCheckForCapture >= controlPoints.Count)
                        {
                            nextCPIndexToCheckForCapture = 0;
                        }

                        WarControlPoint curCheckedCP = controlPoints[nextCPIndexToCheckForCapture];

                        bool pointIsSafe = (((ModCore.curGameTime - msTimeWarStarted < ModOptions.instance.msTimeBeforeEnemySpawnsCanBeCaptured) && curCheckedCP.ownerGang != null) ||
                            !curCheckedCP.CheckIfHasBeenCaptured());

                        if (pointIsSafe && curCheckedCP.onCaptureCooldown)
                        {
                            ControlPointHasCooledDown(curCheckedCP);
                        }

                        nextCPIndexToCheckForCapture++;
                    }
                }

                Logger.Log("warmanager inside war tick: end", 5);
            }
            else
            {
                if (playerNearWarzone)
                {
                    OnPlayerLeftWarzone?.Invoke(this);
                }

                playerNearWarzone = false;
                if (ModCore.curGameTime - msTimeOfLastAutoResolveStep > ModOptions.instance.msTimeBetweenWarAutoResolveSteps)
                {
                    RunAutoResolveStep(1.15f);
                }
            }
            //if the player's gang leader is dead...
            if (!Game.Player.IsAlive && !MindControl.HasChangedBody)
            {
                RunAutoResolveStep(1.05f);
                return;
            }
        }

        public override void ResetUpdateInterval()
        {
            ticksBetweenUpdates = GangWarManager.TICKS_BETWEEN_WAR_UPDATES;
        }

        public void Abort()
        {
            if (warBlip != null)
            {
                warBlip.Delete();

                foreach (Blip areaBlip in warAreaBlips)
                {
                    if (areaBlip != null)
                        areaBlip.Delete();
                }

            }

            PoolAllControlPoints();
        }
    }
}
