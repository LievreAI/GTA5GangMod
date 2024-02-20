﻿using GTA.Native;
using System.Collections.Generic;

namespace GTA.GangAndTurfMod
{
    public class GangAI : UpdatedClass
    {
        public Gang watchedGang;

        private List<TurfZone> myZones;

        public override void Update()
        {
            Logger.Log("gang ai update: begin", 5);

            //everyone tries to expand before anything else;
            //that way, we don't end up with isolated gangs or some kind of peace
            myZones = ZoneManager.instance.GetZonesControlledByGang(watchedGang.name);
            TryExpand();

            switch (watchedGang.upgradeTendency)
            {
                case Gang.AIUpgradeTendency.bigGuns:
                    TryUpgradeGuns();
                    TryUpgradeGuns(); //yeah, we like guns
                    TryUpgradeZones();
                    if (RandoMath.RandomBool()) TryUpgradeMembers(); //but we're not buff
                    break;
                case Gang.AIUpgradeTendency.moreExpansion:
                    TryExpand();
                    TryExpand(); //that's some serious expansion
                    TryUpgradeGuns();
                    TryUpgradeMembers();
                    if (RandoMath.RandomBool()) TryUpgradeZones(); //...but zone strength isn't our priority
                    break;
                case Gang.AIUpgradeTendency.toughMembers:
                    TryUpgradeMembers();
                    TryUpgradeMembers();
                    TryUpgradeGuns();
                    if (RandoMath.RandomBool()) TryUpgradeZones(); //tough members, but tend to be few in number
                    break;
                case Gang.AIUpgradeTendency.toughTurf:
                    TryUpgradeZones();
                    TryUpgradeZones(); //lots of defenders!
                    TryUpgradeMembers();
                    if (RandoMath.RandomBool()) TryUpgradeGuns(); //...with below average guns
                    break;
            }

            //lets check our financial situation:
            //are we running very low on cash (unable to take even a neutral territory)?
            //do we have any turf? are currently fighting a war?
            //if not, we no longer exist
            if (watchedGang.moneyAvailable < ModOptions.instance.baseCostToTakeTurf)
            {
                if (!GangWarManager.instance.IsGangFightingAWar(watchedGang))
                {
                    myZones = ZoneManager.instance.GetZonesControlledByGang(watchedGang.name);
                    if (myZones.Count == 0)
                    {
                        if (ModOptions.instance.gangsCanBeWipedOut)
                        {
                            GangManager.instance.KillGang(this);
                        }
                        else
                        {
                            //we get some money then, at least to keep trying to fight
                            watchedGang.AddMoney((int)(ModOptions.instance.baseCostToTakeTurf * 5 * ModOptions.instance.extraProfitForAIGangsFactor));
                        }

                    }
                }
            }

            Logger.Log("gang ai update: end", 5);

        }

        private void TryExpand()
        {
            //lets attack!
            //pick a random zone owned by us, get the closest hostile zone and attempt to take it
            //..but only if the player hasn't disabled expansion, and we're not involved in too many wars already
            if (ModOptions.instance.preventAIExpansion) return;

            if (GangWarManager.instance.GetAllCurrentWarsInvolvingGang(watchedGang).Count >= ModOptions.instance.maxNumWarsAiGangCanBeInvolvedIn) return;

            if (myZones.Count > 0)
            {
                TurfZone chosenZone = RandoMath.RandomElement(myZones);
                TurfZone closestZoneToChosen = ZoneManager.instance.GetClosestZoneToTargetZone(chosenZone, true);
                TryTakeTurf(closestZoneToChosen);
            }
            else
            {
                //we're out of turf!
                //get a random zone (preferably neutral, since it's cheaper for the AI) and try to take it
                //but only sometimes, since we're probably on a tight spot
                TurfZone chosenZone = ZoneManager.instance.GetRandomZone(true);
                TryTakeTurf(chosenZone);
            }
        }

