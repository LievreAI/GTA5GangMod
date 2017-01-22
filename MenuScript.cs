﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GTA.Native;
using NativeUI;
using System.Windows.Forms;


namespace GTA.GangAndTurfMod
{
    /// <summary>
    /// the nativeUI-implementing class
    /// -------------thanks to the NativeUI developers!-------------
    /// </summary>
    public class MenuScript
    {

        MenuPool menuPool;
        UIMenu zonesMenu, gangMenu, memberMenu, carMenu, gangOptionsSubMenu, 
            modSettingsSubMenu;
        Ped closestPed;
        
        PotentialGangMember memberToSave = new PotentialGangMember();
        int memberStyle = 0, memberColor = 0;

        int healthUpgradeCost, armorUpgradeCost, accuracyUpgradeCost;

        Dictionary<ModOptions.BuyableWeapon, UIMenuCheckboxItem> weaponEntries = 
            new Dictionary<ModOptions.BuyableWeapon, UIMenuCheckboxItem>();

        Dictionary<VehicleColor, UIMenuItem> carColorEntries =
            new Dictionary<VehicleColor, UIMenuItem>();

        int playerGangOriginalBlipColor = 0;
        Dictionary<string, int> blipColorEntries = new Dictionary<string, int>
        {
            {"white", 0 },
            {"red", 1 },
            {"green", 2 },
            {"blue", 3 },
            {"orange", 17 },
            {"purple", 19 },
            {"gray", 20 },
            {"brown", 21 },
            {"pink", 23 },
            {"dark green", 25 },
            {"dark purple", 27 },
            {"black", 29 }, //not really black, but...
            {"yellow", 66 },
        };

        UIMenuItem healthButton, armorButton, accuracyButton, takeZoneButton,
            openGangMenuBtn, openZoneMenuBtn, mindControlBtn, addToGroupBtn, carBackupBtn, paraBackupBtn;

        private int ticksSinceLastCarBkp = 5000, ticksSinceLastParaBkp = 5000;

        public UIMenuListItem aggOption;

        public enum desiredInputType
        {
            none,
            enterGangName,
            changeKeyBinding
        }

        public enum changeableKeyBinding
        {
            GangMenuBtn,
            ZoneMenuBtn,
            MindControlBtn,
            AddGroupBtn,
        }

        public desiredInputType curInputType = desiredInputType.none;
        public changeableKeyBinding targetKeyBindToChange = changeableKeyBinding.AddGroupBtn;

        public static MenuScript instance;

        public MenuScript()
        {
            instance = this;
            zonesMenu = new UIMenu("Gang Mod", "Zone Controls");
            memberMenu = new UIMenu("Gang Mod", "Gang Member Registration Controls");
            carMenu = new UIMenu("Gang Mod", "Gang Vehicle Registration Controls");
            gangMenu = new UIMenu("Gang Mod", "Gang Controls");
            menuPool = new MenuPool();
            menuPool.Add(zonesMenu);
            menuPool.Add(gangMenu);
            menuPool.Add(memberMenu);
            menuPool.Add(carMenu);

            AddGangTakeoverButton();
            AddSaveZoneButton();
            //AddZoneCircleButton();
            
            AddMemberStyleChoices();
            AddSaveMemberButton();
            AddNewPlayerGangMemberButton();
            AddRemovePlayerGangMemberButton();
            AddRemoveFromAllGangsButton();
            AddMakeFriendlyToPlayerGangButton();

            AddCallVehicleButton();
            AddCallParatrooperButton();
            AddSaveVehicleButton();
            AddRegisterPlayerVehicleButton();
            AddRemovePlayerVehicleButton();
            AddRemoveVehicleEverywhereButton();

            UpdateBuyableWeapons();
            AddGangOptionsSubMenu();
            AddModSettingsSubMenu();

            zonesMenu.RefreshIndex();
            gangMenu.RefreshIndex();
            memberMenu.RefreshIndex();

            aggOption.Index = (int)ModOptions.instance.gangMemberAggressiveness;

            //add mouse click as another "select" button
            menuPool.SetKey(UIMenu.MenuControls.Select, Control.PhoneSelect);
            InstructionalButton clickButton = new InstructionalButton(Control.PhoneSelect, "Select");
            zonesMenu.AddInstructionalButton(clickButton);
            gangMenu.AddInstructionalButton(clickButton);
            memberMenu.AddInstructionalButton(clickButton);

            ticksSinceLastCarBkp = ModOptions.instance.ticksCooldownBackupCar;
            ticksSinceLastParaBkp = ModOptions.instance.ticksCooldownParachutingMember;
        }

        #region menu opening methods
        public void OpenGangMenu()
        {
            if (!menuPool.IsAnyMenuOpen())
            {
                UpdateUpgradeCosts();
                UpdateBuyableWeapons();
                gangMenu.Visible = !gangMenu.Visible;
            }
        }

        public void OpenContextualRegistrationMenu()
        {
            if (!menuPool.IsAnyMenuOpen())
            {
                if(Game.Player.Character.CurrentVehicle == null)
                {
                    closestPed = World.GetClosestPed(Game.Player.Character.Position + Game.Player.Character.ForwardVector * 6.0f, 5.5f);
                    if (closestPed != null)
                    {
                        UI.ShowSubtitle("ped selected!");
                        //World.DrawMarker(MarkerType.VerticalCylinder, closestPed.Position, Math.Vector3.WorldUp, Math.Vector3.Zero, new Math.Vector3(1,1,1), System.Drawing.Color.
                        //World.DrawSpotLight(closestPed.Position + Math.Vector3.WorldUp, Math.Vector3.WorldDown, System.Drawing.Color.Azure, 5, 5, 5, 2, 500);
                        World.AddExplosion(closestPed.Position, ExplosionType.WaterHydrant, 1.0f, 0.1f);
                        memberMenu.Visible = !memberMenu.Visible;
                    }
                    else
                    {
                        UI.ShowSubtitle("couldn't find a ped in front of you!");
                    }
                }
                else
                {
                    UI.ShowSubtitle("vehicle selected!");
                    carMenu.Visible = !carMenu.Visible;
                }
                
            }
        }

        public void OpenZoneMenu()
        {
            if (!menuPool.IsAnyMenuOpen())
            {
                ZoneManager.instance.OutputCurrentZoneInfo();
                zonesMenu.Visible = !zonesMenu.Visible;
            }
        }
        #endregion


