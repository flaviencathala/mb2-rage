using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Library;
using static TaleWorlds.MountAndBlade.SkinVoiceManager;
using TaleWorlds.CampaignSystem;
using System.Linq;
using System.Reflection;

namespace Rage
{
	public class Main : MBSubModuleBase
	{
		public override void OnMissionBehaviourInitialize(Mission mission)
		{
			base.OnMissionBehaviourInitialize(mission);
			XmlSerializer xmlSerializer = new XmlSerializer(typeof(RageSettings));
			StreamReader streamReader = new StreamReader(Path.Combine(new string[] { BasePath.Name, "Modules", "Rage", "RageSettings.xml" }));
			RageSettings rageSettings = (RageSettings)xmlSerializer.Deserialize(streamReader);
			mission.AddMissionBehaviour(new RageBehaviour(rageSettings));
		}
	}

	[XmlRoot(ElementName = "RageSettings")]
	public class RageSettings
	{
		[XmlElement(ElementName = "DefaultRageDuration")]
		public float defaultRageDuration { get; set; }
		[XmlElement(ElementName = "DefaultRageCooldown")]
		public float defaultRageCooldown { get; set; }
		[XmlElement(ElementName = "RageInfluenceCost")]
		public int rageInfluenceCost { get; set; }
		[XmlElement(ElementName = "SuperRageMultiplier")]
		public int superRageMultiplier { get; set; }
		[XmlElement(ElementName = "DefaultRageRadius")]
		public int defaultRageRadius { get; set; }
		[XmlElement(ElementName = "RageRadiusMultiplier")]
		public float rageRadiusMultiplier { get; set; }
	}

	class AgentPropertiesSave
	{
		public Agent agent { get; private set; }
		public bool isNewSaveBroken { get; private set; }
		private AgentDrivenProperties save = new AgentDrivenProperties();
		private AgentDrivenProperties newSave = new AgentDrivenProperties();

		public AgentPropertiesSave(Agent agent)
		{
			this.agent = agent;
			this.saveProperties();
		}

		/* agent driven properties are reset on some events, this is meant to counter those resets */
		public void OnSaveBroken()
        {
			this.isNewSaveBroken = true;
		}

		private void saveProperties()
		{
			foreach (PropertyInfo property in this.agent.AgentDrivenProperties.GetType().GetProperties().Where(p => p.CanRead))
			{
				property.SetValue(this.save, property.GetValue(this.agent.AgentDrivenProperties));
			}
		}

		public void saveNewProperties()
		{
			foreach (PropertyInfo property in this.agent.AgentDrivenProperties.GetType().GetProperties().Where(p => p.CanRead))
			{
				property.SetValue(this.newSave, property.GetValue(this.agent.AgentDrivenProperties));
			}

			agent.OnAgentWieldedItemChange += this.OnSaveBroken;
			agent.OnAgentMountedStateChanged += this.OnSaveBroken;
			this.isNewSaveBroken = false;
		}

		private void restoreNewProperties()
		{
			foreach (PropertyInfo property in this.save.GetType().GetProperties().Where(p => p.CanRead))
			{
				property.SetValue(this.agent.AgentDrivenProperties, property.GetValue(this.newSave));
			}
			this.agent.UpdateCustomDrivenProperties();
			this.isNewSaveBroken = false;
		}

		public void tick()
        {
			if (this.isNewSaveBroken)
            {
				this.restoreNewProperties();
            }
        }

		public void restoreProperties()
		{
			foreach (PropertyInfo property in this.save.GetType().GetProperties().Where(p => p.CanRead))
			{
				property.SetValue(this.agent.AgentDrivenProperties, property.GetValue(this.save));
			}
			this.agent.UpdateCustomDrivenProperties();

			agent.OnAgentWieldedItemChange -= this.OnSaveBroken;
			agent.OnAgentMountedStateChanged -= this.OnSaveBroken;
		}
	}

	public class RageBehaviour : MissionLogic {

		private RageSettings settings;

		public new MissionBehaviourType BehaviourType = MissionBehaviourType.Logic;
		private float rageActivated = float.MinValue;
		private float rageDuration;
		private float rageCooldown;
		private List<AgentPropertiesSave> rageAgentsPropertiesSave = new List<AgentPropertiesSave>();