        private void TryUpgradeGuns()
        {
            //try to buy the weapons we like
            if (watchedGang.preferredWeaponHashes.Count == 0)
            {
                watchedGang.SetPreferredWeapons();
            }

            WeaponHash chosenWeapon = RandoMath.RandomElement(watchedGang.preferredWeaponHashes);

            if (!watchedGang.gangWeaponHashes.Contains(chosenWeapon))
            {
                //maybe the chosen weapon can no longer be bought
                if (ModOptions.instance.GetBuyableWeaponByHash(chosenWeapon) == null)
                {
                    watchedGang.preferredWeaponHashes.Remove(chosenWeapon);
                    GangManager.instance.SaveGangData(false);
                    return;
                }

                if (watchedGang.moneyAvailable >= ModOptions.instance.GetBuyableWeaponByHash(chosenWeapon).price)
                {
                    watchedGang.AddMoney(-ModOptions.instance.GetBuyableWeaponByHash(chosenWeapon).price);
                    watchedGang.gangWeaponHashes.Add(chosenWeapon);
                    GangManager.instance.SaveGangData(false);
                }
            }
        }

        private void TryUpgradeMembers()
        {
            //since we've got some extra cash, lets upgrade our members!
            switch (RandoMath.CachedRandom.Next(3))
            {
                case 0: //accuracy!
                    if (watchedGang.memberAccuracyLevel < ModOptions.instance.maxGangMemberAccuracy &&
                watchedGang.moneyAvailable >= GangCalculations.CalculateAccuracyUpgradeCost(watchedGang.memberAccuracyLevel))
                    {
                        watchedGang.AddMoney(-GangCalculations.CalculateAccuracyUpgradeCost(watchedGang.memberAccuracyLevel));
                        watchedGang.memberAccuracyLevel += ModOptions.instance.GetAccuracyUpgradeIncrement();
                        if (watchedGang.memberAccuracyLevel > ModOptions.instance.maxGangMemberAccuracy)
                        {
                            watchedGang.memberAccuracyLevel = ModOptions.instance.maxGangMemberAccuracy;
                        }

                        GangManager.instance.SaveGangData(false);
                    }
                    break;
                case 1: //armor!
                    if (watchedGang.memberArmor < ModOptions.instance.maxGangMemberArmor &&
                            watchedGang.moneyAvailable >= GangCalculations.CalculateArmorUpgradeCost(watchedGang.memberArmor))
                    {
                        watchedGang.AddMoney(-GangCalculations.CalculateArmorUpgradeCost(watchedGang.memberArmor));
                        watchedGang.memberArmor += ModOptions.instance.GetArmorUpgradeIncrement();

                        if (watchedGang.memberArmor > ModOptions.instance.maxGangMemberArmor)
                        {
                            watchedGang.memberArmor = ModOptions.instance.maxGangMemberArmor;
                        }

                        GangManager.instance.SaveGangData(false);
                    }
                    break;

                default: //health!
                    if (watchedGang.memberHealth < ModOptions.instance.maxGangMemberHealth &&
                            watchedGang.moneyAvailable >= GangCalculations.CalculateHealthUpgradeCost(watchedGang.memberHealth))
                    {
                        watchedGang.AddMoney(-GangCalculations.CalculateHealthUpgradeCost(watchedGang.memberHealth));
                        watchedGang.memberHealth += ModOptions.instance.GetHealthUpgradeIncrement();

                        if (watchedGang.memberHealth > ModOptions.instance.maxGangMemberHealth)
                        {
                            watchedGang.memberHealth = ModOptions.instance.maxGangMemberHealth;
                        }

                        GangManager.instance.SaveGangData(false);
                    }
                    break;
            }

        }