        public void Tick()
        {
            menuPool.ProcessMenus();

            if (curInputType == desiredInputType.enterGangName)
            {
                int inputFieldSituation = Function.Call<int>(Hash.UPDATE_ONSCREEN_KEYBOARD);
                if(inputFieldSituation == 1)
                {
                    string typedText = Function.Call<string>(Hash.GET_ONSCREEN_KEYBOARD_RESULT);
                    if(typedText != "none")
                    {
                        ZoneManager.instance.GiveGangZonesToAnother(GangManager.instance.GetPlayerGang().name, typedText);
                        GangManager.instance.GetPlayerGang().name = typedText;
                        GangManager.instance.SaveGangData();

                        UI.ShowSubtitle("Your gang is now known as the " + typedText);
                    }
                    else
                    {
                        UI.ShowSubtitle("That name is not allowed, sorry!");
                    }

                    curInputType = desiredInputType.none;
                }
                else if(inputFieldSituation == 2 || inputFieldSituation == 3)
                {
                    curInputType = desiredInputType.none;
                }
            }

            //countdown for next backups
            ticksSinceLastCarBkp++;
            if (ticksSinceLastCarBkp > ModOptions.instance.ticksCooldownBackupCar)
                ticksSinceLastCarBkp = ModOptions.instance.ticksCooldownBackupCar;
            ticksSinceLastParaBkp++;
            if (ticksSinceLastParaBkp > ModOptions.instance.ticksCooldownParachutingMember)
                ticksSinceLastParaBkp = ModOptions.instance.ticksCooldownParachutingMember;
        }

        void UpdateUpgradeCosts()
        {
            Gang playerGang = GangManager.instance.GetPlayerGang();
            healthUpgradeCost = GangManager.CalculateHealthUpgradeCost(playerGang.memberHealth);
            armorUpgradeCost = GangManager.CalculateArmorUpgradeCost(playerGang.memberArmor);
            accuracyUpgradeCost = GangManager.CalculateAccuracyUpgradeCost(playerGang.memberAccuracyLevel);

            healthButton.Text = "Upgrade Member Health - " + healthUpgradeCost.ToString();
            armorButton.Text = "Upgrade Member Armor - " + armorUpgradeCost.ToString();
            accuracyButton.Text = "Upgrade Member Accuracy - " + accuracyUpgradeCost.ToString();
        }

        void UpdateTakeOverBtnText()
        {
            takeZoneButton.Description = "Makes the zone you are in become part of your gang's turf. If it's not controlled by any gang, it will instantly become yours for a price of $" +
                ModOptions.instance.costToTakeNeutralTurf.ToString() + ". If it belongs to another gang, a battle will begin, for half that price.";
        }

        void UpdateBuyableWeapons()
        {
            Gang playerGang = GangManager.instance.GetPlayerGang();
            List<ModOptions.BuyableWeapon> weaponsList = ModOptions.instance.buyableWeapons;

            for(int i = 0; i < weaponsList.Count; i++)
            {
                if (weaponEntries.ContainsKey(weaponsList[i])){
                    weaponEntries[weaponsList[i]].Checked = playerGang.gangWeaponHashes.Contains(weaponsList[i].wepHash);
                }
                else
                {
                    weaponEntries.Add(weaponsList[i], new UIMenuCheckboxItem
                        (string.Concat(weaponsList[i].wepHash.ToString(), " - ", weaponsList[i].price.ToString()),
                        playerGang.gangWeaponHashes.Contains(weaponsList[i].wepHash)));
                }
            }

        }

        void FillCarColorEntries()
        {
            foreach(ModOptions.GangColorTranslation colorList in ModOptions.instance.similarColors)
            {
                for(int i = 0; i < colorList.vehicleColors.Count; i++)
                {
                    carColorEntries.Add(colorList.vehicleColors[i], new UIMenuItem(colorList.vehicleColors[i].ToString(), "Colors can be previewed if you are inside a vehicle. Click or press enter to confirm the gang color change."));
                }
            }
        }

        public void RefreshKeyBindings()
        {
            openGangMenuBtn.Text = "Gang Control Key - " + ModOptions.instance.openGangMenuKey.ToString();
            openZoneMenuBtn.Text = "Zone Control Key - " + ModOptions.instance.openZoneMenuKey.ToString();
            addToGroupBtn.Text = "Add or Remove Member from Group - " + ModOptions.instance.addToGroupKey.ToString();
            mindControlBtn.Text = "Take Control of Member - " + ModOptions.instance.mindControlKey.ToString();
        }

        #region Zone Menu Stuff