		public RageBehaviour(RageSettings settings)
        {
			if (settings == null)
			{
				InformationManager.DisplayMessage(new InformationMessage("Settings are null", Color.FromUint(0xffFF3030)));
			}
			this.settings = settings == default(RageSettings) ? new RageSettings() : settings;
		}

		public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent, int damage, in MissionWeapon affectorWeapon)
        {
			/* divides damage by 2 to rage agent */
			if (!this.rageAgentsPropertiesSave.Where(s => s.agent == affectedAgent).IsEmpty())
            {
				affectedAgent.Health += (damage / 2);
				affectedAgent.SetMorale(affectedAgent.GetMorale() + 5);
			}
			/* multiply damage by 2 from rage agent */
			if (!this.rageAgentsPropertiesSave.Where(s => s.agent == affectorAgent).IsEmpty())
			{
				affectedAgent.Health -= damage;
				affectorAgent.SetMorale(affectorAgent.GetMorale() + 2);
			}
		}

		public override void OnScoreHit(Agent affectedAgent, Agent affectorAgent, WeaponComponentData attackerWeapon, bool isBlocked, float damage, float movementSpeedDamageModifier, float hitDistance, AgentAttackType attackType, float shotDifficulty, BoneBodyPartType victimHitBodyPart)
        {
			/* multiply damage by 5 from rage agent to shield */
			if (victimHitBodyPart == BoneBodyPartType.None && !this.rageAgentsPropertiesSave.Where(s => s.agent == affectorAgent).IsEmpty())
			{
				EquipmentIndex shieldIndex = affectedAgent.GetWieldedItemIndex(Agent.HandIndex.OffHand);
				if (shieldIndex != EquipmentIndex.None)
					affectedAgent.Equipment.SetHitPointsOfSlot(shieldIndex, Convert.ToInt16(affectedAgent.Equipment[shieldIndex].HitPoints - (damage * 4)));
			}
		}

		private bool isRageActivated()
		{
			return (Mission.Time - this.rageActivated) < this.rageDuration;
		}

		private int lastRageNumberDisplayed;
		private void rageTick()
		{
			if (this.isRageActivated())
			{
				int remaining = (int)((this.rageDuration - (Mission.Time - this.rageActivated)));

				Random random = new Random();
				foreach (var save in this.rageAgentsPropertiesSave)
				{
					save.tick();
					if (random.Next(0, 600) == 0)
					{
						save.agent.MakeVoice(SkinVoiceManager.VoiceType.Yell, CombatVoiceNetworkPredictionType.NoPrediction);
					}
				}
				if (remaining != this.lastRageNumberDisplayed)
				{
					if (remaining > 0)
                    {
						if (remaining != this.rageDuration)
						{
							if (remaining % 5 == 0)
                            {
								InformationManager.DisplayMessage(new InformationMessage(remaining + " seconds of rage remaining"));
							}
						}
					}
					else
					{
						foreach (var agentPropertiesSave in this.rageAgentsPropertiesSave)
                        {
							agentPropertiesSave.restoreProperties();
						}
						this.rageAgentsPropertiesSave.Clear();
						InformationManager.DisplayMessage(new InformationMessage("Rage finished"));
					}
					this.lastRageNumberDisplayed = remaining;
				}
			}
		}