        private void TryUpgradeZones()
        {
            int upgradeCost = GangCalculations.CalculateGangValueUpgradeCost(watchedGang.baseTurfValue);
            //upgrade the whole gang strength if possible!
            //lets not get more upgrades here than the player. it may get too hard for the player to catch up otherwise
            if (watchedGang.moneyAvailable >= upgradeCost &&
                watchedGang.baseTurfValue <= GangManager.instance.PlayerGang.baseTurfValue - 1)
            {
                watchedGang.AddMoney(-upgradeCost);
                watchedGang.baseTurfValue++;
                GangManager.instance.SaveGangData(false);
                return;
            }
            //if we have enough money to upgrade a zone,
            //try upgrading our toughest zone... or one that we can afford upgrading
            int lastCheckedValue = ModOptions.instance.maxTurfValue;
            for (int i = 0; i < myZones.Count; i++)
            {
                if (myZones[i].value >= lastCheckedValue) continue; //we already know we can't afford upgrading from this turf level
                upgradeCost = GangCalculations.CalculateTurfValueUpgradeCost(myZones[i].value);
                if (watchedGang.moneyAvailable >= upgradeCost && !myZones[i].IsBeingContested())
                {
                    watchedGang.AddMoney(-upgradeCost);
                    myZones[i].ChangeValue(myZones[i].value + 1);
                    ZoneManager.instance.SaveZoneData(false);
                    return;
                }
                else
                {
                    lastCheckedValue = myZones[i].value;
                }
            }
        }

        private void TryTakeTurf(TurfZone targetZone)
        {
            if (targetZone == null || targetZone.ownerGangName == watchedGang.name) return; //whoops, there just isn't any zone available for our gang
            if (targetZone.ownerGangName == "none")
            {
                //this zone is neutral, lets just take it
                if (watchedGang.moneyAvailable >= ModOptions.instance.baseCostToTakeTurf)
                {
                    watchedGang.AddMoney(-ModOptions.instance.baseCostToTakeTurf);
                    watchedGang.TakeZone(targetZone);
                }
            }
            else
            {
                TryStartFightForZone(targetZone);
            }
        }

        /// <summary>
        /// if fighting is enabled and the targetzone is controlled by an enemy, attack it! ... But only if it's affordable.
        /// if we're desperate we do it anyway
        /// </summary>
        /// <param name="targetZone"></param>
        private void TryStartFightForZone(TurfZone targetZone)
        {
            Gang ownerGang = GangManager.instance.GetGangByName(targetZone.ownerGangName);

            if (ownerGang == null)
            {
                Logger.Log("Gang with name " + targetZone.ownerGangName + " no longer exists; assigning all owned turf to 'none'", 1);
                ZoneManager.instance.GiveGangZonesToAnother(targetZone.ownerGangName, "none");

                //this zone was controlled by a gang that no longer exists. it is neutral now
                if (watchedGang.moneyAvailable >= ModOptions.instance.baseCostToTakeTurf)
                {
                    watchedGang.AddMoney(-ModOptions.instance.baseCostToTakeTurf);
                    watchedGang.TakeZone(targetZone);
                }
            }
            else
            {
                if(watchedGang.moneyAvailable < 0)
                {
                    // things are bad!
                    // if we don't already own at least one zone, we should be wiped out instead of force-attacking in this case
                    return;
                }

                if (GangWarManager.instance.IsZoneContested(targetZone) ||
                    (ownerGang.isPlayerOwned && 
                        GangWarManager.instance.GetAllCurrentWarsInvolvingGang(ownerGang).Count >= ModOptions.instance.maxConcurrentWarsAgainstPlayer))
                {
                    //don't mess with this zone then, it's a warzone
                    return;
                }
                //we check how well defended this zone is,
                //then figure out how large our attack should be.
                //if we can afford that attack, we do it
                int defenderStrength = GangCalculations.CalculateDefenderStrength(ownerGang, targetZone);
                GangWarManager.AttackStrength requiredStrength =
                    GangCalculations.CalculateRequiredAttackStrength(watchedGang, defenderStrength);

                int atkCost = GangCalculations.CalculateAttackCost(watchedGang, requiredStrength);

                if (watchedGang.moneyAvailable < atkCost)
                {
                    if (myZones.Count == 0)
                    {
                        //if we're out of turf and cant afford a decent attack, lets just attack anyway
                        //we use a light attack and do it even if that means our money gets negative.
                        //this should make gangs get back in the game or be wiped out instead of just staying away
                        requiredStrength = GangWarManager.AttackStrength.light;
                        atkCost = GangCalculations.CalculateAttackCost(watchedGang, requiredStrength);
                    }
                    else
                    {
                        return; //hopefully we can just find a cheaper fight
                    }
                }

                if (targetZone.ownerGangName != GangManager.instance.PlayerGang.name ||
                    (ModOptions.instance.warAgainstPlayerEnabled && GangWarManager.instance.CanStartWarAgainstPlayer &&
                        targetZone.ownerGangName == GangManager.instance.PlayerGang.name))
                {

                    if (ModOptions.instance.survivorsBecomeZoneValueOnAttackerVictory)
                    {
                        // if spare attackers will be used as quick upgrades, it's worth considering bigger attacks
                        if(requiredStrength != GangWarManager.AttackStrength.massive)
                        {
                            int massiveAtkCost = GangCalculations.CalculateAttackCost(watchedGang, GangWarManager.AttackStrength.massive);
                            // but don't do it if it'll cost too much for us
                            if(massiveAtkCost <= watchedGang.moneyAvailable / 3)
                            {
                                atkCost = massiveAtkCost;
                                requiredStrength = GangWarManager.AttackStrength.massive;
                            }
                        }
                    }

                    if (GangWarManager.instance.TryStartWar(watchedGang, targetZone, requiredStrength))
                    {
                        watchedGang.AddMoney(-atkCost);
                    }
                }
                
            }
        }

