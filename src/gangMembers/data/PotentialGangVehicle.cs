﻿using System.Collections.Generic;
using System.Xml.Serialization;

namespace GTA.GangAndTurfMod
{
    public class VehicleModData
    {
        public VehicleMod ModType { get; set; } // Use the VehicleMod enum directly
        public int ModValue { get; set; }
    }

    public class PotentialGangVehicle
    {
        public int modelHash;

        public List<VehicleModData> VehicleMods { get; set; }

        [XmlIgnore]
        public static PotentialCarPool CarPool
        {
            get
            {
                if (carPool == null)
                {
                    carPool = PersistenceHandler.LoadFromFile<PotentialCarPool>("VehiclePool");

                    //if we still don't have a pool, create one!
                    if (carPool == null)
                    {
                        carPool = new PotentialCarPool();
                    }
                }

                return carPool;
            }

        }

        private static PotentialCarPool carPool;



        public PotentialGangVehicle(int modelHash)
        {
            this.modelHash = modelHash;
        }

        public PotentialGangVehicle()
        {
            this.modelHash = -1;
        }

        public static bool AddVehicleAndSavePool(PotentialGangVehicle newCar)
        {
            //check if there isn't an identical entry in the pool
            if (!CarPool.HasIdenticalEntry(newCar))
            {
                CarPool.carList.Add(newCar);
                PersistenceHandler.SaveToFile(CarPool, "VehiclePool");
                return true;
            }

            return false;
        }

        public static bool RemoveVehicleAndSavePool(PotentialGangVehicle newCar)
        {
            int identicalEntryIndex = 0;
            //check if there is an identical entry in the pool
            if (CarPool.HasIdenticalEntry(newCar, ref identicalEntryIndex))
            {
                CarPool.carList.RemoveAt(identicalEntryIndex);
                PersistenceHandler.SaveToFile(CarPool, "VehiclePool");
                return true;
            }

            return false;
        }

        public static PotentialGangVehicle GetCarFromPool()
        {
            PotentialGangVehicle returnedVehicle;

            if (CarPool.carList.Count <= 0)
            {
                
                UI.Notify(Localization.GetTextByKey("notify_warn_bad_carpool_file", "GTA5GangNTurfMod Warning: empty/bad carpool file! Enemy gangs won't have cars"));
                return null;
            }

            returnedVehicle = CarPool.carList[RandoMath.CachedRandom.Next(CarPool.carList.Count)];

            return returnedVehicle;
        }

        /// <summary>
        /// true if both are the same model and have the same mods
        /// </summary>
        /// <param name="otherVehicle"></param>
        /// <returns></returns>
        public bool Equals(PotentialGangVehicle otherVehicle)
        {
            if(otherVehicle.modelHash == modelHash)
            {
                if(otherVehicle.VehicleMods == null && VehicleMods == null)
                {
                    return true;
                }

                if(otherVehicle.VehicleMods != null && VehicleMods != null &&
                    otherVehicle.VehicleMods.Count == VehicleMods.Count)
                {
                    foreach (var vehMod in VehicleMods)
                    {
                        if (otherVehicle.VehicleMods.Find(vm => vm.ModValue == vehMod.ModValue && vm.ModType == vehMod.ModType) == null)
                        {
                            return false;
                        }
                    }

                    return true;
                }

            }

            return false;
        }
    }

    public class PotentialCarPool
    {
        public List<PotentialGangVehicle> carList;

        public PotentialCarPool()
        {
            carList = new List<PotentialGangVehicle>();
        }

        public bool HasIdenticalEntry(PotentialGangVehicle potentialEntry)
        {

            for (int i = 0; i < carList.Count; i++)
            {
                if (carList[i].modelHash == potentialEntry.modelHash)
                {
                    return true;
                }
            }
            return false;
        }

        public bool HasIdenticalEntry(PotentialGangVehicle potentialEntry, ref int identicalEntryIndex)
        {

            for (int i = 0; i < carList.Count; i++)
            {
                if (carList[i].modelHash == potentialEntry.modelHash)
                {
                    identicalEntryIndex = i;
                    return true;
                }
            }
            identicalEntryIndex = -1;
            return false;
        }
    }
}