		private void StartRage(bool superRage = false)
		{
			if ((Mission.Time - this.rageActivated) > this.rageCooldown && Agent.Main.IsActive())
			{
				bool isCustomBattle = Mission.Current.GetMissionBehaviour<CustomBattleAgentLogic>() != default(CustomBattleAgentLogic);

				if (!isCustomBattle)
				{
					int cost = superRage ? this.settings.rageInfluenceCost * this.settings.superRageMultiplier : this.settings.rageInfluenceCost;
					if (PartyBase.MainParty.LeaderHero.Clan.Influence < cost)
					{
						InformationManager.DisplayMessage(new InformationMessage("Insufficient influence to activate rage (" + cost + " required, actual " + PartyBase.MainParty.LeaderHero.Clan.Influence + ")", Color.FromUint(0xffAA3030)));
						return;
					}
					PartyBase.MainParty.LeaderHero.Clan.Influence -= cost;
				}

				if (superRage)
				{
					this.rageDuration = this.settings.defaultRageDuration * this.settings.superRageMultiplier;
					this.rageCooldown = this.settings.defaultRageCooldown * this.settings.superRageMultiplier;
				}
				else
				{
					this.rageDuration = this.settings.defaultRageDuration;
					this.rageCooldown = this.settings.defaultRageCooldown + this.rageDuration;
				}

				Agent.Main.MakeVoice(SkinVoiceManager.VoiceType.Yell, CombatVoiceNetworkPredictionType.NoPrediction);

				this.rageActivated = Mission.Time;
				int radius = isCustomBattle ? this.settings.defaultRageRadius : (int)(PartyBase.MainParty.LeaderHero.GetSkillValue(DefaultSkills.Leadership) * this.settings.rageRadiusMultiplier);
				IEnumerable<Agent> affectedAgents = Mission.GetNearbyAllyAgents(Agent.Main.Position.AsVec2, radius, Agent.Main.Team);
				foreach (var agent in affectedAgents)
                {
					AgentPropertiesSave save = new AgentPropertiesSave(agent);
					this.rageAgentsPropertiesSave.Add(save);

					agent.AgentDrivenProperties.MaxSpeedMultiplier *= 1.2f;

					agent.AgentDrivenProperties.MountSpeed *= 1.2f;
					agent.AgentDrivenProperties.MountChargeDamage *= 2f;
					agent.AgentDrivenProperties.MountManeuver *= 2f;

					agent.AgentDrivenProperties.CombatMaxSpeedMultiplier *= 2f;
					agent.AgentDrivenProperties.SwingSpeedMultiplier *= 2f;
					agent.AgentDrivenProperties.HandlingMultiplier *= 2f;
					agent.AgentDrivenProperties.ThrustOrRangedReadySpeedMultiplier *= 2f;
					agent.AgentDrivenProperties.ShieldBashStunDurationMultiplier *= 2f;
					agent.AgentDrivenProperties.KickStunDurationMultiplier *= 2f;
					agent.AgentDrivenProperties.AttributeShieldMissileCollisionBodySizeAdder *= 2f;

					agent.AgentDrivenProperties.BipedalRangedReadySpeedMultiplier *= 3f;
					agent.AgentDrivenProperties.BipedalRangedReloadSpeedMultiplier *= 3f;
					agent.AgentDrivenProperties.ReloadSpeed *= 3f;
					agent.AgentDrivenProperties.WeaponBestAccuracyWaitTime = 0f;
					agent.AgentDrivenProperties.WeaponMaxMovementAccuracyPenalty = 0f;
					agent.AgentDrivenProperties.WeaponMaxUnsteadyAccuracyPenalty = 0f;
					agent.AgentDrivenProperties.WeaponInaccuracy = 0f;
					agent.AgentDrivenProperties.LongestRangedWeaponInaccuracy = 0f;
					agent.AgentDrivenProperties.ReloadMovementPenaltyFactor = 0f;
					agent.AgentDrivenProperties.WeaponRotationalAccuracyPenaltyInRadians = 0f;

					agent.AgentDrivenProperties.AttributeCourage *= 2f;
					agent.AgentDrivenProperties.AttributeRiding *= 2f;
					agent.AgentDrivenProperties.AttributeHorseArchery *= 2f;
					agent.AgentDrivenProperties.AttributeShield *= 2f;


					agent.AgentDrivenProperties.AiShootFreq *= 2f;
					agent.AgentDrivenProperties.AiWaitBeforeShootFactor = 0f;

					agent.SetMorale(agent.GetMorale() + 50);

					agent.UpdateCustomDrivenProperties();
					save.saveNewProperties();

					agent.MakeVoice(SkinVoiceManager.VoiceType.Yell, CombatVoiceNetworkPredictionType.NoPrediction);
				}
				InformationManager.DisplayMessage(new InformationMessage("Rage activated for " + this.rageAgentsPropertiesSave.Count() + " agents (radius " + radius + ") for " + this.rageDuration + " seconds!"));
			}
			else
			{
				InformationManager.DisplayMessage(new InformationMessage((int)(this.rageCooldown - (Mission.Time - this.rageActivated)) + " seconds to wait to activate rage"));
			}
		}

		public override void OnMissionTick(float dt)
		{
			if (Mission == null || this == null)
            {
				return;
            }
			if (Input.IsKeyReleased(InputKey.B))
			{
				this.StartRage(Input.IsKeyDown(InputKey.LeftAlt));
			}
			this.rageTick();
		}
	}
}