        /// <summary>
        /// if this gang seems to be new, makes it take up to 3 neutral zones
        /// </summary>
        private void DoInitialTakeover()
        {

            if (watchedGang.gangWeaponHashes.Count > 0 || ZoneManager.instance.GetZonesControlledByGang(watchedGang.name).Count > 2)
            {
                //we've been around for long enough to get weapons or get turf, abort
                return;
            }

            TurfZone chosenZone = ZoneManager.instance.GetRandomZone(true);

            if (chosenZone.ownerGangName == "none")
            {
                watchedGang.TakeZone(chosenZone, false);
                //we took one, now we should spread the influence around it
                for (int i = 0; i < 3; i++)
                {
                    TurfZone nearbyZone = ZoneManager.instance.GetClosestZoneToTargetZone(chosenZone, true);
                    if (nearbyZone.ownerGangName == "none")
                    {
                        watchedGang.TakeZone(nearbyZone, false);
                        //and use this new zone as reference from now on
                        chosenZone = nearbyZone;
                    }
                }
            }
            else
            {
                //no neutral turf available, abort!
                return;
            }
        }

        public override void ResetUpdateInterval()
        {
            ticksBetweenUpdates = ModOptions.instance.ticksBetweenGangAIUpdates + RandoMath.CachedRandom.Next(100);
            ticksSinceLastUpdate = ticksBetweenUpdates;
        }

        public GangAI(Gang watchedGang)
        {
            this.watchedGang = watchedGang;
            ResetUpdateInterval();

            //have some turf for free! but only if you're new around here
            DoInitialTakeover();

            //do we have vehicles?
            if (this.watchedGang.carVariations.Count == 0)
            {
                //get some vehicles!
                for (int i = 0; i < RandoMath.CachedRandom.Next(1, 4); i++)
                {
                    PotentialGangVehicle newVeh = PotentialGangVehicle.GetCarFromPool();
                    if (newVeh != null)
                    {
                        this.watchedGang.AddGangCar(newVeh);
                    }
                }
            }
        }

    }
}