        void AddSaveZoneButton()
        {
            UIMenuItem newButton = new UIMenuItem("Add Current Zone to Takeables", "Makes the zone you are in become takeable by gangs and sets your position as the zone's reference position (if toggled, this zone's blip will show here).");
            zonesMenu.AddItem(newButton);
            zonesMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == newButton)
                {
                    string curZoneName = World.GetZoneName(Game.Player.Character.Position);
                    TurfZone curZone = ZoneManager.instance.GetZoneByName(curZoneName);
                    if (curZone == null)
                    {
                        //add a new zone then
                        curZone = new TurfZone(curZoneName);
                    }

                    //update the zone's blip position even if it already existed
                    curZone.zoneBlipPosition = Game.Player.Character.Position;
                    ZoneManager.instance.UpdateZoneData(curZone);
                    UI.ShowSubtitle("Zone Data Updated!");
                }
            };

        }

        //void AddZoneCircleButton()
        //{
        //    UIMenuItem newButton = new UIMenuItem("New Zone Circle", "Create a new map circle to represent this zone's boundaries.");
        //    zonesMenu.AddItem(newButton);
        //    zonesMenu.OnItemSelect += (sender, item, index) =>
        //    {
        //        if (item == newButton)
        //        {
        //            string curZoneName = World.GetZoneName(Game.Player.Character.Position);
        //            TurfZone curZone = ZoneManager.instance.GetZoneByName(curZoneName);

        //            ZoneManager.instance.AddNewCircleBlip(Game.Player.Character.Position, curZone);
                    
        //            ZoneManager.instance.UpdateZoneData(curZone);
        //            UI.ShowSubtitle("Zone Data Updated!");
        //        }
        //    };
        //}

        void AddGangTakeoverButton()
        {
            takeZoneButton = new UIMenuItem("Take current zone",
                "Makes the zone you are in become part of your gang's turf. If it's not controlled by any gang, it will instantly become yours for a price of $" +
                ModOptions.instance.costToTakeNeutralTurf.ToString() +". If it belongs to another gang, a battle will begin, for half that price.");
            zonesMenu.AddItem(takeZoneButton);
            zonesMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == takeZoneButton)
                {
                    string curZoneName = World.GetZoneName(Game.Player.Character.Position);
                    TurfZone curZone = ZoneManager.instance.GetZoneByName(curZoneName);
                    if (curZone == null)
                    {
                        UI.ShowSubtitle("this zone isn't marked as takeable.");
                    }
                    else
                    {
                       if(curZone.ownerGangName == "none")
                        {
                            if (GangManager.instance.AddOrSubtractMoneyToProtagonist(-ModOptions.instance.costToTakeNeutralTurf))
                            {
                                GangManager.instance.GetPlayerGang().TakeZone(curZone);
                                UI.ShowSubtitle("This zone is " + GangManager.instance.GetPlayerGang().name + " turf now!");
                            }
                            else
                            {
                                UI.ShowSubtitle("You don't have the resources to take over a neutral zone.");
                            }
                        }
                        else
                        {
                            if(curZone.ownerGangName == GangManager.instance.GetPlayerGang().name)
                            {
                                UI.ShowSubtitle("Your gang already owns this zone.");
                            }
                            else
                            if (GangManager.instance.AddOrSubtractMoneyToProtagonist(-ModOptions.instance.costToTakeNeutralTurf / 2, true))
                            {
                                zonesMenu.Visible = !zonesMenu.Visible;
                                if (ModOptions.instance.fightingEnabled)
                                {
                                    if (!GangWarManager.instance.StartWar(GangManager.instance.GetGangByName(curZone.ownerGangName), curZone, GangWarManager.warType.attackingEnemy))
                                    {
                                        UI.ShowSubtitle("A war is already in progress.");
                                    }
                                    else
                                    {
                                        GangManager.instance.AddOrSubtractMoneyToProtagonist(-ModOptions.instance.costToTakeNeutralTurf / 2);
                                    }
                                }
                                else
                                {
                                    UI.ShowSubtitle("Gang Fights must be enabled in order to start a war!");
                                }
                                
                            }
                            else
                            {
                                UI.ShowSubtitle("You don't have the resources to start a battle against another gang.");
                            }
                        }
                    }
                }
            };
        }

        #endregion

        #region register Member/Vehicle Stuff

        void AddMemberStyleChoices()
        {
            List<dynamic> memberStyles = new List<dynamic>
            {
                "Business",
                "Street",
                "Beach",
                "Special"
            };

            List<dynamic> memberColors = new List<dynamic>
            {
                "White",
                "Black",
                "Red",
                "Green",
                "Blue",
                "Yellow",
                "Gray",
                "Pink",
                "Purple"
            };

            UIMenuListItem styleList = new UIMenuListItem("Member Dressing Style", memberStyles, 0, "The way the selected member is dressed. Used by the AI when picking members (if the AI gang's chosen style is the same as this member's, it may choose this member).");
            UIMenuListItem colorList = new UIMenuListItem("Member Color", memberColors, 0, "The color the member will be assigned to. Used by the AI when picking members (if the AI gang's color is the same as this member's, it may choose this member).");
            memberMenu.AddItem(styleList);
            memberMenu.AddItem(colorList);
            memberMenu.OnListChange += (sender, item, index) =>
            {
                if (item == styleList)
                {
                    memberStyle = item.Index;
                }
                else if(item == colorList)
                {
                    memberColor = item.Index;
                }

            };

        }

        void AddSaveMemberButton()
        {
            UIMenuItem newButton = new UIMenuItem("Save Potential Member", "Saves the selected ped as a potential gang member with the specified data. AI gangs will be able to choose him\\her.");
            memberMenu.AddItem(newButton);
            memberMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == newButton)
                {
                    memberToSave.modelHash = closestPed.Model.Hash;

                    memberToSave.headDrawableIndex = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, closestPed, 0);
                    memberToSave.headTextureIndex = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, closestPed, 0);

                    memberToSave.hairDrawableIndex = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, closestPed, 2);

                    memberToSave.torsoDrawableIndex = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, closestPed, 3);
                    memberToSave.torsoTextureIndex = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, closestPed, 3);
                       
                    memberToSave.legsDrawableIndex = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, closestPed, 4);
                    memberToSave.legsTextureIndex = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, closestPed, 4);

                    memberToSave.myStyle = (PotentialGangMember.dressStyle) memberStyle;
                    memberToSave.linkedColor = (PotentialGangMember.memberColor) memberColor;

                    if (PotentialGangMember.AddMemberAndSavePool
                    (new PotentialGangMember(memberToSave.modelHash, memberToSave.myStyle, memberToSave.linkedColor,
                    memberToSave.headDrawableIndex, memberToSave.headTextureIndex, memberToSave.hairDrawableIndex,
                        memberToSave.torsoDrawableIndex, memberToSave.torsoTextureIndex,
                        memberToSave.legsDrawableIndex, memberToSave.legsTextureIndex)))
                    {
                        UI.ShowSubtitle("Potential member added!");
                    }
                    else
                    {
                        UI.ShowSubtitle("A similar potential member already exists.");
                    }
                }
            };
        }

        void AddNewPlayerGangMemberButton()
        {
            UIMenuItem newButton = new UIMenuItem("Save ped type as your gang member", "Saves the selected ped type as a member of your gang, with the specified data. The selected ped himself won't be a member, however.");
            memberMenu.AddItem(newButton);
            memberMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == newButton)
                {

                    memberToSave.modelHash = closestPed.Model.Hash;

                    memberToSave.headDrawableIndex = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, closestPed, 0);
                    memberToSave.headTextureIndex = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, closestPed, 0);

                    memberToSave.hairDrawableIndex = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, closestPed, 2);

                    memberToSave.torsoDrawableIndex = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, closestPed, 3);
                    memberToSave.torsoTextureIndex = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, closestPed, 3);

                    memberToSave.legsDrawableIndex = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, closestPed, 4);
                    memberToSave.legsTextureIndex = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, closestPed, 4);

                    memberToSave.myStyle = (PotentialGangMember.dressStyle)memberStyle;
                    memberToSave.linkedColor = (PotentialGangMember.memberColor)memberColor;

                    if (GangManager.instance.GetPlayerGang().AddMemberVariation(new PotentialGangMember(memberToSave.modelHash,
                        memberToSave.myStyle, memberToSave.linkedColor,
                        memberToSave.headDrawableIndex, memberToSave.headTextureIndex, memberToSave.hairDrawableIndex,
                        memberToSave.torsoDrawableIndex, memberToSave.torsoTextureIndex,
                        memberToSave.legsDrawableIndex, memberToSave.legsTextureIndex)))
                    {
                        UI.ShowSubtitle("Member added successfully!");
                    }
                    else
                    {
                        UI.ShowSubtitle("Your gang already has a similar member.");
                    }
                }
            };
        }

        void AddRemovePlayerGangMemberButton()
        {
            UIMenuItem newButton = new UIMenuItem("Remove ped type from your gang", "If the selected ped type was a member of your gang, it will no longer be. The selected ped himself will still be a member, however.");
            memberMenu.AddItem(newButton);
            memberMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == newButton)
                {

                    memberToSave.modelHash = closestPed.Model.Hash;

                    memberToSave.headDrawableIndex = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, closestPed, 0);
                    memberToSave.headTextureIndex = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, closestPed, 0);

                    memberToSave.hairDrawableIndex = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, closestPed, 2);

                    memberToSave.torsoDrawableIndex = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, closestPed, 3);
                    memberToSave.torsoTextureIndex = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, closestPed, 3);

                    memberToSave.legsDrawableIndex = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, closestPed, 4);
                    memberToSave.legsTextureIndex = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, closestPed, 4);

                    memberToSave.myStyle = (PotentialGangMember.dressStyle)memberStyle;
                    memberToSave.linkedColor = (PotentialGangMember.memberColor)memberColor;

                    if (GangManager.instance.GetPlayerGang().RemoveMemberVariation(new PotentialGangMember(memberToSave.modelHash,
                        memberToSave.myStyle, memberToSave.linkedColor,
                        memberToSave.headDrawableIndex, memberToSave.headTextureIndex, memberToSave.hairDrawableIndex,
                        memberToSave.torsoDrawableIndex, memberToSave.torsoTextureIndex,
                        memberToSave.legsDrawableIndex, memberToSave.legsTextureIndex)))
                    {
                        UI.ShowSubtitle("Member removed successfully!");
                    }
                    else
                    {
                        UI.ShowSubtitle("Your gang doesn't seem to have a similar member. Make sure you've selected the registration options (color and style don't matter) just like the ones you did when registering!", 8000);
                    }
                }
            };
        }

        void AddRemoveFromAllGangsButton()
        {
            UIMenuItem newButton = new UIMenuItem("Remove ped type from all gangs and pool", "Removes the ped type from all gangs and from the member pool, which means future gangs also won't try to use this type. The selected ped himself will still be a gang member, however.");
            memberMenu.AddItem(newButton);
            memberMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == newButton)
                {

                    memberToSave.modelHash = closestPed.Model.Hash;

                    memberToSave.headDrawableIndex = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, closestPed, 0);
                    memberToSave.headTextureIndex = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, closestPed, 0);

                    memberToSave.hairDrawableIndex = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, closestPed, 2);

                    memberToSave.torsoDrawableIndex = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, closestPed, 3);
                    memberToSave.torsoTextureIndex = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, closestPed, 3);

                    memberToSave.legsDrawableIndex = Function.Call<int>(Hash.GET_PED_DRAWABLE_VARIATION, closestPed, 4);
                    memberToSave.legsTextureIndex = Function.Call<int>(Hash.GET_PED_TEXTURE_VARIATION, closestPed, 4);

                    memberToSave.myStyle = (PotentialGangMember.dressStyle)memberStyle;
                    memberToSave.linkedColor = (PotentialGangMember.memberColor)memberColor;

                    PotentialGangMember memberToRemove = new PotentialGangMember(memberToSave.modelHash,
                        memberToSave.myStyle, memberToSave.linkedColor,
                        memberToSave.headDrawableIndex, memberToSave.headTextureIndex, memberToSave.hairDrawableIndex,
                        memberToSave.torsoDrawableIndex, memberToSave.torsoTextureIndex,
                        memberToSave.legsDrawableIndex, memberToSave.legsTextureIndex);

                    if (PotentialGangMember.RemoveMemberAndSavePool(memberToRemove))
                    {
                        UI.ShowSubtitle("Ped type removed from pool! (It might not be the only similar ped in the pool)");
                    }
                    else
                    {
                        UI.ShowSubtitle("Ped type not found in pool.");
                    }

                    for(int i = 0; i < GangManager.instance.gangData.gangs.Count; i++)
                    {
                        GangManager.instance.gangData.gangs[i].RemoveMemberVariation(memberToRemove);
                    }

                }
            };
        }

        void AddMakeFriendlyToPlayerGangButton()
        {
            UIMenuItem newButton = new UIMenuItem("Make Ped friendly to your gang", "Makes the selected ped (and everyone from his group) and your gang become allies. Can't be used with cops or gangs from this mod! NOTE: this only lasts until scripts are loaded again");
            memberMenu.AddItem(newButton);
            memberMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == newButton)
                {
                    int closestPedRelGroup = closestPed.RelationshipGroup;
                    //check if we can really become allies with this guy
                    if(closestPedRelGroup != Function.Call<int>(Hash.GET_HASH_KEY, "COP"))
                    {
                        //he can still be from one of the gangs! we should check
                     
                        if(GangManager.instance.GetGangByRelGroup(closestPedRelGroup) != null)
                        {
                            UI.ShowSubtitle("That ped is a gang member! Gang members cannot be marked as allies");
                            return;
                        }

                        //ok, we can be allies
                        Gang playerGang = GangManager.instance.GetPlayerGang();
                        World.SetRelationshipBetweenGroups(Relationship.Respect, playerGang.relationGroupIndex, closestPedRelGroup);
                        World.SetRelationshipBetweenGroups(Relationship.Respect, closestPedRelGroup, playerGang.relationGroupIndex);
                        UI.ShowSubtitle("That ped's group is now an allied group!");
                    }
                    else
                    {
                        UI.ShowSubtitle("That ped is a cop! Cops cannot be marked as allies");
                    }
                }
            };
        }


        void AddSaveVehicleButton()
        {
            UIMenuItem newButton = new UIMenuItem("Register Vehicle as usable by AI Gangs", "Makes the vehicle type you are driving become chooseable as one of the types used by AI gangs.");
            carMenu.AddItem(newButton);
            carMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == newButton)
                {
                    Vehicle curVehicle = Game.Player.Character.CurrentVehicle;
                    if (curVehicle != null)
                    {
                        if (PotentialGangVehicle.AddVehicleAndSavePool(new PotentialGangVehicle(curVehicle.Model.Hash)))
                        {
                            UI.ShowSubtitle("Vehicle added to pool!");
                        }
                        else
                        {
                            UI.ShowSubtitle("That vehicle has already been added to the pool.");
                        }
                    }
                    else
                    {
                        UI.ShowSubtitle("You are not inside a vehicle.");
                    }
                }
            };
        }

        void AddRegisterPlayerVehicleButton()
        {
            UIMenuItem newButton = new UIMenuItem("Register Vehicle for your Gang", "Makes the vehicle type you are driving become one of the default types used by your gang.");
            carMenu.AddItem(newButton);
            carMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == newButton)
                {
                    Vehicle curVehicle = Game.Player.Character.CurrentVehicle;
                    if (curVehicle != null)
                    {
                        if (GangManager.instance.GetPlayerGang().AddGangCar(new PotentialGangVehicle(curVehicle.Model.Hash)))
                        {
                            UI.ShowSubtitle("Gang vehicle added!");
                        }
                        else
                        {
                            UI.ShowSubtitle("That vehicle is already registered for your gang.");
                        }
                    }
                    else
                    {
                        UI.ShowSubtitle("You are not inside a vehicle.");
                    }
                }
            };
        }

        void AddRemovePlayerVehicleButton()
        {
            UIMenuItem newButton = new UIMenuItem("Remove Vehicle Type for your Gang", "Removes the vehicle type you are driving from the possible vehicle types for your gang.");
            carMenu.AddItem(newButton);
            carMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == newButton)
                {
                    Vehicle curVehicle = Game.Player.Character.CurrentVehicle;
                    if (curVehicle != null)
                    {
                        if (GangManager.instance.GetPlayerGang().RemoveGangCar(new PotentialGangVehicle(curVehicle.Model.Hash)))
                        {
                            UI.ShowSubtitle("Gang vehicle removed!");
                        }
                        else
                        {
                            UI.ShowSubtitle("That vehicle is not registered for your gang.");
                        }
                    }
                    else
                    {
                        UI.ShowSubtitle("You are not inside a vehicle.");
                    }
                }
            };
        }

        void AddRemoveVehicleEverywhereButton()
        {
            UIMenuItem newButton = new UIMenuItem("Remove Vehicle Type from all gangs and pool", "Removes the vehicle type you are driving from the possible vehicle types for all gangs, including yours. Existing gangs will also stop using that car and get another one if needed.");
            carMenu.AddItem(newButton);
            carMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == newButton)
                {
                    Vehicle curVehicle = Game.Player.Character.CurrentVehicle;
                    if (curVehicle != null)
                    {
                        PotentialGangVehicle removedVehicle = new PotentialGangVehicle(curVehicle.Model.Hash);

                        if (PotentialGangVehicle.RemoveVehicleAndSavePool(removedVehicle))
                        {
                            UI.ShowSubtitle("Vehicle type removed from pool!");
                        }
                        else
                        {
                            UI.ShowSubtitle("Vehicle type not found in pool.");
                        }

                        for (int i = 0; i < GangManager.instance.gangData.gangs.Count; i++)
                        {
                            GangManager.instance.gangData.gangs[i].RemoveGangCar(removedVehicle);
                        }
                    }
                    else
                    {
                        UI.ShowSubtitle("You are not inside a vehicle.");
                    }
                }
            };
        }

        #endregion

        #region Gang Control Stuff

        public void doCallBackup(bool showMenu=true)
        {
            if (ticksSinceLastCarBkp < ModOptions.instance.ticksCooldownBackupCar)
            {
                UI.ShowSubtitle("You must wait before calling for car backup again! (This is configurable)");
                return;
            }
            if (GangManager.instance.AddOrSubtractMoneyToProtagonist(-ModOptions.instance.costToCallBackupCar, true))
            {
                Gang playergang = GangManager.instance.GetPlayerGang();
                if (ZoneManager.instance.GetZonesControlledByGang(playergang.name).Count > 0)
                {
                    Math.Vector3 destPos = Game.Player.Character.Position;

                    Math.Vector3 spawnPos = GangManager.instance.FindGoodSpawnPointForCar();

                    Vehicle spawnedVehicle = GangManager.instance.SpawnGangVehicle(GangManager.instance.GetPlayerGang(),
                            spawnPos, destPos, true, false, true); //zix - im not convinced the set drive task below is used, seems to be handle by this constructor instead
                    if (spawnedVehicle != null)
                    {
                        GangManager.instance.TryPlaceVehicleOnStreet(spawnedVehicle, spawnPos);
                        spawnedVehicle.GetPedOnSeat(VehicleSeat.Driver).Task.DriveTo(spawnedVehicle, destPos, 25, 100);
                        Function.Call(Hash.SET_DRIVE_TASK_DRIVING_STYLE, spawnedVehicle.GetPedOnSeat(VehicleSeat.Driver), 4457020); //ignores roads, avoids obstacles

                        if (showMenu)
                        {
                            gangMenu.Visible = !gangMenu.Visible;
                        }
                        ticksSinceLastCarBkp = 0;
                        GangManager.instance.AddOrSubtractMoneyToProtagonist(-ModOptions.instance.costToCallBackupCar);
                        UI.ShowSubtitle("A vehicle is on its way!", 1000);
                    }
                    else
                    {
                        UI.ShowSubtitle("There are too many gang members around or you haven't registered any member or car.");
                    }
                }
                else
                {
                    UI.ShowSubtitle("You need to have control of at least one territory in order to call for backup.");
                }
            }
            else
            {
                UI.ShowSubtitle("You need $" + ModOptions.instance.costToCallBackupCar.ToString() + " to call a vehicle!");
            }
        }

        void AddCallVehicleButton()
        {
            carBackupBtn = new UIMenuItem("Call Backup Vehicle ($" + ModOptions.instance.costToCallBackupCar.ToString() + ")", "Calls one of your gang's vehicles to your position. All passengers leave the vehicle once it arrives.");
            gangMenu.AddItem(carBackupBtn);
            gangMenu.OnItemSelect += (sender, item, index) =>
            {
                if(item == carBackupBtn)
                {
                    doCallBackup();
                    
                }
               
            };
        }

        void AddCallParatrooperButton()
        {
            paraBackupBtn = new UIMenuItem("Call Parachuting Member ($" + ModOptions.instance.costToCallParachutingMember.ToString() + ")", "Calls a gang member who parachutes to your position (member survival not guaranteed!).");
            gangMenu.AddItem(paraBackupBtn);
            gangMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == paraBackupBtn)
                {
                    if(ticksSinceLastParaBkp < ModOptions.instance.ticksCooldownParachutingMember)
                    {
                        UI.ShowSubtitle("You must wait before calling for parachuting backup again! (This is configurable)");
                        return;
                    }

                    if (GangManager.instance.AddOrSubtractMoneyToProtagonist(-ModOptions.instance.costToCallParachutingMember, true))
                    {
                        Gang playergang = GangManager.instance.GetPlayerGang();
                        //only allow spawning if the player has turf
                        if (ZoneManager.instance.GetZonesControlledByGang(playergang.name).Count > 0)
                        {
                            Math.Vector3 destPos = Game.Player.Character.Position;
                            Ped spawnedPed = GangManager.instance.SpawnGangMember(GangManager.instance.GetPlayerGang(),
                       Game.Player.Character.Position + Math.Vector3.WorldUp * 50);
                            if (spawnedPed != null)
                            {
                                spawnedPed.Task.ParachuteTo(destPos);
                                ticksSinceLastParaBkp = 0;
                                GangManager.instance.AddOrSubtractMoneyToProtagonist(-ModOptions.instance.costToCallParachutingMember);
                                gangMenu.Visible = !gangMenu.Visible;
                            }
                            else
                            {
                                UI.ShowSubtitle("There are too many gang members around or you haven't registered any member.");
                            }
                        }
                        else
                        {
                            UI.ShowSubtitle("You need to have control of at least one territory in order to call for backup.");
                        }
                    }
                    else
                    {
                        UI.ShowSubtitle("You need $" + ModOptions.instance.costToCallParachutingMember.ToString() + " to call a parachuting member!");
                    }
                   
                }

            };
        }

        void AddGangOptionsSubMenu()
        {
            gangOptionsSubMenu = menuPool.AddSubMenu(gangMenu, "Gang Customization/Upgrades Menu");

            AddGangUpgradesMenu();
            AddGangWeaponsMenu();
            AddSetCarColorMenu();
            AddSetBlipColorMenu();
            AddRenameGangButton();

            gangOptionsSubMenu.RefreshIndex();
        }

        void AddGangUpgradesMenu()
        {
            UIMenu upgradesMenu = menuPool.AddSubMenu(gangOptionsSubMenu, "Gang Upgrades...");

            //upgrade buttons
            healthButton = new UIMenuItem("Upgrade Member Health - " + healthUpgradeCost.ToString(), "Increases gang member starting and maximum health. The cost increases with the amount of upgrades made. The limit is configurable via the ModOptions file.");
            armorButton = new UIMenuItem("Upgrade Member Armor - " + armorUpgradeCost.ToString(), "Increases gang member starting body armor. The cost increases with the amount of upgrades made. The limit is configurable via the ModOptions file.");
            accuracyButton = new UIMenuItem("Upgrade Member Accuracy - " + accuracyUpgradeCost.ToString(), "Increases gang member firing accuracy. The cost increases with the amount of upgrades made. The limit is configurable via the ModOptions file.");
            upgradesMenu.AddItem(healthButton);
            upgradesMenu.AddItem(armorButton);
            upgradesMenu.AddItem(accuracyButton);
            upgradesMenu.RefreshIndex();

            upgradesMenu.OnItemSelect += (sender, item, index) =>
            {
                Gang playerGang = GangManager.instance.GetPlayerGang();

                if (item == healthButton)
                {
                    if(GangManager.instance.AddOrSubtractMoneyToProtagonist(-healthUpgradeCost, true))
                    {
                        if(playerGang.memberHealth < ModOptions.instance.maxGangMemberHealth)
                        {
                            playerGang.memberHealth += 20;
                            if(playerGang.memberHealth > ModOptions.instance.maxGangMemberHealth)
                            {
                                playerGang.memberHealth = ModOptions.instance.maxGangMemberHealth;
                            }
                            GangManager.instance.AddOrSubtractMoneyToProtagonist(-healthUpgradeCost);
                            GangManager.instance.SaveGangData();
                            UI.ShowSubtitle("Member health upgraded!");
                        }
                        else
                        {
                            UI.ShowSubtitle("Your members' health is at its maximum limit (it can be configured in the ModOptions file)");
                        }
                    }
                    else
                    {
                        UI.ShowSubtitle("You don't have enough money to buy that upgrade.");
                    }
                }

                if (item == armorButton)
                {
                    if (GangManager.instance.AddOrSubtractMoneyToProtagonist(-armorUpgradeCost, true))
                    {
                        if (playerGang.memberArmor < ModOptions.instance.maxGangMemberArmor)
                        {
                            playerGang.memberArmor += 20;
                            if (playerGang.memberArmor > ModOptions.instance.maxGangMemberArmor)
                            {
                                playerGang.memberArmor = ModOptions.instance.maxGangMemberArmor;
                            }
                            GangManager.instance.AddOrSubtractMoneyToProtagonist(-armorUpgradeCost);
                            GangManager.instance.SaveGangData();
                            UI.ShowSubtitle("Member armor upgraded!");
                        }
                        else
                        {
                            UI.ShowSubtitle("Your members' armor is at its maximum limit (it can be configured in the ModOptions file)");
                        }
                    }
                    else
                    {
                        UI.ShowSubtitle("You don't have enough money to buy that upgrade.");
                    }
                }

                if (item == accuracyButton)
                {
                    if (GangManager.instance.AddOrSubtractMoneyToProtagonist(-accuracyUpgradeCost, true))
                    {
                        if (playerGang.memberAccuracyLevel < ModOptions.instance.maxGangMemberAccuracy)
                        {
                            playerGang.memberAccuracyLevel += 10;
                            if (playerGang.memberAccuracyLevel > ModOptions.instance.maxGangMemberAccuracy)
                            {
                                playerGang.memberAccuracyLevel = ModOptions.instance.maxGangMemberAccuracy;
                            }
                            GangManager.instance.AddOrSubtractMoneyToProtagonist(-accuracyUpgradeCost);
                            GangManager.instance.SaveGangData();
                            UI.ShowSubtitle("Member accuracy upgraded!");
                        }
                        else
                        {
                            UI.ShowSubtitle("Your members' accuracy is at its maximum limit (it can be configured in the ModOptions file)");
                        }
                    }
                    else
                    {
                        UI.ShowSubtitle("You don't have enough money to buy that upgrade.");
                    }
                }

                UpdateUpgradeCosts();

            };

        }

        void AddGangWeaponsMenu()
        {
            UIMenu weaponsMenu = menuPool.AddSubMenu(gangOptionsSubMenu, "Gang Weapons Menu");

            Gang playerGang = GangManager.instance.GetPlayerGang();

            ModOptions.BuyableWeapon[] buyableWeaponsArray = weaponEntries.Keys.ToArray();
            UIMenuCheckboxItem[] weaponCheckBoxArray = weaponEntries.Values.ToArray();

            for (int i = 0; i < weaponCheckBoxArray.Length; i++)
            {
                weaponsMenu.AddItem(weaponCheckBoxArray[i]);
            }

            weaponsMenu.RefreshIndex();

            weaponsMenu.OnCheckboxChange += (sender, item, checked_) =>
            {
                for(int i = 0; i < buyableWeaponsArray.Length; i++)
                {
                    if(item == weaponEntries[buyableWeaponsArray[i]])
                    {
                        if (playerGang.gangWeaponHashes.Contains(buyableWeaponsArray[i].wepHash)){
                            playerGang.gangWeaponHashes.Remove(buyableWeaponsArray[i].wepHash);
                            GangManager.instance.AddOrSubtractMoneyToProtagonist(buyableWeaponsArray[i].price);
                            GangManager.instance.SaveGangData();
                            UI.ShowSubtitle("Weapon Removed!");
                        }
                        else
                        {
                            if(GangManager.instance.AddOrSubtractMoneyToProtagonist(-buyableWeaponsArray[i].price))
                            {
                                playerGang.gangWeaponHashes.Add(buyableWeaponsArray[i].wepHash);
                                GangManager.instance.SaveGangData();
                                UI.ShowSubtitle("Weapon Bought!");
                            }
                            else
                            {
                                UI.ShowSubtitle("You don't have enough money to buy that weapon for your gang.");
                            }
                        }

                        break;
                    }
                }

                UpdateBuyableWeapons();
            };
        }

        void AddSetCarColorMenu()
        {
            FillCarColorEntries();

            UIMenu carColorsMenu = menuPool.AddSubMenu(gangOptionsSubMenu, "Gang Vehicle Colors Menu");

            Gang playerGang = GangManager.instance.GetPlayerGang();

            VehicleColor[] carColorsArray = carColorEntries.Keys.ToArray();
            UIMenuItem[] colorButtonsArray = carColorEntries.Values.ToArray();

            for (int i = 0; i < colorButtonsArray.Length; i++)
            {
                carColorsMenu.AddItem(colorButtonsArray[i]);
            }

            carColorsMenu.RefreshIndex();

            carColorsMenu.OnIndexChange += (sender, index) =>
            {
                Vehicle playerVehicle = Game.Player.Character.CurrentVehicle;
                if (playerVehicle != null)
                {
                    playerVehicle.PrimaryColor = carColorsArray[index];
                }
            };
          
            carColorsMenu.OnItemSelect += (sender, item, checked_) =>
            {
                for (int i = 0; i < carColorsArray.Length; i++)
                {
                    if (item == carColorEntries[carColorsArray[i]])
                    {
                        playerGang.vehicleColor = carColorsArray[i];
                        GangManager.instance.SaveGangData(false);
                        UI.ShowSubtitle("Gang vehicle color changed!");
                        break;
                    }
                }

            };
        }

        void AddSetBlipColorMenu()
        {

            UIMenu blipColorsMenu = menuPool.AddSubMenu(gangOptionsSubMenu, "Gang Blip Colors Menu");

            Gang playerGang = GangManager.instance.GetPlayerGang();

            string[] blipColorNamesArray = blipColorEntries.Keys.ToArray();
            int[] colorCodesArray = blipColorEntries.Values.ToArray();

            for (int i = 0; i < colorCodesArray.Length; i++)
            {
                blipColorsMenu.AddItem(new UIMenuItem(blipColorNamesArray[i], "The color change can be seen immediately on turf blips. Click or press enter after selecting a color to save the color change."));
            }

            blipColorsMenu.RefreshIndex();

            blipColorsMenu.OnIndexChange += (sender, index) =>
            {
                GangManager.instance.GetPlayerGang().blipColor = colorCodesArray[index];
                ZoneManager.instance.RefreshZoneBlips();
            };

            gangOptionsSubMenu.OnMenuChange += (oldMenu, newMenu, forward) =>{
                if(newMenu == blipColorsMenu)
                {
                    playerGangOriginalBlipColor = GangManager.instance.GetPlayerGang().blipColor;
                }
            };

            blipColorsMenu.OnMenuClose += (sender) =>
            {
                GangManager.instance.GetPlayerGang().blipColor = playerGangOriginalBlipColor;
                ZoneManager.instance.RefreshZoneBlips();
            };

            blipColorsMenu.OnItemSelect += (sender, item, checked_) =>
            {
                for (int i = 0; i < blipColorNamesArray.Length; i++)
                {
                    if (item.Text == blipColorNamesArray[i])
                    {
                        GangManager.instance.GetPlayerGang().blipColor = colorCodesArray[i];
                        playerGangOriginalBlipColor = colorCodesArray[i];
                        GangManager.instance.SaveGangData(false);
                        UI.ShowSubtitle("Gang blip color changed!");
                        break;
                    }
                }

            };
        }

        void AddRenameGangButton()
        {
            UIMenuItem newButton = new UIMenuItem("Rename Gang", "Resets your gang's name.");
            gangOptionsSubMenu.AddItem(newButton);
            gangOptionsSubMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == newButton)
                {
                    gangOptionsSubMenu.Visible = !gangOptionsSubMenu.Visible;
                    Function.Call(Hash.DISPLAY_ONSCREEN_KEYBOARD, false, "FMMC_KEY_TIP12N", "", "Gang Name", "", "", "", 30);
                    curInputType = desiredInputType.enterGangName;
                }
            };
        }

        #endregion

        #region Mod Settings

        void AddModSettingsSubMenu()
        {
            modSettingsSubMenu = menuPool.AddSubMenu(gangMenu, "Mod Settings Menu");

            AddMemberAggressivenessControl();
            AddEnableAmbientSpawnToggle();
            AddEnableFightingToggle();
            AddEnableWarVersusPlayerToggle();
            AddEnableCarTeleportToggle();
            AddGangsStartWithPistolToggle();
            AddKeyBindingMenu();
            AddGamepadControlsToggle();
            AddReloadOptionsButton();
            AddResetWeaponOptionsButton();
            AddResetOptionsButton();
           
            modSettingsSubMenu.RefreshIndex();
        }

        void AddMemberAggressivenessControl()
        { 
            List<dynamic> aggModes = new List<dynamic>
            { 
                "V. Aggressive",
                "Aggressive",
                "Defensive"
            };

            aggOption = new UIMenuListItem("Member Aggressiveness", aggModes,(int) ModOptions.instance.gangMemberAggressiveness, "This controls how aggressive members from all gangs will be. Very aggressive members will shoot at cops and other gangs on sight, aggressive members will shoot only at other gangs on sight and defensive members will only shoot when one of them is attacked or aimed at."); 
            modSettingsSubMenu.AddItem(aggOption);
            modSettingsSubMenu.OnListChange += (sender, item, index) => 
            { 
                if (item == aggOption) 
                {
                    ModOptions.instance.SetMemberAggressiveness((ModOptions.gangMemberAggressivenessMode)index);
                } 
            }; 
     } 

        void AddEnableFightingToggle()
        {
            UIMenuCheckboxItem fightingToggle = new UIMenuCheckboxItem("Gang Fights Enabled?", ModOptions.instance.fightingEnabled, "If unchecked, members from different gangs won't attack each other (including the player). Gang wars also won't happen.");

            modSettingsSubMenu.AddItem(fightingToggle);
            modSettingsSubMenu.OnCheckboxChange += (sender, item, checked_) =>
            {
                if (item == fightingToggle)
                {
                    if (!GangWarManager.instance.isOccurring)
                    {
                        ModOptions.instance.fightingEnabled = checked_;
                        ModOptions.instance.SaveOptions(false);
                    }
                    else
                    {
                        UI.ShowSubtitle("There's a war going on, this option can't be changed now.");
                        fightingToggle.Checked = !checked_;
                    }
                }

            };
        }

        void AddEnableWarVersusPlayerToggle()
        {
            UIMenuCheckboxItem warToggle = new UIMenuCheckboxItem("Enemy gangs can attack your turf?", ModOptions.instance.warAgainstPlayerEnabled, "If unchecked, enemy gangs won't start a war against you, but you will still be able to start a war against them.");

            modSettingsSubMenu.AddItem(warToggle);
            modSettingsSubMenu.OnCheckboxChange += (sender, item, checked_) =>
            {
                if (item == warToggle)
                {
                    ModOptions.instance.warAgainstPlayerEnabled = checked_;
                    ModOptions.instance.SaveOptions(false);
                }

            };
        }

        void AddEnableAmbientSpawnToggle()
        {
            UIMenuCheckboxItem spawnToggle = new UIMenuCheckboxItem("Ambient member spawning?", ModOptions.instance.ambientSpawningEnabled, "If enabled, members from the gang which owns the zone you are in will spawn once in a while. This option does not affect member spawning via backup calls or gang wars.");

            modSettingsSubMenu.AddItem(spawnToggle);
            modSettingsSubMenu.OnCheckboxChange += (sender, item, checked_) =>
            {
                if (item == spawnToggle)
                {
                    ModOptions.instance.ambientSpawningEnabled = checked_;
                    ModOptions.instance.SaveOptions(false);
                }

            };
        }

        void AddEnableCarTeleportToggle()
        {
            UIMenuCheckboxItem spawnToggle = new UIMenuCheckboxItem("Backup cars can teleport to always arrive?", ModOptions.instance.forceSpawnCars, "If enabled, backup cars, after taking too long to get to the player, will teleport close by. This will only affect friendly vehicles.");

            modSettingsSubMenu.AddItem(spawnToggle);
            modSettingsSubMenu.OnCheckboxChange += (sender, item, checked_) =>
            {
                if (item == spawnToggle)
                {
                    ModOptions.instance.forceSpawnCars = checked_;
                    ModOptions.instance.SaveOptions(false);
                }

            };
        }

        void AddGangsStartWithPistolToggle()
        {
            UIMenuCheckboxItem pistolToggle = new UIMenuCheckboxItem("Gangs start with Pistols?", ModOptions.instance.gangsStartWithPistols, "If checked, all gangs, except the player's, will start with pistols. Pistols will not be given to gangs already in town.");

            modSettingsSubMenu.AddItem(pistolToggle);
            modSettingsSubMenu.OnCheckboxChange += (sender, item, checked_) =>
            {
                if (item == pistolToggle)
                {
                    ModOptions.instance.gangsStartWithPistols = checked_;
                    ModOptions.instance.SaveOptions(false);
                }

            };
        }

        void AddGamepadControlsToggle()
        {
            UIMenuCheckboxItem padToggle = new UIMenuCheckboxItem("Use joypad controls?", ModOptions.instance.joypadControls, "Enables/disables the use of joypad commands to recruit members (pad right), call backup (pad left) and output zone info (pad up). Commands are used while aiming. All credit goes to zixum.");

            modSettingsSubMenu.AddItem(padToggle);
            modSettingsSubMenu.OnCheckboxChange += (sender, item, checked_) =>
            {
                if (item == padToggle)
                {
                    ModOptions.instance.joypadControls = checked_;
                    if (checked_)
                    {
                        UI.ShowSubtitle("Joypad controls activated. Remember to disable them when not using a joypad, as it is possible to use the commands with mouse/keyboard as well");
                    }
                    ModOptions.instance.SaveOptions(false);
                }

            };
        }

        void AddKeyBindingMenu()
        {
            UIMenu bindingsMenu = menuPool.AddSubMenu(modSettingsSubMenu, "Key Bindings...");

            //the buttons
            openGangMenuBtn = new UIMenuItem("Gang Control Key - " + ModOptions.instance.openGangMenuKey.ToString(), "The key used to open the Gang/Mod Menu. Used with shift to open the Member Registration Menu. Default is B.");
            openZoneMenuBtn = new UIMenuItem("Zone Control Key - " + ModOptions.instance.openZoneMenuKey.ToString(), "The key used to check the current zone's name and ownership. Used with shift to open the Zone Menu and with control to toggle zone blip display modes. Default is N.");
            addToGroupBtn = new UIMenuItem("Add or Remove Member from Group - " + ModOptions.instance.addToGroupKey.ToString(), "The key used to add/remove the targeted friendly gang member to/from your group. Members of your group will follow you. Default is H.");
            mindControlBtn = new UIMenuItem("Take Control of Member - " + ModOptions.instance.mindControlKey.ToString(), "The key used to take control of the targeted friendly gang member. Pressing this key while already in control of a member will restore protagonist control. Default is J.");
            bindingsMenu.AddItem(openGangMenuBtn);
            bindingsMenu.AddItem(openZoneMenuBtn);
            bindingsMenu.AddItem(addToGroupBtn);
            bindingsMenu.AddItem(mindControlBtn);
            bindingsMenu.RefreshIndex();

            bindingsMenu.OnItemSelect += (sender, item, index) =>
            {
                UI.ShowSubtitle("Press the new key for this command.");
                curInputType = desiredInputType.changeKeyBinding;

                if (item == openGangMenuBtn)
                {
                    targetKeyBindToChange = changeableKeyBinding.GangMenuBtn;
                }
                if (item == openZoneMenuBtn)
                {
                    targetKeyBindToChange = changeableKeyBinding.ZoneMenuBtn;
                }
                if (item == addToGroupBtn)
                {
                    targetKeyBindToChange = changeableKeyBinding.AddGroupBtn;
                }
                if (item == mindControlBtn)
                {
                    targetKeyBindToChange = changeableKeyBinding.MindControlBtn;
                }
            };
        }

        void AddReloadOptionsButton()
        {
            UIMenuItem newButton = new UIMenuItem("Reload Mod Options", "Reload the settings defined by the ModOptions file. Use this if you tweaked the ModOptions file while playing for its new settings to take effect.");
            modSettingsSubMenu.AddItem(newButton);
            modSettingsSubMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == newButton)
                {
                    ModOptions.instance.LoadOptions();
                    GangManager.instance.ResetGangUpdateIntervals();
                    UpdateBuyableWeapons();
                    UpdateUpgradeCosts();
                    carBackupBtn.Text = "Call Backup Vehicle ($" + ModOptions.instance.costToCallBackupCar.ToString() + ")";
                    paraBackupBtn.Text = "Call Parachuting Member ($" + ModOptions.instance.costToCallParachutingMember.ToString() + ")";
                    UpdateTakeOverBtnText();
                }
            };
        }

        void AddResetWeaponOptionsButton()
        {
            UIMenuItem newButton = new UIMenuItem("Reset Weapon List and Prices to Defaults", "Resets the weapon list in the ModOptions file back to the default values. The new options take effect immediately.");
            modSettingsSubMenu.AddItem(newButton);
            modSettingsSubMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == newButton)
                {
                    ModOptions.instance.buyableWeapons.Clear();
                    ModOptions.instance.SetWeaponListDefaultValues();
                    ModOptions.instance.SaveOptions(false);
                    UpdateBuyableWeapons();
                    UpdateUpgradeCosts();
                }
            };
        }

        void AddResetOptionsButton()
        {
            UIMenuItem newButton = new UIMenuItem("Reset Mod Options to Defaults", "Resets all the options in the ModOptions file back to the default values (except the possible gang first and last names). The new options take effect immediately.");
            modSettingsSubMenu.AddItem(newButton);
            modSettingsSubMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == newButton)
                {
                    ModOptions.instance.SetAllValuesToDefault();
                    UpdateBuyableWeapons();
                    UpdateUpgradeCosts();
                    UpdateTakeOverBtnText();
                }
            };
        }

        #endregion
    }
}