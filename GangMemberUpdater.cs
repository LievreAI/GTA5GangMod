﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GTA.Math;
using GTA.Native;

namespace GTA.GangAndTurfMod {
	class GangMemberUpdater : Script {

		public static GangMemberUpdater instance;

		/// <summary>
		/// the updater must wait for the gangManager before it starts looking for members to update
		/// </summary>
		public bool enabled = false;

		public List<SpawnedGangMember> memberList;

		private bool updateRanThisFrame = false;


		void OnTick(object sender, EventArgs e) {
			updateRanThisFrame = false;
			for (int i = 0; i < memberList.Count; i++) {
				if (memberList[i].watchedPed != null) {
					memberList[i].ticksSinceLastUpdate++;
					if (!updateRanThisFrame && memberList[i].ticksSinceLastUpdate >= memberList[i].ticksBetweenUpdates) {
						memberList[i].Update();
						updateRanThisFrame = true;
						memberList[i].ticksSinceLastUpdate = 0 - RandoMath.CachedRandom.Next(memberList[i].ticksBetweenUpdates / 3);
					}
				}
			}
		}

		public static void Initialize() {
			instance.memberList = GangManager.instance.livingMembers;
			instance.enabled = true;
		}

		
		public GangMemberUpdater() {
			this.Tick += OnTick;
			instance = this;
		}

	}
}