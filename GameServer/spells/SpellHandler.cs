/*
 * DAWN OF LIGHT - The first free open source DAoC server emulator
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 *
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

using System.Text;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS.Effects;
using DOL.GS.PacketHandler;
using DOL.GS.PlayerClass;
using DOL.GS.ServerProperties;
using DOL.GS.RealmAbilities;
using DOL.GS.SkillHandler;
using DOL.GS.SpellEffects;
using DOL.Language;


using log4net;
using System.Collections.Concurrent;

namespace DOL.GS.Spells
{
	/// <summary>
	/// Default class for spell handler
	/// should be used as a base class for spell handler
	/// </summary>
	public class SpellHandler : ISpellHandler
	{
		private static readonly ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
		public object _stateLock = new object();

		//GameLoop Methods
		public eCastState castState { get; set; }
		private double _castFinishedTickTime;
		//todo create this list when loading spell
		private List<IEffectComponent> _spellEffectComponents = new List<IEffectComponent>();
		public GameLiving GetTarget()
        {
			return m_spellTarget;
        }

		// Max number of Concentration spells that a single caster is allowed to cast.
		public const int MAX_CONC_SPELLS = 20;

		/// <summary>
		/// Maximum number of sub-spells to get delve info for.
		/// </summary>
		protected static readonly byte MAX_DELVE_RECURSION = 5;

		protected DelayedCastTimer m_castTimer;
		/// <summary>
		/// The spell that we want to handle
		/// </summary>
		protected Spell m_spell;
		/// <summary>
		/// The spell line the spell belongs to
		/// </summary>
		protected SpellLine m_spellLine;
		/// <summary>
		/// The caster of the spell
		/// </summary>
		protected GameLiving m_caster;
		/// <summary>
		/// The target for this spell
		/// </summary>
		protected GameLiving m_spellTarget = null;
		/// <summary>
		/// Has the spell been interrupted
		/// </summary>
		protected bool m_interrupted = false;
		/// <summary>
		/// Delayedcast Stage
		/// </summary>
		public int Stage
		{
			get { return m_stage; }
			set { m_stage = value; }
		}
		protected int m_stage = 0;
		/// <summary>
		/// Use to store Time when the delayedcast started
		/// </summary>
		protected long m_started = 0;
		/// <summary>
		/// Shall we start the reuse timer
		/// </summary>
		protected bool m_startReuseTimer = true;

		private long _castStartTick;
		public long CastStartTick { get { return _castStartTick; } }
		public bool StartReuseTimer
		{
			get { return m_startReuseTimer; }
		}

		/// <summary>
		/// Can this spell be queued with other spells?
		/// </summary>
		public virtual bool CanQueue
		{
			get { return true; }
		}

		/// <summary>
		/// Does this spell break stealth on start of cast?
		/// </summary>
		public virtual bool UnstealthCasterOnStart
		{
			get { return true; }
		}
		
		/// <summary>
		/// Does this spell break stealth on Finish of cast?
		/// </summary>
		public virtual bool UnstealthCasterOnFinish
		{
			get { return true; }
		}
		
		protected InventoryItem m_spellItem = null;

		/// <summary>
		/// Ability that casts a spell
		/// </summary>
		protected ISpellCastingAbilityHandler m_ability = null;

		/// <summary>
		/// Stores the current delve info depth
		/// </summary>
		private byte m_delveInfoDepth;

		/// <summary>
		/// AttackData result for this spell, if any
		/// </summary>
		protected AttackData m_lastAttackData = null;
		/// <summary>
		/// AttackData result for this spell, if any
		/// </summary>
		public AttackData LastAttackData
		{
			get { return m_lastAttackData; }
		}

		/// <summary>
		/// The property key for the interrupt timeout
		/// </summary>
		public const string INTERRUPT_TIMEOUT_PROPERTY = "CAST_INTERRUPT_TIMEOUT";
		/// <summary>
		/// The property key for focus spells
		/// </summary>
		protected const string FOCUS_SPELL = "FOCUSING_A_SPELL";

		protected bool m_ignoreDamageCap = false;


		private long _calculatedCastTime = 0;

		/// <summary>
		/// Does this spell ignore any damage cap?
		/// </summary>
		public bool IgnoreDamageCap
		{
			get { return m_ignoreDamageCap; }
			set { m_ignoreDamageCap = value; }
		}

		protected bool m_useMinVariance = false;

		/// <summary>
		/// Should this spell use the minimum variance for the type?
		/// Followup style effects, for example, always use the minimum variance
		/// </summary>
		public bool UseMinVariance
		{
			get { return m_useMinVariance; }
			set { m_useMinVariance = value; }
		}
		
		/// <summary>
		/// Can this SpellHandler Coexist with other Overwritable Spell Effect
		/// </summary>
		public virtual bool AllowCoexisting
		{
			get { return Spell.AllowCoexisting; }
		}
		
		
		public virtual bool IsSummoningSpell
		{
			get
			{
				if (m_spell.SpellType != (int)eSpellType.Null)
					switch ((eSpellType)m_spell.SpellType)
					{
						case eSpellType.Bomber:
						case eSpellType.Charm:
						case eSpellType.Pet:
						case eSpellType.SummonCommander:
						case eSpellType.SummonTheurgistPet:
						case eSpellType.Summon:
						case eSpellType.SummonJuggernaut:
						//case eSpellType.SummonMerchant:
						case eSpellType.SummonMinion:
						case eSpellType.SummonSimulacrum:
						case eSpellType.SummonUnderhill:
						//case eSpellType.SummonVaultkeeper:
						case eSpellType.SummonAnimistAmbusher:
						case eSpellType.SummonAnimistPet:
						case eSpellType.SummonDruidPet:
						case eSpellType.SummonHealingElemental:
						case eSpellType.SummonHunterPet:
						case eSpellType.SummonAnimistFnF:
						case eSpellType.SummonAnimistFnFCustom:
						case eSpellType.SummonSiegeBallista:
						case eSpellType.SummonSiegeCatapult:
						case eSpellType.SummonSiegeRam:
						case eSpellType.SummonSiegeTrebuchet:
						case eSpellType.SummonSpiritFighter:
						case eSpellType.SummonNecroPet:
						//case eSpellType.SummonNoveltyPet:
							return true;
						default:
							return false;
					}
				
				return false;
			}
		}


		/// <summary>
		/// The CastingCompleteEvent
		/// </summary>
		public event CastingCompleteCallback CastingCompleteEvent;

		/// <summary>
		/// spell handler constructor
		/// <param name="caster">living that is casting that spell</param>
		/// <param name="spell">the spell to cast</param>
		/// <param name="spellLine">the spell line that spell belongs to</param>
		/// </summary>
		public SpellHandler(GameLiving caster, Spell spell, SpellLine spellLine)
		{
			m_caster = caster;
			m_spell = spell;
			m_spellLine = spellLine;
		}

		/// <summary>
		/// Returns the string representation of the SpellHandler
		/// </summary>
		/// <returns></returns>
		public override string ToString()
		{
			return new StringBuilder(128)
				.Append("Caster=").Append(Caster == null ? "(null)" : Caster.Name)
				.Append(", IsCasting=").Append(IsCasting)
				.Append(", m_interrupted=").Append(m_interrupted)
				.Append("\nSpell: ").Append(Spell == null ? "(null)" : Spell.ToString())
				.Append("\nSpellLine: ").Append(SpellLine == null ? "(null)" : SpellLine.ToString())
				.ToString();
		}

		#region Pulsing Spells

		/// <summary>
		/// When spell pulses
		/// </summary>
		public virtual void OnSpellPulse(PulsingSpellEffect effect)
		{
			if (Caster.IsMoving && Spell.IsFocus)
			{
				MessageToCaster("Your spell was cancelled.", eChatType.CT_SpellExpires);
				effect.Cancel(false);
				return;
			}
			if (Caster.IsAlive == false)
			{
				effect.Cancel(false);
				return;
			}
			if (Caster.ObjectState != GameObject.eObjectState.Active)
				return;
			if (Caster.IsStunned || Caster.IsMezzed)
				return;

			// no instrument anymore = stop the song
			if (m_spell.InstrumentRequirement != 0 && !CheckInstrument())
			{
				MessageToCaster("You stop playing your song.", eChatType.CT_Spell);
				effect.Cancel(false);
				return;
			}

			if (Caster.Mana >= Spell.PulsePower)
			{
				Caster.Mana -= Spell.PulsePower;
				if (Spell.InstrumentRequirement != 0 || !HasPositiveEffect)
				{
					SendEffectAnimation(Caster, 0, true, 1); // pulsing auras or songs
				}

				StartSpell(m_spellTarget);
			}
			else
			{
				if (Spell.IsFocus)
				{
					//FocusSpellAction(null, Caster, null);
				}
				MessageToCaster("You do not have enough power and your spell was canceled.", eChatType.CT_SpellExpires);
				effect.Cancel(false);
			}
		}

		/// <summary>
		/// Checks if caster holds the right instrument for this 
		/// </summary>
		/// <returns>true if right instrument</returns>
		protected bool CheckInstrument()
		{
			InventoryItem instrument = Caster.AttackWeapon;
			// From patch 1.97:  Flutes, Lutes, and Drums will now be able to play any song type, and will no longer be limited to specific songs.
			if (instrument == null || instrument.Object_Type != (int)eObjectType.Instrument ) // || (instrument.DPS_AF != 4 && instrument.DPS_AF != m_spell.InstrumentRequirement))
			{
				return false;
			}
			return true;
		}

		/// <summary>
		/// Cancels first pulsing spell of type
		/// </summary>
		/// <param name="living">owner of pulsing spell</param>
		/// <param name="spellType">type of spell to cancel</param>
		/// <returns>true if any spells were canceled</returns>
		public virtual bool CancelPulsingSpell(GameLiving living, byte spellType)
		{
			//lock (living.ConcentrationEffects)
			//{
			//	for (int i = 0; i < living.ConcentrationEffects.Count; i++)
			//	{
			//		PulsingSpellEffect effect = living.ConcentrationEffects[i] as PulsingSpellEffect;
			//		if (effect == null)
			//			continue;
			//		if (effect.SpellHandler.Spell.SpellType == spellType)
			//		{
			//			effect.Cancel(false);
			//			return true;
			//		}
			//	}
			//}

			lock (living.effectListComponent._effectsLock)
            {
				var effects = living.effectListComponent.GetAllPulseEffects();

				for (int i = 0; i < effects.Count; i++)
                {
                    ECSPulseEffect effect = effects[i];
                    if (effect == null)
                        continue;

					if (effect == null)
						continue;
					if (effect.SpellHandler.Spell.SpellType == spellType)
					{
						EffectService.RequestCancelConcEffect(effect);
						return true;
					}
                }
            }
            return false;
		}

		public static void CancelAllPulsingSpells(GameLiving living)
		{ 		
			var effects = living.effectListComponent.GetAllPulseEffects();//.ConcentrationEffects.Where(e => e is ECSPulseEffect).ToArray();
			for (int i = 0; i < effects.Count(); i++)
			{
				EffectService.RequestImmediateCancelConcEffect(effects[i]);
			}
        }
		/// <summary>
		/// Cancels all pulsing spells
		/// </summary>
		/// <param name="living"></param>
// 		public static void CancelAllPulsingSpells(GameLiving living)
// 		{
// 			//[Takii] I updated this method so things would compile but the only call to it is currently commented and its unclear if we want to keep it.
// 
// 			List<IConcentrationEffect> pulsingSpells = new List<IConcentrationEffect>();
// 
// 			GamePlayer player = living as GamePlayer;
// 
// 			lock (living.ConcentrationEffects)
// 			{
// 				for (int i = 0; i < living.ConcentrationEffects.Count; i++)
// 				{
// 					ECSPulseEffect effect = living.ConcentrationEffects[i] as ECSPulseEffect;
// 					if (effect == null)
// 						continue;
// 
// 					if (player != null && player.CharacterClass.MaxPulsingSpells > 1)
// 						pulsingSpells.Add(effect);
// 					else
// 						EffectService.RequestCancelEffect(effect);
// 				}
// 			}
// 
// 			// Non-concentration spells are grouped at the end of GameLiving.ConcentrationEffects.
// 			// The first one is added at the very end; successive additions are inserted just before the last element
// 			// which results in the following ordering:
// 			// Assume pulsing spells A, B, C, and D were added in that order; X, Y, and Z represent other spells
// 			// ConcentrationEffects = { X, Y, Z, ..., B, C, D, A }
// 			// If there are only ever 2 or less pulsing spells active, then the oldest one will always be at the end.
// 			// However, if an update or modification allows more than 2 to be active, the goofy ordering of the spells
// 			// will prevent us from knowing which spell is the oldest and should be canceled - we can go ahead and simply
// 			// cancel the last spell in the list (which will result in inconsistent behavior) or change the code that adds
// 			// spells to ConcentrationEffects so that it enforces predictable ordering.
// 			if (pulsingSpells.Count > 1)
// 			{
// 				ECSPulseEffect effect = pulsingSpells[pulsingSpells.Count - 1] as ECSPulseEffect;
// 				if (effect != null)
// 				{
// 					EffectService.RequestCancelEffect(effect);
// 				}
// 			}
// 		}

		#endregion

		/// <summary>
		/// Sets the target of the spell to the caster for beneficial effects when not selecting a valid target
		///		ie. You're in the middle of a fight with a mob and want to heal yourself.  Rather than having to switch
		///		targets to yourself to healm and then back to the target, you can just heal yourself
		/// </summary>
		/// <param name="target">The current target of the spell, changed to the player if appropriate</param>
		protected virtual void AutoSelectCaster(ref GameLiving target)
		{
			GameNPC npc = target as GameNPC;
			if (Spell.Target.ToUpper() == "REALM" && Caster is GamePlayer &&
				(npc == null || npc.Realm != Caster.Realm || (npc.Flags & GameNPC.eFlags.PEACE) != 0))
				target = Caster;
		}

		/// <summary>
		/// Cast a spell by using an item
		/// </summary>
		/// <param name="item"></param>
		/// <returns></returns>
		public virtual bool CastSpell(InventoryItem item)
		{
			m_spellItem = item;
			return CastSpell(Caster.TargetObject as GameLiving);
		}

		/// <summary>
		/// Cast a spell by using an Item
		/// </summary>
		/// <param name="targetObject"></param>
		/// <param name="item"></param>
		/// <returns></returns>
		public virtual bool CastSpell(GameLiving targetObject, InventoryItem item)
		{
			m_spellItem = item;
			return CastSpell(targetObject);
		}

        public virtual void CreateECSEffect(ECSGameEffectInitParams initParams)
		{
			// Base function should be empty once all effects are moved to their own effect class.
			new ECSGameSpellEffect(initParams);
        }

        public virtual void CreateECSPulseEffect(GameLiving target, double effectiveness)
        {

            int freq = Spell != null ? Spell.Frequency : 0;
            // return new GameSpellEffect(this, CalculateEffectDuration(target, effectiveness), freq, effectiveness);

            new ECSPulseEffect(target, this, CalculateEffectDuration(target, effectiveness), freq, effectiveness, Spell.Icon);
        }

        /// <summary>
        /// called whenever the player clicks on a spell icon
        /// or a GameLiving wants to cast a spell
        /// </summary>
        public virtual bool CastSpell()
		{
			return CastSpell(Caster.TargetObject as GameLiving);
		}

		public virtual bool CastSpell(GameLiving targetObject)
		{
			bool success = true;
			
			if (Properties.AUTOSELECT_CASTER)
				AutoSelectCaster(ref targetObject);

			m_spellTarget = targetObject;

			Caster.Notify(GameLivingEvent.CastStarting, m_caster, new CastingEventArgs(this));

			//[Stryve]: Do not break stealth if spell can be cast without breaking stealth.
			if (Caster is GamePlayer && UnstealthCasterOnStart)
				((GamePlayer)Caster).Stealth(false);

			if (Caster.IsEngaging)
			{
				EngageECSGameEffect effect = (EngageECSGameEffect)EffectListService.GetEffectOnTarget(Caster, eEffect.Engage);

				if (effect != null)
					effect.Cancel(false);
			}

			m_interrupted = false;

			if (Spell.Target.ToLower() == "pet")
			{
				// Pet is the target, check if the caster is the pet.

				if (Caster is GameNPC && (Caster as GameNPC).Brain is IControlledBrain)
					m_spellTarget = Caster;

				if (Caster is GamePlayer && Caster.ControlledBrain != null && Caster.ControlledBrain.Body != null)
				{
					if (m_spellTarget == null || !Caster.IsControlledNPC(m_spellTarget as GameNPC))
					{
						m_spellTarget = Caster.ControlledBrain.Body;
					}
				}
			}
			else if (Spell.Target.ToLower() == "controlled")
			{
				// Can only be issued by the owner of a pet and the target
				// is always the pet then.

				if (Caster is GamePlayer && Caster.ControlledBrain != null)
					m_spellTarget = Caster.ControlledBrain.Body;
				else
					m_spellTarget = null;
			}

			if (Spell.Pulse != 0 && !Spell.IsFocus && CancelPulsingSpell(Caster, Spell.SpellType))
			{
				if (Spell.InstrumentRequirement == 0)
					MessageToCaster("You cancel your effect.", eChatType.CT_Spell);
				else
					MessageToCaster("You stop playing your song.", eChatType.CT_Spell);
			}
			else if (GameServer.ServerRules.IsAllowedToCastSpell(Caster, m_spellTarget, Spell, m_spellLine))
			{
				if (CheckBeginCast(m_spellTarget))
				{
					//Added to force non-Concentration spells cast on Necromancer to be cast on pet instead
					if (!Spell.IsConcentration && Caster.TargetObject == m_spellTarget && (Caster.TargetObject as GamePlayer) != null 
						&& (Caster.TargetObject as GamePlayer).IsShade && Spell.ID != 5999)
                    {
                        m_spellTarget = m_spellTarget.ControlledBrain.Body;
                    }
					
					if (m_caster is GamePlayer && (m_caster as GamePlayer).IsOnHorse && !HasPositiveEffect)
					{
						(m_caster as GamePlayer).IsOnHorse = false;
					}

					if (!Spell.IsInstantCast)
					{
						StartCastTimer(m_spellTarget);

						if ((Caster is GamePlayer && (Caster as GamePlayer).IsStrafing) || Caster.IsMoving)
							CasterMoves();
					}
					else
					{
						if (Caster.ControlledBrain == null || Caster.ControlledBrain.Body == null || !(Caster.ControlledBrain.Body is NecromancerPet))
						{
							SendCastAnimation(0);
						}

						FinishSpellCast(m_spellTarget);
					}
				}
				else
				{
					success = false;
				}
			}

			// This is critical to restore the casters state and allow them to cast another spell
			if (!IsCasting)
				OnAfterSpellCastSequence();

			return success;
		}


		public virtual void StartCastTimer(GameLiving target)
		{
			m_interrupted = false;
			SendSpellMessages();

			int time = CalculateCastingTime();

			int step1 = time / 3;
			if (step1 > ServerProperties.Properties.SPELL_INTERRUPT_MAXSTAGELENGTH)
				step1 = ServerProperties.Properties.SPELL_INTERRUPT_MAXSTAGELENGTH;
			if (step1 < 1)
				step1 = 1;

			int step3 = time / 3;
			if (step3 > ServerProperties.Properties.SPELL_INTERRUPT_MAXSTAGELENGTH)
				step3 = ServerProperties.Properties.SPELL_INTERRUPT_MAXSTAGELENGTH;
			if (step3 < 1)
				step3 = 1;

			int step2 = time - step1 - step3;
			if (step2 < 1)
				step2 = 1;

			if (Caster is GamePlayer && ServerProperties.Properties.ENABLE_DEBUG)
			{
				(Caster as GamePlayer).Out.SendMessage("[DEBUG] spell time = " + time + ", step1 = " + step1 + ", step2 = " + step2 + ", step3 = " + step3, eChatType.CT_System, eChatLoc.CL_SystemWindow);
			}

			m_castTimer = new DelayedCastTimer(Caster, this, target, step2, step3);
			m_castTimer.Start(step1);
			m_started = GameLoop.GameLoopTime;
			SendCastAnimation();

			if (m_caster.IsMoving || m_caster.IsStrafing)
			{
				CasterMoves();
			}
		}

		/// <summary>
		/// Is called when the caster moves
		/// </summary>
		public virtual void CasterMoves()
		{
			if (Spell.InstrumentRequirement != 0)
				return;

			if (Spell.MoveCast)
				return;

			
            
			if (Caster is GamePlayer)
                if (castState != eCastState.Focusing)
				    (Caster as GamePlayer).Out.SendMessage(LanguageMgr.GetTranslation((Caster as GamePlayer).Client, "SpellHandler.CasterMove"), eChatType.CT_Important, eChatLoc.CL_SystemWindow);
                else
                    Caster.CancelFocusSpell(true);

            InterruptCasting();
        }

		/// <summary>
		/// This sends the spell messages to the player/target.
		///</summary>
		public virtual void SendSpellMessages()
		{
			if (Spell.SpellType != (byte)eSpellType.PveResurrectionIllness && Spell.SpellType != (byte)eSpellType.RvrResurrectionIllness)
			{
				if (Spell.InstrumentRequirement == 0)
				{
					if (Caster is GamePlayer playerCaster)
						// Message: You begin casting a {0} spell!
						MessageToCaster(LanguageMgr.GetTranslation(playerCaster.Client, "SpellHandler.CastSpell.Msg.YouBeginCasting", Spell.Name), eChatType.CT_Spell);
					if (Caster is NecromancerPet {Owner: GamePlayer casterOwner})
						// Message: {0} begins casting a {1} spell!
						casterOwner.Out.SendMessage(LanguageMgr.GetTranslation(casterOwner.Client.Account.Language, "SpellHandler.CastSpell.Msg.PetBeginsCasting", Caster.GetName(0, true), Spell.Name), eChatType.CT_Spell, eChatLoc.CL_SystemWindow);
				}
				else
					if (Caster is GamePlayer songCaster)
						// Message: You begin playing {0}!
						MessageToCaster(LanguageMgr.GetTranslation(songCaster.Client, "SpellHandler.CastSong.Msg.YouBeginPlaying", Spell.Name), eChatType.CT_Spell);
			}
		}

		/// <summary>
		/// casting sequence has a chance for interrupt through attack from enemy
		/// the final decision and the interrupt is done here
		/// TODO: con level dependend
		/// </summary>
		/// <param name="attacker">attacker that interrupts the cast sequence</param>
		/// <returns>true if casting was interrupted</returns>
		public virtual bool CasterIsAttacked(GameLiving attacker)
		{
			//[StephenxPimentel] Check if the necro has MoC effect before interrupting.
			if (Caster is NecromancerPet necroPet)
            {
				GamePlayer Necro = necroPet.Owner as GamePlayer;
				if (Necro.effectListComponent.ContainsEffectForEffectType(eEffect.MasteryOfConcentration))
				{
					return false;
				}
			}
			if (Spell.Uninterruptible)
				return false;

			if (Caster.effectListComponent.ContainsEffectForEffectType(eEffect.MasteryOfConcentration)
				|| Caster.effectListComponent.ContainsEffectForEffectType(eEffect.FacilitatePainworking)
				|| Caster.effectListComponent.ContainsEffectForEffectType(eEffect.QuickCast))
				return false;

			if (IsCasting && (GameLoop.GameLoopTime < _castStartTick + _calculatedCastTime * .5 ))// Stage < 2) //only interrupt if we're under 50% of the way through the cast
			{
				if (Caster.ChanceSpellInterrupt(attacker))
				{
					Caster.LastInterruptMessage = attacker.GetName(0, true) + " attacks you and your spell is interrupted!";
					MessageToLiving(Caster, Caster.LastInterruptMessage, eChatType.CT_SpellResisted);
					InterruptCasting(); // always interrupt at the moment
					return true;
				}
			}
			return false;
		}

		#region begin & end cast check

		public virtual bool CheckBeginCast(GameLiving selectedTarget)
		{
			return CheckBeginCast(selectedTarget, false);
		}

		/// <summary>
		/// All checks before any casting begins
		/// </summary>
		/// <param name="selectedTarget"></param>
		/// <returns></returns>
		public virtual bool CheckBeginCast(GameLiving selectedTarget, bool quiet)
		{
			if (m_caster.ObjectState != GameLiving.eObjectState.Active)
			{
				return false;
			}
 
            if (!m_caster.IsAlive)
			{
				if (!quiet) MessageToCaster("You are dead and can't cast!", eChatType.CT_System);
				return false;
			}

			var quickCast = EffectListService.GetAbilityEffectOnTarget(m_caster, eEffect.QuickCast);
			if (quickCast != null)
				quickCast.ExpireTick = GameLoop.GameLoopTime + quickCast.Duration;

            if (m_spell.Pulse != 0 && m_spell.Frequency > 0)
            {
                if (Spell.IsPulsing && Caster.LastPulseCast != null && Caster.LastPulseCast.Equals(Spell))
                {
                    if (Spell.InstrumentRequirement == 0)
                        MessageToCaster("You cancel your effect.", eChatType.CT_Spell);
                    else
                        MessageToCaster("You stop playing your song.", eChatType.CT_Spell);

					ECSGameSpellEffect cancelEffect = Caster.effectListComponent.GetSpellEffects(eEffect.Pulse).Where(effect => effect.SpellHandler.Spell.Equals(Spell)).FirstOrDefault();
                    if (cancelEffect != null)
                    {
						EffectService.RequestImmediateCancelConcEffect((IConcentrationEffect)cancelEffect);
                        Caster.LastPulseCast = null;
                        //Console.WriteLine("Canceling Effect " + cancelEffect.SpellHandler.Spell.Name);
                    }
                    //else
                        //Console.WriteLine("Error Canceling Effect");

                    return false;
                }
            }

            if (m_caster is GamePlayer)
			{
				long nextSpellAvailTime = m_caster.TempProperties.getProperty<long>(GamePlayer.NEXT_SPELL_AVAIL_TIME_BECAUSE_USE_POTION);

				if (nextSpellAvailTime > m_caster.CurrentRegion.Time && Spell.CastTime > 0) // instant spells ignore the potion cast delay
				{
					((GamePlayer)m_caster).Out.SendMessage(LanguageMgr.GetTranslation(((GamePlayer)m_caster).Client, "GamePlayer.CastSpell.MustWaitBeforeCast", (nextSpellAvailTime - m_caster.CurrentRegion.Time) / 1000), eChatType.CT_System, eChatLoc.CL_SystemWindow);
					return false;
				}
				if (((GamePlayer)m_caster).Steed != null && ((GamePlayer)m_caster).Steed is GameSiegeRam)
				{
					if (!quiet) MessageToCaster("You can't cast in a siegeram!.", eChatType.CT_System);
					return false;
				}
				
			}

            /*
			GameSpellEffect Phaseshift = FindEffectOnTarget(Caster, "Phaseshift");
			if (Phaseshift != null && (Spell.InstrumentRequirement == 0 || Spell.SpellType == (byte)eSpellType.Mesmerize))
			{
				if (!quiet) MessageToCaster("You're phaseshifted and can't cast a spell", eChatType.CT_System);
				return false;
			}*/

			// Apply Mentalist RA5L
			if (Spell.Range>0)
			{
				SelectiveBlindnessEffect SelectiveBlindness = Caster.EffectList.GetOfType<SelectiveBlindnessEffect>();
				if (SelectiveBlindness != null)
				{
					GameLiving EffectOwner = SelectiveBlindness.EffectSource;
					if(EffectOwner==selectedTarget)
					{
						if (m_caster is GamePlayer && !quiet)
							((GamePlayer)m_caster).Out.SendMessage(string.Format("{0} is invisible to you!", selectedTarget.GetName(0, true)), eChatType.CT_Missed, eChatLoc.CL_SystemWindow);

						return false;
					}
				}
			}

			if (selectedTarget!=null && selectedTarget.HasAbility("DamageImmunity") && Spell.SpellType == (byte)eSpellType.DirectDamage && Spell.Radius == 0)
			{
				if (!quiet) MessageToCaster(selectedTarget.Name + " is immune to this effect!", eChatType.CT_SpellResisted);
				return false;
			}

			if (m_spell.InstrumentRequirement != 0)
			{
				if (!CheckInstrument())
				{
					if (!quiet) MessageToCaster("You are not wielding the right type of instrument!",
					                            eChatType.CT_SpellResisted);
					return false;
				}
			}
			else if (m_caster.IsSitting) // songs can be played if sitting
			{
				//Purge can be cast while sitting but only if player has negative effect that
				//don't allow standing up (like stun or mez)
				if (!quiet) MessageToCaster("You can't cast while sitting!", eChatType.CT_SpellResisted);
				return false;
			}

			if (m_caster.attackComponent.AttackState && m_spell.CastTime != 0)
			{
				if (m_caster.CanCastInCombat(Spell) == false)
				{
					if(!(m_caster is GamePet))
						m_caster.attackComponent.LivingStopAttack(); //dont stop melee for pet (probaby look at stopping attack just for game player)
					return false;
				}
			}

			//Check Interrupts for Player
			if (!m_spell.Uninterruptible && m_spell.CastTime > 0 && m_caster is GamePlayer &&
				!m_caster.effectListComponent.ContainsEffectForEffectType(eEffect.QuickCast) && !m_caster.effectListComponent.ContainsEffectForEffectType(eEffect.MasteryOfConcentration))
			{
                if (Caster.InterruptAction > 0 && Caster.InterruptTime > GameLoop.GameLoopTime)
				{
					if (!quiet) MessageToCaster("You must wait " + (((Caster.InterruptTime) - GameLoop.GameLoopTime) / 1000 + 1).ToString() + " seconds to cast a spell!", eChatType.CT_SpellResisted);
					return false;
				}
			}

			//Check Interrupts for NPC
			if (!m_spell.Uninterruptible && m_spell.CastTime > 0 && m_caster is GameNPC)
			{
                if (Caster.InterruptAction > 0 && Caster.InterruptTime > GameLoop.GameLoopTime)
				{
					if(m_caster is NecromancerPet necropet && necropet.effectListComponent.ContainsEffectForEffectType(eEffect.FacilitatePainworking))
					{
						//Necro pet has Facilitate Painworking effect and isn't interrupted.
					}
					else
						return false;
				}
			}

			if (m_spell.RecastDelay > 0)
			{
				int left = m_caster.GetSkillDisabledDuration(m_spell);
				if (left > 0)
				{
					if (m_caster is NecromancerPet && ((m_caster as NecromancerPet).Owner as GamePlayer).Client.Account.PrivLevel > (int)ePrivLevel.Player)
					{
						// Ignore Recast Timer
					}
					else
					{
						if (!quiet) MessageToCaster("You must wait " + (left / 1000 + 1).ToString() + " seconds to use this spell!", eChatType.CT_System);
						return false;
					}
				}
			}

			String targetType = m_spell.Target.ToLower();

			
			//[Ganrod] Nidel: Can cast pet spell on all Pet/Turret/Minion (our pet)
			if (targetType.Equals("pet"))
			{ 
				if (selectedTarget == null || !Caster.IsControlledNPC(selectedTarget as GameNPC) || (selectedTarget != null && selectedTarget != Caster.ControlledBrain?.Body))
				{
					if (Caster.ControlledBrain != null && Caster.ControlledBrain.Body != null)
					{
						selectedTarget = Caster.ControlledBrain.Body;
					}
					else if (selectedTarget is TurretPet)
                    {
						//do nothing
                    }
					else
					{
						if (!quiet) MessageToCaster("You must cast this spell on a creature you are controlling.",
						                            eChatType.CT_System);
						return false;
					}
				}
			}
			if (targetType == "area")
			{
				if (!m_caster.IsWithinRadius(m_caster.GroundTarget, CalculateSpellRange()))
				{
					if (!quiet) MessageToCaster("Your area target is out of range.  Select a closer target.", eChatType.CT_SpellResisted);
					return false;
				}
                //if (!Caster.GroundTargetInView)
                //{
                //    MessageToCaster("Your ground target is not in view!", eChatType.CT_SpellResisted);
                //    return false;
                //}
            }
			else if (targetType != "self" && targetType != "group" && targetType != "pet"
			         && targetType != "controlled" && targetType != "cone" && m_spell.Range > 0)
			{
				// All spells that need a target.

				if (selectedTarget == null || selectedTarget.ObjectState != GameLiving.eObjectState.Active)
				{
					if (!quiet) MessageToCaster("You must select a target for this spell!",
					                            eChatType.CT_SpellResisted);
					return false;
				}

				if (!m_caster.IsWithinRadius(selectedTarget, CalculateSpellRange()))
				{
					if(Caster is GamePlayer && !quiet) MessageToCaster("That target is too far away!",
					                                                   eChatType.CT_SpellResisted);
					Caster.Notify(GameLivingEvent.CastFailed,
					              new CastFailedEventArgs(this, CastFailedEventArgs.Reasons.TargetTooFarAway));
					if (Caster is GameNPC npc)
						npc.Follow(selectedTarget, Spell.Range - 100, GameNPC.STICKMAXIMUMRANGE);
					return false;
				}

				switch (m_spell.Target.ToLower())
				{
					case "enemy":
						if (selectedTarget == m_caster)
						{
							if (!quiet) MessageToCaster("You can't attack yourself! ", eChatType.CT_System);
							return false;
						}

						if (FindStaticEffectOnTarget(selectedTarget, typeof(NecromancerShadeEffect)) != null)
						{
							if (!quiet) MessageToCaster("Invalid target.", eChatType.CT_System);
							return false;
						}

						if (m_spell.SpellType == (byte)eSpellType.Charm && m_spell.CastTime == 0 && m_spell.Pulse != 0)
							break;

						if (m_spell.SpellType != (byte)eSpellType.PetSpell && m_caster.IsObjectInFront(selectedTarget, 180) == false)
						{
							if (!quiet) MessageToCaster("Your target is not in view!", eChatType.CT_SpellResisted);
							Caster.Notify(GameLivingEvent.CastFailed, new CastFailedEventArgs(this, CastFailedEventArgs.Reasons.TargetNotInView));
							return false;
						}

						if (m_caster.TargetInView == false)
						{
							if (!quiet) MessageToCaster("Your target is not visible!", eChatType.CT_SpellResisted);
							Caster.Notify(GameLivingEvent.CastFailed, new CastFailedEventArgs(this, CastFailedEventArgs.Reasons.TargetNotInView));
							return false;
						}

						if (!GameServer.ServerRules.IsAllowedToAttack(Caster, selectedTarget, quiet))
						{
							return false;
						}
						break;

					case "corpse":
						if (selectedTarget.IsAlive || !GameServer.ServerRules.IsSameRealm(Caster, selectedTarget, true))
						{
							if (!quiet) MessageToCaster("This spell only works on dead members of your realm!", eChatType.CT_SpellResisted);
							return false;
						}
						break;

					case "realm":
						if (GameServer.ServerRules.IsAllowedToAttack(Caster, selectedTarget, true))
						{
							return false;
						}
						break;
				}

				//heals/buffs/rez need LOS only to start casting, TargetInView only works if selectedTarget == TargetObject
				if (selectedTarget == Caster.TargetObject && !m_caster.TargetInView && m_spell.Target.ToLower() != "pet")
				{
					if (!quiet) MessageToCaster("Your target is not in visible!", eChatType.CT_SpellResisted);
					Caster.Notify(GameLivingEvent.CastFailed, new CastFailedEventArgs(this, CastFailedEventArgs.Reasons.TargetNotInView));
					return false;
				}

				if (m_spell.Target.ToLower() != "corpse" && !selectedTarget.IsAlive)
				{
					if (!quiet) MessageToCaster(selectedTarget.GetName(0, true) + " is dead!", eChatType.CT_SpellResisted);
					return false;
				}
			}
			
			//Ryan: don't want mobs to have reductions in mana
			if (Spell.Power != 0 && m_caster is GamePlayer && (m_caster as GamePlayer).CharacterClass.ID != (int)eCharacterClass.Savage && m_caster.Mana < PowerCost(selectedTarget) && EffectListService.GetAbilityEffectOnTarget(Caster, eEffect.QuickCast) == null && Spell.SpellType != (byte)eSpellType.Archery)
			{
				if (!quiet) MessageToCaster("You don't have enough power to cast that!", eChatType.CT_SpellResisted);
				return false;
			}

			if (m_caster is GamePlayer && m_spell.Concentration > 0)
			{
				if (m_caster.Concentration < m_spell.Concentration)
				{
					if (!quiet) MessageToCaster("This spell requires " + m_spell.Concentration + " concentration points to cast!", eChatType.CT_SpellResisted);
					return false;
				}

				if (m_caster.effectListComponent.ConcentrationEffects.Count >= MAX_CONC_SPELLS)
				{
					if (!quiet) MessageToCaster($"You can only cast up to {MAX_CONC_SPELLS} simultaneous concentration spells!", eChatType.CT_SpellResisted);
					return false;
				}
			}

			// Cancel engage if user starts attack
			if (m_caster.IsEngaging)
			{
				EngageECSGameEffect engage = (EngageECSGameEffect)EffectListService.GetEffectOnTarget(m_caster, eEffect.Engage);
				if (engage != null)
				{
					engage.Cancel(false);
				}
			}

			if (!(Caster is GamePlayer))
			{
				Caster.Notify(GameLivingEvent.CastSucceeded, this, new PetSpellEventArgs(Spell, SpellLine, selectedTarget));
			}

			return true;
		}

		/// <summary>
		/// Does the area we are in force an LoS check on everything?
		/// </summary>
		/// <param name="living"></param>
		/// <returns></returns>
		protected bool MustCheckLOS(GameLiving living)
		{
			foreach (AbstractArea area in living.CurrentAreas)
			{
				if (area.CheckLOS)
				{
					return true;
				}
			}

			return false;
		}


		/// <summary>
		/// Check the Line of Sight from you to your pet
		/// </summary>
		/// <param name="player">The player</param>
		/// <param name="response">The result</param>
		/// <param name="targetOID">The target OID</param>
		public virtual void CheckLOSYouToPet(GamePlayer player, ushort response, ushort targetOID)
		{
			if (player == null) // Hmm
				return;
			if ((response & 0x100) == 0x100) // In view ?
				return;
			MessageToLiving(player, "Your pet not in view.", eChatType.CT_SpellResisted);
			InterruptCasting(); // break;
		}

		/// <summary>
		/// Check the Line of Sight from a player to a target
		/// </summary>
		/// <param name="player">The player</param>
		/// <param name="response">The result</param>
		/// <param name="targetOID">The target OID</param>
		public virtual void CheckLOSPlayerToTarget(GamePlayer player, GameObject source, GameObject target, bool losOk, EventArgs args, PropertyCollection tempProperties)
		{
			if (player == null) // Hmm
				return;

			if (losOk) // In view?
				return;

			if (ServerProperties.Properties.ENABLE_DEBUG)
			{
				MessageToCaster("LoS Interrupt in CheckLOSPlayerToTarget", eChatType.CT_System);
				log.Debug("LoS Interrupt in CheckLOSPlayerToTarget");
			}

			if (Caster is GamePlayer)
			{
				MessageToCaster("You can't see your target from here!", eChatType.CT_SpellResisted);
				if(Spell.IsFocus && Spell.IsHarmful)
				{
					FocusSpellAction(/*null, Caster, null*/);
				}
			}

			InterruptCasting();
		}

		/// <summary>
		/// Check the Line of Sight from an npc to a target
		/// </summary>
		/// <param name="player">The player</param>
		/// <param name="response">The result</param>
		/// <param name="targetOID">The target OID</param>
		public virtual void CheckLOSNPCToTarget(GamePlayer player, GameObject source, GameObject target, bool losOk, EventArgs args, PropertyCollection tempProperties)
		{
			if (player == null) // Hmm
				return;

			if (losOk) // In view?
				return;

			if (ServerProperties.Properties.ENABLE_DEBUG)
			{
				MessageToCaster("LoS Interrupt in CheckLOSNPCToTarget", eChatType.CT_System);
				log.Debug("LoS Interrupt in CheckLOSNPCToTarget");
			}

			InterruptCasting();
		}
		/// <summary>
		/// Checks after casting before spell is executed
		/// </summary>
		/// <param name="target"></param>
		/// <returns></returns>
		public virtual bool CheckEndCast(GameLiving target)
		{
			if (IsSummoningSpell && Caster.CurrentRegion.IsCapitalCity)
			{
				// Message: You can't summon here!
				ChatUtil.SendErrorMessage(Caster as GamePlayer, "GamePlayer.CastEnd.Fail.BadRegion", null);
				return false;
			}
			
			if (Caster is GameNPC casterNPC && Caster is not NecromancerPet)
				casterNPC.TurnTo(target);

			if (m_caster.ObjectState != GameLiving.eObjectState.Active)
			{
				return false;
			}

			if (!m_caster.IsAlive)
			{
				MessageToCaster("You are dead and can't cast!", eChatType.CT_System);
				return false;
			}

			if (m_spell.InstrumentRequirement != 0)
			{
				if (!CheckInstrument())
				{
					MessageToCaster("You are not wielding the right type of instrument!", eChatType.CT_SpellResisted);
					return false;
				}
			}
			else if (m_caster.IsSitting) // songs can be played if sitting
			{
				//Purge can be cast while sitting but only if player has negative effect that
				//don't allow standing up (like stun or mez)
				MessageToCaster("You can't cast while sitting!", eChatType.CT_SpellResisted);
				return false;
			}

			if (m_spell.Target.ToLower() == "area")
			{
				if (!m_caster.IsWithinRadius(m_caster.GroundTarget, CalculateSpellRange()))
				{
					MessageToCaster("Your area target is out of range.  Select a closer target.", eChatType.CT_SpellResisted);
					return false;
				}
			}
			else if (m_spell.Target.ToLower() != "self" && m_spell.Target.ToLower() != "group" && m_spell.Target.ToLower() != "cone" && m_spell.Range > 0)
			{
				if (m_spell.Target.ToLower() != "pet")
				{
					//all other spells that need a target
					if (target == null || target.ObjectState != GameObject.eObjectState.Active)
					{
						if (Caster is GamePlayer)
							MessageToCaster("You must select a target for this spell!", eChatType.CT_SpellResisted);
						return false;
					}

					if (!m_caster.IsWithinRadius(target, CalculateSpellRange()))
					{
						if (Caster is GamePlayer)
							MessageToCaster("That target is too far away!", eChatType.CT_SpellResisted);
						return false;
					}
				}

				switch (m_spell.Target)
				{
					case "Enemy":
						if (m_spell.SpellType == (byte)eSpellType.Charm)
							break;
						//enemys have to be in front and in view for targeted spells
						if (m_spell.SpellType != (byte)eSpellType.PetSpell && !m_caster.IsObjectInFront(target, 180))
						{
							MessageToCaster("Your target is not in view. The spell fails.", eChatType.CT_SpellResisted);
							return false;
						}

						if (!GameServer.ServerRules.IsAllowedToAttack(Caster, target, false))
						{
							return false;
						}
						break;

					case "Corpse":
						if (target.IsAlive || !GameServer.ServerRules.IsSameRealm(Caster, target, true))
						{
							MessageToCaster("This spell only works on dead members of your realm!",
							                eChatType.CT_SpellResisted);
							return false;
						}
						break;

					case "Realm":
						if (GameServer.ServerRules.IsAllowedToAttack(Caster, target, true))
						{
							return false;
						}
						break;

					case "Pet":
						/*
						 * [Ganrod] Nidel: Can cast pet spell on all Pet/Turret/Minion (our pet)
						 * -If caster target's isn't own pet.
						 *  -check if caster have controlled pet, select this automatically
						 *  -check if target isn't null
						 * -check if target isn't too far away
						 * If all checks isn't true, return false.
						 */
						if (target == null || !Caster.IsControlledNPC(target as GameNPC))
						{
							if (Caster.ControlledBrain != null && Caster.ControlledBrain.Body != null)
							{
								target = Caster.ControlledBrain.Body;
							}
							else
							{
								MessageToCaster("You must cast this spell on a creature you are controlling.", eChatType.CT_System);
								return false;
							}
						}
						//Now check distance for own pet
						if (!m_caster.IsWithinRadius(target, CalculateSpellRange()))
						{
							MessageToCaster("That target is too far away!", eChatType.CT_SpellResisted);
							return false;
						}
						break;
				}
			}

			if (m_caster.Mana <= 0 && Spell.Power > 0 && Spell.SpellType != (byte)eSpellType.Archery)
			{
				MessageToCaster("You have exhausted all of your power and cannot cast spells!", eChatType.CT_SpellResisted);
				return false;
			}
			if (Spell.Power > 0 && m_caster.Mana < PowerCost(target) && EffectListService.GetAbilityEffectOnTarget(Caster, eEffect.QuickCast) == null && Spell.SpellType != (byte)eSpellType.Archery)
			{
				MessageToCaster("You don't have enough power to cast that!", eChatType.CT_SpellResisted);
				return false;
			}

			if (m_caster is GamePlayer && m_spell.Concentration > 0 && m_caster.Concentration < m_spell.Concentration)
			{
				MessageToCaster("This spell requires " + m_spell.Concentration + " concentration points to cast!", eChatType.CT_SpellResisted);
				return false;
			}

			if (m_caster is GamePlayer && m_spell.Concentration > 0 && m_caster.effectListComponent.ConcentrationEffects.Count >= MAX_CONC_SPELLS)
			{
				MessageToCaster($"You can only cast up to {MAX_CONC_SPELLS} simultaneous concentration spells!", eChatType.CT_SpellResisted);
				return false;
			}

			return true;
		}

		public virtual bool CheckDuringCast(GameLiving target)
		{
			return CheckDuringCast(target, false);
		}

		public virtual bool CheckDuringCast(GameLiving target, bool quiet)
		{
			if (m_interrupted)
			{
				return false;
			}

			//if (!m_spell.Uninterruptible && m_spell.CastTime > 0 && m_caster is GamePlayer &&
			//	m_caster.EffectList.GetOfType<QuickCastEffect>() == null && m_caster.EffectList.GetOfType<MasteryofConcentrationEffect>() == null)
			//{
			//	if(Caster.InterruptTime > 0 && Caster.InterruptTime > m_started)
			//	{
			//		if (!quiet)
			//		{
			//			if (Caster.LastInterruptMessage != "") MessageToCaster(Caster.LastInterruptMessage, eChatType.CT_SpellResisted);
			//			else MessageToCaster("You are interrupted and must wait " + ((Caster.InterruptTime - m_started) / 1000 + 1).ToString() + " seconds to cast a spell!", eChatType.CT_SpellResisted);
			//		}
			//		return false;
			//	}
			//}

			if (m_caster.ObjectState != GameLiving.eObjectState.Active)
			{
				return false;
			}

			if (!m_caster.IsAlive)
			{
				if(!quiet) MessageToCaster("You are dead and can't cast!", eChatType.CT_System);
				return false;
			}

			if (m_spell.InstrumentRequirement != 0)
			{
				if (!CheckInstrument())
				{
					if (!quiet) MessageToCaster("You are not wielding the right type of instrument!", eChatType.CT_SpellResisted);
					return false;
				}
			}
			else if (m_caster.IsSitting) // songs can be played if sitting
			{
				//Purge can be cast while sitting but only if player has negative effect that
				//don't allow standing up (like stun or mez)
				if (!quiet) MessageToCaster("You can't cast while sitting!", eChatType.CT_SpellResisted);
				return false;
			}

			if (m_spell.Target.ToLower() == "area")
			{
				if (!m_caster.IsWithinRadius(m_caster.GroundTarget, CalculateSpellRange()))
				{
					if (!quiet) MessageToCaster("Your area target is out of range.  Select a closer target.", eChatType.CT_SpellResisted);
					return false;
				}
			}
			else if (m_spell.Target.ToLower() != "self" && m_spell.Target.ToLower() != "group" && m_spell.Target.ToLower() != "cone" && m_spell.Range > 0)
			{
				if (m_spell.Target.ToLower() != "pet")
				{
					//all other spells that need a target
					if (target == null || target.ObjectState != GameObject.eObjectState.Active)
					{
						if (Caster is GamePlayer && !quiet)
							MessageToCaster("You must select a target for this spell!", eChatType.CT_SpellResisted);
						return false;
					}

					//Removed mid-cast range check - SuiteJ
					//if (Caster is GamePlayer && !m_caster.IsWithinRadius(target, CalculateSpellRange()))
					//{
					//	if (!quiet) MessageToCaster("That target is too far away!", eChatType.CT_SpellResisted);
					//	return false;
					//}
				}

				switch (m_spell.Target.ToLower())
				{
					case "enemy":
						//enemys have to be in front and in view for targeted spells
						if (Caster is GamePlayer && !m_caster.TargetInView && !Caster.IsWithinRadius(target, 64) &&
							m_spell.SpellType != (byte)eSpellType.PetSpell && (!m_spell.IsPulsing && m_spell.SpellType != (byte)eSpellType.Mesmerize))
						{
							if (!quiet) MessageToCaster("Your target is not in view. The spell fails.", eChatType.CT_SpellResisted);
							return false;
						}

						if (ServerProperties.Properties.CHECK_LOS_DURING_CAST)
						{
							GamePlayer playerChecker = null;

							if (target is GamePlayer)
							{
								playerChecker = target as GamePlayer;
							}
							else if (Caster is GamePlayer)
							{
								playerChecker = Caster as GamePlayer;
							}
							else if (Caster is GameNPC && (Caster as GameNPC).Brain != null && (Caster as GameNPC).Brain is IControlledBrain)
							{
								playerChecker = ((Caster as GameNPC).Brain as IControlledBrain).GetPlayerOwner();
							}

							if (playerChecker != null)
							{
								// If the area forces an LoS check then we do it, otherwise we only check
								// if caster or target is a player
								// This will generate an interrupt if LOS check fails

								if (Caster is GamePlayer)
								{
									//playerChecker.Out.SendCheckLOS(Caster, target, new CheckLOSMgrResponse(CheckLOSPlayerToTarget));
									LosCheckMgr chk = new LosCheckMgr();
									chk.LosCheck(playerChecker, Caster, target, new LosMgrResponse(CheckLOSPlayerToTarget), false, Spell.CastTime);
								}
								else if (target is GamePlayer || MustCheckLOS(Caster))
								{
									//playerChecker.Out.SendCheckLOS(Caster, target, new CheckLOSMgrResponse(CheckLOSNPCToTarget));
									LosCheckMgr chk = new LosCheckMgr();
									chk.LosCheck(playerChecker, Caster, target, new LosMgrResponse(CheckLOSNPCToTarget), false, Spell.CastTime);
								}
							}
						}

						if (!GameServer.ServerRules.IsAllowedToAttack(Caster, target, quiet))
						{
							return false;
						}
						break;

					case "corpse":
						if (target.IsAlive || !GameServer.ServerRules.IsSameRealm(Caster, target, quiet))
						{
							if (!quiet) MessageToCaster("This spell only works on dead members of your realm!",
							                            eChatType.CT_SpellResisted);
							return false;
						}
						break;

					case "realm":
						if (GameServer.ServerRules.IsAllowedToAttack(Caster, target, true))
						{
							return false;
						}
						break;

					case "pet":
						/*
						 * Can cast pet spell on all Pet/Turret/Minion (our pet)
						 * -If caster target's isn't own pet.
						 *  -check if caster have controlled pet, select this automatically
						 *  -check if target isn't null
						 * -check if target isn't too far away
						 * If all checks isn't true, return false.
						 */
						if (target == null || !Caster.IsControlledNPC(target as GameNPC))
						{
							if (Caster.ControlledBrain != null && Caster.ControlledBrain.Body != null)
							{
								target = Caster.ControlledBrain.Body;
							}
							else
							{
								if (!quiet) MessageToCaster("You must cast this spell on a creature you are controlling.", eChatType.CT_System);
								return false;
							}
						}
						//Now check distance for own pet
						if (!m_caster.IsWithinRadius(target, CalculateSpellRange()))
						{
							if (!quiet) MessageToCaster("That target is too far away!", eChatType.CT_SpellResisted);
							return false;
						}
						break;
				}
			}

			if (m_caster.Mana <= 0 && Spell.Power > 0 && Spell.SpellType != (byte)eSpellType.Archery)
			{
				if (!quiet) MessageToCaster("You have exhausted all of your power and cannot cast spells!", eChatType.CT_SpellResisted);
				return false;
			}
			if (Spell.Power != 0 && m_caster.Mana < PowerCost(target) && EffectListService.GetAbilityEffectOnTarget(Caster, eEffect.QuickCast) == null && Spell.SpellType != (byte)eSpellType.Archery)
			{
				if (!quiet) MessageToCaster("You don't have enough power to cast that!", eChatType.CT_SpellResisted);
				return false;
			}

			if (m_caster is GamePlayer && m_spell.Concentration > 0 && m_caster.Concentration < m_spell.Concentration)
			{
				if (!quiet) MessageToCaster("This spell requires " + m_spell.Concentration + " concentration points to cast!", eChatType.CT_SpellResisted);
				return false;
			}

			if (m_caster is GamePlayer && m_spell.Concentration > 0 && m_caster.effectListComponent.ConcentrationEffects.Count >= MAX_CONC_SPELLS)
			{
				if (!quiet) MessageToCaster($"You can only cast up to {MAX_CONC_SPELLS} simultaneous concentration spells!", eChatType.CT_SpellResisted);
				return false;
			}
			
			if ((m_caster.IsMoving || m_caster.IsStrafing) && !Spell.MoveCast)
			{
				CasterMoves();
				return false;
			}

			return true;
		}

		public virtual bool CheckAfterCast(GameLiving target)
		{
			return CheckAfterCast(target, false);
		}


		public virtual bool CheckAfterCast(GameLiving target, bool quiet)
		{
			if (m_interrupted)
			{
				return false;
			}

			if (!m_spell.Uninterruptible && m_spell.CastTime > 0 && m_caster is GamePlayer &&
				!m_caster.effectListComponent.ContainsEffectForEffectType(eEffect.QuickCast) && !m_caster.effectListComponent.ContainsEffectForEffectType(eEffect.MasteryOfConcentration))
			{
				if (Caster.InterruptTime > 0 && Caster.InterruptTime > m_started)
				{
					if (!quiet)
					{
						if(Caster.LastInterruptMessage != "") MessageToCaster(Caster.LastInterruptMessage, eChatType.CT_SpellResisted);
						else MessageToCaster("You are interrupted and must wait " + ((Caster.InterruptTime - m_started) / 1000 + 1).ToString() + " seconds to cast a spell!", eChatType.CT_SpellResisted);
					}
					Caster.InterruptAction = GameLoop.GameLoopTime - Caster.SpellInterruptRecastAgain;
					return false;
				}
			}

			if (m_caster.ObjectState != GameLiving.eObjectState.Active)
			{
				return false;
			}

			if (!m_caster.IsAlive)
			{
				if (!quiet) MessageToCaster("You are dead and can't cast!", eChatType.CT_System);
				return false;
			}

			if (m_spell.InstrumentRequirement != 0)
			{
				if (!CheckInstrument())
				{
					if (!quiet) MessageToCaster("You are not wielding the right type of instrument!", eChatType.CT_SpellResisted);
					return false;
				}
			}
			else if (m_caster.IsSitting) // songs can be played if sitting
			{
				//Purge can be cast while sitting but only if player has negative effect that
				//don't allow standing up (like stun or mez)
				if (!quiet) MessageToCaster("You can't cast while sitting!", eChatType.CT_SpellResisted);
				return false;
			}

			if (m_spell.Target.ToLower() == "area")
			{
				if (!m_caster.IsWithinRadius(m_caster.GroundTarget, CalculateSpellRange()))
				{
					if (!quiet) MessageToCaster("Your area target is out of range.  Select a closer target.", eChatType.CT_SpellResisted);
					return false;
				}
                //if (!Caster.GroundTargetInView)
                //{
                //    MessageToCaster("Your ground target is not in view!", eChatType.CT_SpellResisted);
                //    return false;
                //}
            }
			else if (m_spell.Target.ToLower() != "self" && m_spell.Target.ToLower() != "group" && m_spell.Target.ToLower() != "cone" && m_spell.Range > 0)
			{
				if (m_spell.Target.ToLower() != "pet")
				{
					//all other spells that need a target
					if (target == null || target.ObjectState != GameObject.eObjectState.Active)
					{
						if (Caster is GamePlayer && !quiet)
							MessageToCaster("FYou must select a target for this spell!", eChatType.CT_SpellResisted);
						return false;
					}

					//Removed mid-cast range check - SuiteJ
					//if (Caster is GamePlayer && !m_caster.IsWithinRadius(target, CalculateSpellRange()))
					//{
					//	if (!quiet) MessageToCaster("That target is too far away!", eChatType.CT_SpellResisted);
					//	return false;
					//}
				}

				switch (m_spell.Target)
				{
					case "Enemy":
						//enemys have to be in front and in view for targeted spells
						if (Caster is GamePlayer && m_spell.SpellType != (byte)eSpellType.PetSpell && !m_caster.IsObjectInFront(target, 180) && !Caster.IsWithinRadius(target, 50))
						{
							if (!quiet) MessageToCaster("Your target is not in view. The spell fails.", eChatType.CT_SpellResisted);
							return false;
						}

						if (!GameServer.ServerRules.IsAllowedToAttack(Caster, target, quiet))
						{
							return false;
						}
						break;

					case "Corpse":
						if (target.IsAlive || !GameServer.ServerRules.IsSameRealm(Caster, target, quiet))
						{
							if (!quiet) MessageToCaster("This spell only works on dead members of your realm!", eChatType.CT_SpellResisted);
							return false;
						}
						break;

					case "Realm":
						if (GameServer.ServerRules.IsAllowedToAttack(Caster, target, true))
						{
							return false;
						}
						break;

					case "Pet":
						/*
						 * [Ganrod] Nidel: Can cast pet spell on all Pet/Turret/Minion (our pet)
						 * -If caster target's isn't own pet.
						 *  -check if caster have controlled pet, select this automatically
						 *  -check if target isn't null
						 * -check if target isn't too far away
						 * If all checks isn't true, return false.
						 */
						if (target == null || !Caster.IsControlledNPC(target as GameNPC))
						{
							if (Caster.ControlledBrain != null && Caster.ControlledBrain.Body != null)
							{
								target = Caster.ControlledBrain.Body;
							}
							else
							{
								if (!quiet) MessageToCaster("You must cast this spell on a creature you are controlling.", eChatType.CT_System);
								return false;
							}
						}
						//Now check distance for own pet
						if (!m_caster.IsWithinRadius(target, CalculateSpellRange()))
						{
							if (!quiet) MessageToCaster("That target is too far away!", eChatType.CT_SpellResisted);
							return false;
						}
						break;
				}
			}

			if (m_caster.Mana <= 0 && Spell.Power > 0 && Spell.SpellType != (byte)eSpellType.Archery)
			{
				if (!quiet) MessageToCaster("You have exhausted all of your power and cannot cast spells!", eChatType.CT_SpellResisted);
				return false;
			}
			if (Spell.Power != 0 && m_caster.Mana < PowerCost(target) && EffectListService.GetAbilityEffectOnTarget(Caster, eEffect.QuickCast) == null && Spell.SpellType != (byte)eSpellType.Archery)
			{
				if (!quiet) MessageToCaster("You don't have enough power to cast that!", eChatType.CT_SpellResisted);
				return false;
			}

			if (m_caster is GamePlayer && m_spell.Concentration > 0 && m_caster.Concentration < m_spell.Concentration)
			{
				if (!quiet) MessageToCaster("This spell requires " + m_spell.Concentration + " concentration points to cast!", eChatType.CT_SpellResisted);
				return false;
			}

			if (m_caster is GamePlayer && m_spell.Concentration > 0 && m_caster.effectListComponent.ConcentrationEffects.Count >= MAX_CONC_SPELLS)
			{
				if (!quiet) MessageToCaster($"You can only cast up to {MAX_CONC_SPELLS} simultaneous concentration spells!", eChatType.CT_SpellResisted);
				return false;
			}

			return true;
		}


		#endregion

		//This is called after our pre-cast checks are done (Range, valid target, mana pre-req, and standing still?) and checks for the casting states
		public void Tick(long currentTick)
		{
				switch (castState)
				{
					case eCastState.Precast:
						if (Spell.Target == "Self")
						{
							// Self spells should ignore whatever we actually have selected.
							m_spellTarget = Caster;
						}
						else
						{
							m_spellTarget = Caster?.TargetObject as GameLiving;

							if (m_spellTarget is null && Caster is NecromancerPet nPet)
							{
								m_spellTarget = (nPet.Brain as NecromancerPetBrain).GetSpellTarget();
							}
						}

						if (CheckBeginCast(m_spellTarget))
						{
							m_started = GameLoop.GameLoopTime;
							_castStartTick = currentTick;
							if (!Spell.IsInstantCast)
								SendSpellMessages();
							if (Spell.IsInstantCast)
							{
								if (!CheckEndCast(m_spellTarget))
									castState = eCastState.Interrupted;
								else
								{
									SendCastAnimation(0);
									castState = eCastState.Finished;
								}
							}
							else
							{
								SendCastAnimation();
								castState = eCastState.Casting;
							}
						}
						else
						{
							if (Caster.InterruptAction > 0 && Caster.InterruptTime > GameLoop.GameLoopTime)
								castState = eCastState.Interrupted;
							else
								castState = eCastState.Cleanup;
						}
						break;
					case eCastState.Casting:
						if (!CheckDuringCast(m_spellTarget))
						{
							castState = eCastState.Interrupted;
						}
						if (_castStartTick + _calculatedCastTime < currentTick)
						{
							if (!(m_spell.IsPulsing && m_spell.SpellType == (byte)eSpellType.Mesmerize))
							{
								if (!CheckEndCast(m_spellTarget))
									castState = eCastState.Interrupted;
								else
									castState = eCastState.Finished;
							}
							else
							{
								if (CheckEndCast(m_spellTarget))
									castState = eCastState.Finished;
							}
						}
						break;
					case eCastState.Interrupted:
						InterruptCasting();
						SendInterruptCastAnimation();
						castState = eCastState.Cleanup;
						break;
					case eCastState.Focusing:
						if ((Caster is GamePlayer && (Caster as GamePlayer).IsStrafing) || Caster.IsMoving)
						{
							CasterMoves();
							castState = eCastState.Cleanup;
						}
						break;
				}

			//Process cast on same tick if finished.
			if (castState == eCastState.Finished)
			{
				FinishSpellCast(m_spellTarget);
				if (Spell.IsFocus)
				{
					if (Spell.ID != 5998)
					{
						castState = eCastState.Focusing;
					}
					else
					{
						castState = eCastState.Cleanup;

						var stone = Caster.Inventory.GetFirstItemByName("Personal Bind Recall Stone", eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
						stone.CanUseAgainIn = stone.CanUseEvery;

						//.SetCooldown();
					}
				}			
				else
					castState = eCastState.Cleanup;
			}
			if (castState == eCastState.Cleanup)
			{
				CleanupSpellCast();
			}

			
		}

		public void CleanupSpellCast()
		{
			if (Caster is GamePlayer p)
			{
				if (Spell.CastTime > 0)
				{
					if (p.castingComponent.queuedSpellHandler != null && p.SpellQueue)
					{
						p.castingComponent.spellHandler = p.castingComponent.queuedSpellHandler;
						p.castingComponent.queuedSpellHandler = null;
					}
					else
					{
						p.castingComponent.spellHandler = null;
					}
				}
				else
				{
					p.castingComponent.instantSpellHandler = null;
				}
			}
			else if (Caster is NecromancerPet nPet)
			{
				if (nPet.Brain is NecromancerPetBrain necroBrain)
				{
					if (Spell.CastTime > 0)
					{
						necroBrain.RemoveSpellFromQueue();
						if (nPet.attackComponent.AttackState)
							necroBrain.RemoveSpellFromAttackQueue();

						if (Caster.castingComponent.queuedSpellHandler != null)
						{
							Caster.castingComponent.spellHandler = Caster.castingComponent.queuedSpellHandler;
							Caster.castingComponent.queuedSpellHandler = null;
						}
						else
						{
							Caster.castingComponent.spellHandler = null;
						}

						if (necroBrain.SpellsQueued)
							necroBrain.CheckSpellQueue();
					}
					else
					{
						if (nPet.attackComponent.AttackState)
							necroBrain.RemoveSpellFromAttackQueue();

						Caster.castingComponent.instantSpellHandler = null;
					}
				}
			}
			else
			{
				if (Caster.castingComponent.queuedSpellHandler != null)
				{
					Caster.castingComponent.spellHandler = Caster.castingComponent.queuedSpellHandler;
					Caster.castingComponent.queuedSpellHandler = null;
				}
				else
				{
					Caster.castingComponent.spellHandler = null;
				}
			}
		}

		

		/// <summary>
		/// Calculates the power to cast the spell
		/// </summary>
		/// <param name="target"></param>
		/// <returns></returns>
		public virtual int PowerCost(GameLiving target)
		{
			/*
			// warlock
			GameSpellEffect effect = SpellHandler.FindEffectOnTarget(m_caster, "Powerless");
			if (effect != null && !m_spell.IsPrimary)
				return 0;*/

			//1.108 - Valhallas Blessing now has a 75% chance to not use power.
			ValhallasBlessingEffect ValhallasBlessing = m_caster.EffectList.GetOfType<ValhallasBlessingEffect>();
			if (ValhallasBlessing != null && Util.Chance(75))
				return 0;

			//patch 1.108 increases the chance to not use power to 50%.
			FungalUnionEffect FungalUnion = m_caster.EffectList.GetOfType<FungalUnionEffect>();
			{
				if (FungalUnion != null && Util.Chance(50))
					return 0;
			}

			// Arcane Syphon chance
			int syphon = Caster.GetModified(eProperty.ArcaneSyphon);
			if (syphon > 0)
			{
				if(Util.Chance(syphon))
				{
					return 0;
				}
			}

			double basepower = m_spell.Power; //<== defined a basevar first then modified this base-var to tell %-costs from absolut-costs

			// percent of maxPower if less than zero
			if (basepower < 0)
			{
				if (Caster is GamePlayer && ((GamePlayer)Caster).CharacterClass.ManaStat != eStat.UNDEFINED)
				{
					GamePlayer player = Caster as GamePlayer;
					basepower = player.CalculateMaxMana(player.Level, player.GetBaseStat(player.CharacterClass.ManaStat)) * basepower * -0.01;
				}
				else
				{
					basepower = Caster.MaxMana * basepower * -0.01;
				}
			}

			double power = basepower * 1.2; //<==NOW holding basepower*1.2 within 'power'

			eProperty focusProp = SkillBase.SpecToFocus(SpellLine.Spec);
			if (focusProp != eProperty.Undefined)
			{
				double focusBonus = Caster.GetModified(focusProp) * 0.4;
				if (Spell.Level > 0)
					focusBonus /= Spell.Level;
				if (focusBonus > 0.4)
					focusBonus = 0.4;
				else if (focusBonus < 0)
					focusBonus = 0;
				if (Caster is GamePlayer)
				{
					var spec = ((GamePlayer)Caster).GetModifiedSpecLevel(SpellLine.Spec);
					double specBonus = Math.Min(spec, 50) / (Spell.Level * 1.0);
					if (specBonus > 1)
						specBonus = 1;
					focusBonus *= specBonus;
				}
				power -= basepower * focusBonus; //<== So i can finally use 'basepower' for both calculations: % and absolut
			}
			else if (Caster is GamePlayer && ((GamePlayer)Caster).CharacterClass.ClassType == eClassType.Hybrid)
			{
				double specBonus = 0;
				if (Spell.Level != 0) specBonus = (((GamePlayer)Caster).GetBaseSpecLevel(SpellLine.Spec) * 0.4 / Spell.Level);

				if (specBonus > 0.4)
					specBonus = 0.4;
				else if (specBonus < 0)
					specBonus = 0;
				power -= basepower * specBonus;
			}
			// doubled power usage if quickcasting
			if (EffectListService.GetAbilityEffectOnTarget(Caster, eEffect.QuickCast) != null && Spell.CastTime > 0)
				power *= 2;
			return (int)power;
		}

		/// <summary>
		/// Calculates the enduance cost of the spell
		/// </summary>
		/// <returns></returns>
		public virtual int CalculateEnduranceCost()
		{
			return 5;
		}

		/// <summary>
		/// Calculates the range to target needed to cast the spell
		/// NOTE: This method returns a minimum value of 32
		/// </summary>
		/// <returns></returns>
		public virtual int CalculateSpellRange()
		{
			int range = Math.Max(32, (int)(Spell.Range * Caster.GetModified(eProperty.SpellRange) * 0.01));
			return range;
			//Dinberg: add for warlock range primer
		}

		/// <summary>
		/// Called whenever the casters casting sequence is to interrupt immediately
		/// </summary>
		public virtual void InterruptCasting()
		{
			//castState = eCastState.Interrupted;
			if (m_interrupted || !IsCasting)
				return;

			m_interrupted = true;

			if (IsCasting)
			{
				Parallel.ForEach((m_caster.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE)).OfType<GamePlayer>(), player =>
				{
					player.Out.SendInterruptAnimation(m_caster);
				});
			}
			
			if(m_caster is GamePlayer p && p.castingComponent != null)
            {
				p.castingComponent.spellHandler = null;
				p.castingComponent.queuedSpellHandler = null;
            }

			if (m_castTimer != null)
			{
				m_castTimer.Stop();
				m_castTimer = null;

				if (m_caster is GamePlayer)
				{
					((GamePlayer)m_caster).ClearSpellQueue();
				}
			}
			castState = eCastState.Interrupted;
			m_startReuseTimer = false;
			OnAfterSpellCastSequence();
		}

		/// <summary>
		/// Special use case for when Amnesia isued used against the caster
		/// </summary>
		public virtual void AmnesiaInterruptCasting()
		{
			//castState = eCastState.Interrupted;
			if (m_interrupted || !IsCasting)
				return;
			
			if(m_caster is GamePlayer p && p.castingComponent != null)
            {
				p.castingComponent.spellHandler = null;
				//p.castingComponent.queuedSpellHandler = null;
            }

			if (m_castTimer != null)
			{
				m_castTimer.Stop();
				m_castTimer = null;

				// if (m_caster is GamePlayer)
				// {
				// 	((GamePlayer)m_caster).ClearSpellQueue();
				// }
			}
			//castState = eCastState.Interrupted;
			m_startReuseTimer = false;
			OnAfterSpellCastSequence();
		}

		/// <summary>
		/// Casts a spell after the CastTime delay
		/// </summary>
		protected class DelayedCastTimer : GameTimer
		{
			/// <summary>
			/// The spellhandler instance with callbacks
			/// </summary>
			private readonly SpellHandler m_handler;
			/// <summary>
			/// The target object at the moment of CastSpell call
			/// </summary>
			private readonly GameLiving m_target;
			private readonly GameLiving m_caster;
			private byte m_stage;
			private readonly int m_delay1;
			private readonly int m_delay2;

			/// <summary>
			/// Constructs a new DelayedSpellTimer
			/// </summary>
			/// <param name="actionSource">The caster</param>
			/// <param name="handler">The spell handler</param>
			/// <param name="target">The target object</param>
			public DelayedCastTimer(GameLiving actionSource, SpellHandler handler, GameLiving target, int delay1, int delay2)
				: base(actionSource.CurrentRegion.TimeManager)
			{
				if (handler == null)
					throw new ArgumentNullException("handler");

				if (actionSource == null)
					throw new ArgumentNullException("actionSource");

				m_handler = handler;
				m_target = target;
				m_caster = actionSource;
				m_stage = 0;
				m_delay1 = delay1;
				m_delay2 = delay2;
			}

			/// <summary>
			/// Called on every timer tick
			/// </summary>
			protected override void OnTick()
			{
				try
				{
					if (m_stage == 0)
					{
						if (!m_handler.CheckAfterCast(m_target))
						{
							Interval = 0;
							m_handler.InterruptCasting();
							m_handler.OnAfterSpellCastSequence();
							return;
						}
						m_stage = 1;
						m_handler.Stage = 1;
						Interval = m_delay1;
					}
					else if (m_stage == 1)
					{
						if (!m_handler.CheckDuringCast(m_target))
						{
							Interval = 0;
							m_handler.InterruptCasting();
							m_handler.OnAfterSpellCastSequence();
							return;
						}
						m_stage = 2;
						m_handler.Stage = 2;
						Interval = m_delay2;
					}
					else if (m_stage == 2)
					{
						m_stage = 3;
						m_handler.Stage = 3;
						Interval = 100;
						
						if (m_handler.CheckEndCast(m_target))
						{
							m_handler.FinishSpellCast(m_target);
						}
					}
					else
					{
						m_stage = 4;
						m_handler.Stage = 4;
						Interval = 0;
						m_handler.OnAfterSpellCastSequence();
					}

					if (m_caster is GamePlayer && ServerProperties.Properties.ENABLE_DEBUG && m_stage < 3)
					{
						(m_caster as GamePlayer).Out.SendMessage("[DEBUG] step = " + (m_handler.Stage + 1), eChatType.CT_System, eChatLoc.CL_SystemWindow);
					}

					return;
				}
				catch (Exception e)
				{
					if (log.IsErrorEnabled)
						log.Error(ToString(), e);
				}

				m_handler.OnAfterSpellCastSequence();
				Interval = 0;
			}

			/// <summary>
			/// Returns short information about the timer
			/// </summary>
			/// <returns>Short info about the timer</returns>
			public override string ToString()
			{
				return new StringBuilder(base.ToString(), 128)
					.Append(" spellhandler: (").Append(m_handler.ToString()).Append(')')
					.ToString();
			}
		}

		/// <summary>
		/// Calculates the effective casting time
		/// </summary>
		/// <returns>effective casting time in milliseconds</returns>
		public virtual int CalculateCastingTime()
		{
			return m_caster.CalculateCastingTime(m_spellLine, m_spell);
		}


		#region animations

		/// <summary>
		/// Sends the cast animation
		/// </summary>
		public virtual void SendCastAnimation()
		{
            if (Spell.CastTime == 0)
            {
                SendCastAnimation(0);
            }
            else
            {
				ushort castTime = (ushort)(CalculateCastingTime() / 100);
                SendCastAnimation(castTime);
            }
		}

		/// <summary>
		/// Sends the cast animation
		/// </summary>
		/// <param name="castTime">The cast time</param>
		public virtual void SendCastAnimation(ushort castTime)
		{
			_calculatedCastTime = castTime * 100;
            //Console.WriteLine($"Cast Animation - CastTime Sent to Clients: {castTime} CalcTime: {_calculatedCastTime} Predicted Tick: {GameLoop.GameLoopTime + _calculatedCastTime}");

            Parallel.ForEach(m_caster.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE).OfType<GamePlayer>(), player =>
			{
				if (player == null)
					return;
				player.Out.SendSpellCastAnimation(m_caster, m_spell.ClientEffect, castTime);
			});
		}

		/// <summary>
		/// Send the Effect Animation
		/// </summary>
		/// <param name="target">The target object</param>
		/// <param name="boltDuration">The duration of a bolt</param>
		/// <param name="noSound">sound?</param>
		/// <param name="success">spell success?</param>
		public virtual void SendEffectAnimation(GameObject target, ushort boltDuration, bool noSound, byte success)
		{
			if (target == null)
				target = m_caster;

			//foreach (GamePlayer player in target.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
			//{
			//	player.Out.SendSpellEffectAnimation(m_caster, target, m_spell.ClientEffect, boltDuration, noSound, success);
			//}
			Parallel.ForEach((target.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE)).OfType<GamePlayer>(), player =>
			{
				player.Out.SendSpellEffectAnimation(m_caster, target, m_spell.ClientEffect, boltDuration, noSound, success);
			});
		}

		/// <summary>
		/// Send the Interrupt Cast Animation
		/// </summary>
		public virtual void SendInterruptCastAnimation()
		{
			Parallel.ForEach((m_caster.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE)).OfType<GamePlayer>(), player =>
			{
				player.Out.SendInterruptAnimation(m_caster);
			});
		}
		public virtual void SendEffectAnimation(GameObject target, ushort clientEffect, ushort boltDuration, bool noSound, byte success)
		{
			if (target == null)
				target = m_caster;

			Parallel.ForEach((target.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE)).OfType<GamePlayer>(), player =>
			{
				player.Out.SendSpellEffectAnimation(m_caster, target, clientEffect, boltDuration, noSound, success);
			});
		}
		#endregion

		/// <summary>
		/// called after normal spell cast is completed and effect has to be started
		/// </summary>
		public virtual void FinishSpellCast(GameLiving target)
		{
			if (Caster is GamePlayer && ((GamePlayer)Caster).IsOnHorse && !HasPositiveEffect)
				((GamePlayer)Caster).IsOnHorse = false;
			
			//[Stryve]: Do not break stealth if spell never breaks stealth.
			if ((Caster is GamePlayer) && UnstealthCasterOnFinish )
				((GamePlayer)Caster).Stealth(false);

			if (Caster is GamePlayer && !HasPositiveEffect)
			{
				if (Caster.AttackWeapon != null && Caster.AttackWeapon is GameInventoryItem)
				{
					(Caster.AttackWeapon as GameInventoryItem).OnSpellCast(Caster, target, Spell);
				}
			}

			// messages
			if (Spell.InstrumentRequirement == 0 && Spell.ClientEffect != 0) // && Spell.CastTime > 0 - Takii - Commented this out since we do want cast messages even for insta spells...
			{
				if (Spell.SpellType != (byte)eSpellType.PveResurrectionIllness && Spell.SpellType != (byte)eSpellType.RvrResurrectionIllness)
				{
					if (Caster is GamePlayer playerCaster)
						// Message: You cast a {0} spell!
						MessageToCaster(LanguageMgr.GetTranslation(playerCaster.Client, "SpellHandler.CastSpell.Msg.YouCastSpell", Spell.Name), eChatType.CT_Spell);
					if (Caster is NecromancerPet {Owner: GamePlayer casterOwner})
						// Message: {0} cast a {1} spell!
						casterOwner.Out.SendMessage(LanguageMgr.GetTranslation(casterOwner.Client.Account.Language, "SpellHandler.CastSpell.Msg.PetCastSpell", Caster.GetName(0, true), Spell.Name), eChatType.CT_Spell, eChatLoc.CL_SystemWindow);
					foreach (GamePlayer player in m_caster.GetPlayersInRadius(WorldMgr.INFO_DISTANCE))
					{
						if (player != m_caster)
							// Message: {0} casts a spell!
							player.MessageFromArea(m_caster, LanguageMgr.GetTranslation(player.Client, "SpellHandler.CastSpell.Msg.LivingCastsSpell", Caster.GetName(0, true)), eChatType.CT_Spell, eChatLoc.CL_SystemWindow);
					}
				}
			}

            //CancelAllPulsingSpells(Caster);
            //PulsingSpellEffect pulseeffect = new PulsingSpellEffect(this);
            //pulseeffect.Start();
            //// show animation on caster for positive spells, negative shows on every StartSpell
            //if (m_spell.Target == "Self" || m_spell.Target == "Group")
            //	SendEffectAnimation(Caster, 0, false, 1);
            //if (m_spell.Target == "Pet")
            //	SendEffectAnimation(target, 0, false,1);

            if (Spell.IsPulsing)
            {
				CancelAllPulsingSpells(Caster);
				//EffectService.RequestImmediateCancelConcEffect(EffectListService.GetPulseEffectOnTarget(Caster));

				if (m_spell.SpellType != (byte)eSpellType.Mesmerize)
				{
					CreateECSPulseEffect(Caster, Caster.Effectiveness);
					Caster.LastPulseCast = Spell;
				}
			}

            

            //CreateSpellEffects();
            StartSpell(target); // and action

            /*
			//Dinberg: This is where I moved the warlock part (previously found in gameplayer) to prevent
			//cancelling before the spell was fired.
			if (m_spell.SpellType != (byte)eSpellType.Powerless && m_spell.SpellType != (byte)eSpellType.Range && m_spell.SpellType != (byte)eSpellType.Uninterruptable)
			{
				GameSpellEffect effect = SpellHandler.FindEffectOnTarget(m_caster, "Powerless");
				if (effect == null)
					effect = SpellHandler.FindEffectOnTarget(m_caster, "Range");
				if (effect == null)
					effect = SpellHandler.FindEffectOnTarget(m_caster, "Uninterruptable");

				//if we found an effect, cancel it!
				if (effect != null)
					effect.Cancel(false);
			}*/

			//the quick cast is unallowed whenever you miss the spell
			//set the time when casting to can not quickcast during a minimum time
			if (m_caster is GamePlayer)
			{
				QuickCastECSGameEffect quickcast = (QuickCastECSGameEffect)EffectListService.GetAbilityEffectOnTarget(m_caster, eEffect.QuickCast);
				if (quickcast != null && Spell.CastTime > 0)
				{
					m_caster.TempProperties.setProperty(GamePlayer.QUICK_CAST_CHANGE_TICK, m_caster.CurrentRegion.Time);
					((GamePlayer)m_caster).DisableSkill(SkillBase.GetAbility(Abilities.Quickcast), QuickCastAbilityHandler.DISABLE_DURATION);
					//EffectService.RequestImmediateCancelEffect(quickcast, false);
					quickcast.Cancel(false);
				}
			}


			if (m_ability != null)
				m_caster.DisableSkill(m_ability.Ability, (m_spell.RecastDelay == 0 ? 3000 : m_spell.RecastDelay));

			// disable spells with recasttimer (Disables group of same type with same delay)
			if (m_spell.RecastDelay > 0 && m_startReuseTimer)
			{
				if (m_caster is GamePlayer)
				{
					ICollection<Tuple<Skill, int>> toDisable = new List<Tuple<Skill, int>>();
					
					GamePlayer gp_caster = m_caster as GamePlayer;
					foreach (var skills in gp_caster.GetAllUsableSkills())
						if (skills.Item1 is Spell &&
						    (((Spell)skills.Item1).ID == m_spell.ID || ( ((Spell)skills.Item1).SharedTimerGroup != 0 && ( ((Spell)skills.Item1).SharedTimerGroup == m_spell.SharedTimerGroup) ) ))
							toDisable.Add(new Tuple<Skill, int>((Spell)skills.Item1, m_spell.RecastDelay));
					
					foreach (var sl in gp_caster.GetAllUsableListSpells())
						foreach(var sp in sl.Item2)
							if (sp is Spell &&
							    ( ((Spell)sp).ID == m_spell.ID || ( ((Spell)sp).SharedTimerGroup != 0 && ( ((Spell)sp).SharedTimerGroup == m_spell.SharedTimerGroup) ) ))
							toDisable.Add(new Tuple<Skill, int>((Spell)sp, m_spell.RecastDelay));
					
					m_caster.DisableSkill(toDisable);
				}
				else if (m_caster is GameNPC)
					m_caster.DisableSkill(m_spell, m_spell.RecastDelay);
			}

			/*if(Caster is GamePlayer && target != null)
            {
				(Caster as GamePlayer).Out.SendObjectUpdate(target);
            }*/
			if(!this.Spell.IsPulsingEffect && !this.Spell.IsPulsing && Caster is GamePlayer {CharacterClass: not ClassSavage})
				m_caster.ChangeEndurance(m_caster, eEnduranceChangeType.Spell, -5);

			GameEventMgr.Notify(GameLivingEvent.CastFinished, m_caster, new CastingEventArgs(this, target, m_lastAttackData));
		}

		/// <summary>
		/// Select all targets for this spell
		/// </summary>
		/// <param name="castTarget"></param>
		/// <returns></returns>
		public virtual IList<GameLiving> SelectTargets(GameObject castTarget)
		{
			var list = new List<GameLiving>(8);
			GameLiving target = castTarget as GameLiving;
			bool targetchanged = false;
			string modifiedTarget = Spell.Target.ToLower();
			ushort modifiedRadius = (ushort)Spell.Radius;
			int newtarget = 0;

			/*
			GameSpellEffect TargetMod = SpellHandler.FindEffectOnTarget(m_caster, "TargetModifier");
			if (TargetMod != null)
			{
				if (modifiedTarget == "enemy" || modifiedTarget == "realm" || modifiedTarget == "group")
				{
					newtarget = (int)TargetMod.Spell.Value;

					switch (newtarget)
					{
						case 0: // Apply on heal single
							if (m_spell.SpellType == (byte)eSpellType.Heal && modifiedTarget == "realm")
							{
								modifiedTarget = "group";
								targetchanged = true;
							}
							break;
						case 1: // Apply on heal group
							if (m_spell.SpellType == (byte)eSpellType.Heal && modifiedTarget == "group")
							{
								modifiedTarget = "realm";
								modifiedRadius = (ushort)m_spell.Range;
								targetchanged = true;
							}
							break;
						case 2: // apply on enemy
							if (modifiedTarget == "enemy")
							{
								if (m_spell.Radius == 0)
									modifiedRadius = 450;
								if (m_spell.Radius != 0)
									modifiedRadius += 300;
								targetchanged = true;
							}
							break;
						case 3: // Apply on buff
							if (m_spell.Target.ToLower() == "group"
							    && m_spell.Pulse != 0)
							{
								modifiedTarget = "realm";
								modifiedRadius = (ushort)m_spell.Range;
								targetchanged = true;
							}
							break;
					}
				}
				if (targetchanged)
				{
					if (TargetMod.Duration < 65535)
						TargetMod.Cancel(false);
				}
			}*/

			if (modifiedTarget == "pet" && !HasPositiveEffect)
			{
				modifiedTarget = "enemy";
				//[Ganrod] Nidel: can cast TurretPBAoE on selected Pet/Turret
				if (Spell.SpellType != (byte)eSpellType.TurretPBAoE)
				{
					target = Caster.ControlledBrain.Body;
				}
			}

			#region Process the targets
			switch (modifiedTarget)
			{
					#region GTAoE
					// GTAoE
				case "area":
					//Dinberg - fix for animists turrets, where before a radius of zero meant that no targets were ever
					//selected!
					if (Spell.SpellType == (byte)eSpellType.SummonAnimistPet || Spell.SpellType == (byte)eSpellType.SummonAnimistFnF)
					{
						list.Add(Caster);
					}
					else
						if (modifiedRadius > 0)
					{

						ConcurrentBag<GamePlayer> aoePlayers = new ConcurrentBag<GamePlayer>();
						Parallel.ForEach((WorldMgr.GetPlayersCloseToSpot(Caster.CurrentRegionID, Caster.GroundTarget.X, Caster.GroundTarget.Y, Caster.GroundTarget.Z, modifiedRadius)).OfType<GamePlayer>(), player =>
						{
							if (GameServer.ServerRules.IsAllowedToAttack(Caster, player, true))
							{
								// Apply Mentalist RA5L
								SelectiveBlindnessEffect SelectiveBlindness = Caster.EffectList.GetOfType<SelectiveBlindnessEffect>();
								if (SelectiveBlindness != null)
								{
									GameLiving EffectOwner = SelectiveBlindness.EffectSource;
									if (EffectOwner == player)
									{
										if (Caster is GamePlayer) ((GamePlayer)Caster).Out.SendMessage(string.Format("{0} is invisible to you!", player.GetName(0, true)), eChatType.CT_Missed, eChatLoc.CL_SystemWindow);
									}
									else aoePlayers.Add(player);
								}
								else aoePlayers.Add(player);
							}
						});
						list.AddRange(aoePlayers.Distinct());

						ConcurrentBag<GameNPC> aoeMobs = new ConcurrentBag<GameNPC>();
						Parallel.ForEach((WorldMgr.GetNPCsCloseToSpot(Caster.CurrentRegionID, Caster.GroundTarget.X, Caster.GroundTarget.Y, Caster.GroundTarget.Z, modifiedRadius)).OfType<GameNPC>(), npc =>
						{
							if (npc is GameStorm)
								aoeMobs.Add(npc);
							else if (GameServer.ServerRules.IsAllowedToAttack(Caster, npc, true))
							{
								if (!npc.HasAbility("DamageImmunity")) aoeMobs.Add(npc);
							}
						});
						list.AddRange(aoeMobs.Distinct());
					}
					break;
					#endregion
					#region Corpse
				case "corpse":
					if (target != null && !target.IsAlive)
						list.Add(target);
					break;
					#endregion
					#region Pet
				case "pet":
					{
						//Start-- [Ganrod] Nidel: Can cast Pet spell on our Minion/Turret pet without ControlledNpc
						// awesome, Pbaoe with target pet spell ?^_^
						if (modifiedRadius > 0 && Spell.Range == 0)
						{
							foreach (GameNPC pet in Caster.GetNPCsInRadius(modifiedRadius))
							{
								if (Caster.IsControlledNPC(pet))
								{
									list.Add(pet);
								}
							}
							return list;
						}
						if (target == null)
						{
							break;
						}

						var petBody = target as GameNPC;
						// check target
						if (petBody != null && Caster.IsWithinRadius(petBody, Spell.Range))
						{
							if (Caster.IsControlledNPC(petBody))
							{
								list.Add(petBody);
							}
						}
						//check controllednpc if target isn't pet (our pet)
						if (list.Count < 1 && Caster.ControlledBrain != null)
						{
							if (Caster is GamePlayer player && player.CharacterClass.Name.ToLower() == "bonedancer")
							{
								foreach (var pet in player.GetNPCsInRadius((ushort) Spell.Range))
								{
									if (pet is CommanderPet commander && commander.Owner == player)
									{
										list.Add(commander);
									}
									else if (pet is BDSubPet {Brain: IControlledBrain brain} subpet && brain.GetPlayerOwner() == player)
									{
										if (!Spell.IsHealing)
											list.Add(subpet);
									}
								}
							}
							else
							{
								petBody = Caster.ControlledBrain.Body;
								if (petBody != null && Caster.IsWithinRadius(petBody, Spell.Range))
								{
									list.Add(petBody);
								}
							}
						}

						//Single spell buff/heal...
						if (Spell.Radius == 0)
						{
							return list;
						}
						//Our buff affects every pet in the area of targetted pet (our pets)
						if (Spell.Radius > 0 && petBody != null)
						{
							foreach (GameNPC pet in petBody.GetNPCsInRadius(modifiedRadius))
							{
								//ignore target or our main pet already added
								if (pet == petBody || !Caster.IsControlledNPC(pet))
								{
									continue;
								}
								list.Add(pet);
							}
						}
					}
					//End-- [Ganrod] Nidel: Can cast Pet spell on our Minion/Turret pet without ControlledNpc
					break;
				case "bdsubpet":
					{ 
						var player = Caster as GamePlayer;
						if (player == null) return null;
						var petBody = player.ControlledBrain?.Body;
						if (petBody != null)
						{
							foreach (var pet in petBody.GetNPCsInRadius(modifiedRadius))
							{
								if (pet is not BDSubPet {Brain: IControlledBrain brain} subpet ||
								    brain.GetPlayerOwner() != player) continue;
								if (!Spell.IsHealing)
									list.Add(subpet);
							}

							if (list.Count < 1)
							{
								player.Out.SendMessage("You don't have any subpet to cast this spell on!", eChatType.CT_SpellResisted, eChatLoc.CL_SystemWindow);
							}
						}
					}
					break;
					#endregion
					#region Enemy
				case "enemy":
					if (modifiedRadius > 0)
					{
						if (Spell.SpellType != (byte)eSpellType.TurretPBAoE && (target == null || Spell.Range == 0))
							target = Caster;
						if (target == null) return null;

						ConcurrentBag<GamePlayer> aoePlayers = new ConcurrentBag<GamePlayer>();
						Parallel.ForEach((target.GetPlayersInRadius(modifiedRadius)).OfType<GamePlayer>(), player =>
						{
							if (GameServer.ServerRules.IsAllowedToAttack(Caster, player, true))
							{
								if (GameServer.ServerRules.IsAllowedToAttack(Caster, player, true))
								{
									SelectiveBlindnessEffect SelectiveBlindness = Caster.EffectList.GetOfType<SelectiveBlindnessEffect>();
									if (SelectiveBlindness != null)
									{
										GameLiving EffectOwner = SelectiveBlindness.EffectSource;
										if (EffectOwner == player)
										{
											if (Caster is GamePlayer) ((GamePlayer)Caster).Out.SendMessage(string.Format("{0} is invisible to you!", player.GetName(0, true)), eChatType.CT_Missed, eChatLoc.CL_SystemWindow);
										}
										else aoePlayers.Add(player);
									}
									else aoePlayers.Add(player);
								}
							}
						});
						list.AddRange(aoePlayers.Distinct());

						ConcurrentBag<GameNPC> aoeMobs = new ConcurrentBag<GameNPC>();
						Parallel.ForEach((target.GetNPCsInRadius(modifiedRadius)).OfType<GameNPC>(), npc =>
						{
							if (GameServer.ServerRules.IsAllowedToAttack(Caster, npc, true))
							{
								if (!npc.HasAbility("DamageImmunity")) aoeMobs.Add(npc);
							}
						});
						list.AddRange(aoeMobs.Distinct());
					}
					else
					{
						if (target != null && GameServer.ServerRules.IsAllowedToAttack(Caster, target, true))
						{
							// Apply Mentalist RA5L
							if (Spell.Range > 0)
							{
								SelectiveBlindnessEffect SelectiveBlindness = Caster.EffectList.GetOfType<SelectiveBlindnessEffect>();
								if (SelectiveBlindness != null)
								{
									GameLiving EffectOwner = SelectiveBlindness.EffectSource;
									if (EffectOwner == target)
									{
										if (Caster is GamePlayer) ((GamePlayer)Caster).Out.SendMessage(string.Format("{0} is invisible to you!", target.GetName(0, true)), eChatType.CT_Missed, eChatLoc.CL_SystemWindow);
									}
									else if (!target.HasAbility("DamageImmunity")) list.Add(target);
								}
								else if (!target.HasAbility("DamageImmunity")) list.Add(target);
							}
							else if (!target.HasAbility("DamageImmunity")) list.Add(target);
						}
					}
					break;
					#endregion
					#region Realm
				case "realm":
					if (modifiedRadius > 0)
					{
						if (target == null || Spell.Range == 0)
							target = Caster;

						ConcurrentBag<GameLiving> aoePlayers = new ConcurrentBag<GameLiving>();
						Parallel.ForEach((target.GetPlayersInRadius(modifiedRadius)).OfType<GamePlayer>(), player =>
						{
							if (GameServer.ServerRules.IsAllowedToAttack(Caster, player, true) == false)
							{
								if (player.CharacterClass.ID == (int)eCharacterClass.Necromancer && player.IsShade)
								{
									if (!Spell.IsBuff)
										aoePlayers.Add(player.ControlledBrain.Body);
									else
										aoePlayers.Add(player);
								}
								else
									aoePlayers.Add(player);
							}
						});
						list.AddRange(aoePlayers.Distinct());

						ConcurrentBag<GameNPC> aoeMobs = new ConcurrentBag<GameNPC>();
						Parallel.ForEach((target.GetNPCsInRadius(modifiedRadius)).OfType<GameNPC>(), npc =>
						{
							if (GameServer.ServerRules.IsAllowedToAttack(Caster, npc, true) == false)
							{
								aoeMobs.Add(npc);
							}
						});
						list.AddRange(aoeMobs.Distinct());
					}
					else
					{
						if (target != null && GameServer.ServerRules.IsAllowedToAttack(Caster, target, true) == false)
						{
							if (target is GamePlayer player && player.CharacterClass.ID == (int)eCharacterClass.Necromancer && player.IsShade && Spell.ID != 5999)
							{
								if (!Spell.IsBuff)
									list.Add(player.ControlledBrain.Body);
								else
									list.Add(player);
							}
							else
								list.Add(target);
						}
					}
					break;
					#endregion
					#region Self
				case "self":
					{
						if (modifiedRadius > 0)
						{
							if (target == null || Spell.Range == 0)
								target = Caster;

							ConcurrentBag<GamePlayer> aoePlayers = new ConcurrentBag<GamePlayer>();
							Parallel.ForEach((target.GetPlayersInRadius(modifiedRadius)).OfType<GamePlayer>(), player =>
							{
								if (GameServer.ServerRules.IsAllowedToAttack(Caster, player, true) == false)
								{
									aoePlayers.Add(player);
								}
							});
							list.AddRange(aoePlayers.Distinct());

							ConcurrentBag<GameNPC> aoeMobs = new ConcurrentBag<GameNPC>();
							Parallel.ForEach((target.GetNPCsInRadius(modifiedRadius)).OfType<GameNPC>(), npc =>
							{
								if (GameServer.ServerRules.IsAllowedToAttack(Caster, npc, true) == false)
								{
									aoeMobs.Add(npc);
								}
							});
							list.AddRange(aoeMobs.Distinct());
						}
						else
						{
							list.Add(Caster);
						}
						break;
					}
					#endregion
					#region Group
				case "group":
					{
						Group group = m_caster.Group;
						
						int spellRange;
						if (Spell.Range == 0)
							spellRange = modifiedRadius;
						else
							spellRange = CalculateSpellRange();

						if (group == null)
						{
							if (m_caster is GamePlayer)
							{
								list.Add(m_caster);

								IControlledBrain npc = m_caster.ControlledBrain;
								if (npc != null)
								{
									//Add our first pet
									GameNPC petBody2 = npc.Body;
									if (m_caster.IsWithinRadius(petBody2, spellRange))
										list.Add(petBody2);

									//Now lets add any subpets!
									if (petBody2 != null && petBody2.ControlledNpcList != null)
									{
										foreach (IControlledBrain icb in petBody2.ControlledNpcList)
										{
											if (icb != null && m_caster.IsWithinRadius(icb.Body, spellRange))
												list.Add(icb.Body);
										}
									}
								}
							}// if (m_caster is GamePlayer)
							else if (m_caster is GameNPC && (m_caster as GameNPC).Brain is ControlledNpcBrain)
							{
								IControlledBrain casterbrain = (m_caster as GameNPC).Brain as IControlledBrain;

								GamePlayer player = casterbrain.GetPlayerOwner();

								if (player != null)
								{
									if (player.Group == null)
									{
										// No group, add both the pet and owner to the list
										list.Add(player);
										list.Add(m_caster);
									}
									else
										// Assign the owner's group so they are added to the list
										group = player.Group;
								}
								else
									list.Add(m_caster);
							}// else if (m_caster is GameNPC...
							else
								list.Add(m_caster);
						}// if (group == null)
						
						//We need to add the entire group
						if (group != null)
						{
							foreach (GameLiving living in group.GetMembersInTheGroup())
							{
								// only players in range
								if (m_caster.IsWithinRadius(living, spellRange))
								{
									list.Add(living);

									IControlledBrain npc = living.ControlledBrain;
									if (npc != null)
									{
										//Add our first pet
										GameNPC petBody2 = npc.Body;
										if (m_caster.IsWithinRadius(petBody2, spellRange))
											list.Add(petBody2);

										//Now lets add any subpets!
										if (petBody2 != null && petBody2.ControlledNpcList != null)
										{
											foreach (IControlledBrain icb in petBody2.ControlledNpcList)
											{
												if (icb != null && m_caster.IsWithinRadius(icb.Body, spellRange))
													list.Add(icb.Body);
											}
										}
									}
								}
							}
						}

						break;
					}
					#endregion
					#region Cone AoE
				case "cone":
					{
						target = Caster;

						ConcurrentBag<GamePlayer> aoePlayers = new ConcurrentBag<GamePlayer>();
						Parallel.ForEach((target.GetPlayersInRadius((ushort)Spell.Range)).OfType<GamePlayer>(), player =>
						{
							if (player == Caster)
								return;

							if (!m_caster.IsObjectInFront(player, (double)(Spell.Radius != 0 ? Spell.Radius : 100), false))
								return;

							if (!GameServer.ServerRules.IsAllowedToAttack(Caster, player, true))
								return;

							aoePlayers.Add(player);
						});
						list.AddRange(aoePlayers.Distinct());

						ConcurrentBag<GameNPC> aoeMobs = new ConcurrentBag<GameNPC>();
						Parallel.ForEach((target.GetNPCsInRadius((ushort)Spell.Range)).OfType<GameNPC>(), npc =>
						{
							if (npc == Caster)
								return;

							if (!m_caster.IsObjectInFront(npc, (double)(Spell.Radius != 0 ? Spell.Radius : 100), false))
								return;

							if (!GameServer.ServerRules.IsAllowedToAttack(Caster, npc, true))
								return;

							if (!npc.HasAbility("DamageImmunity")) aoeMobs.Add(npc);
						});
						list.AddRange(aoeMobs.Distinct());
						break;
					}
					#endregion
			}
			#endregion
			return list;
		}

		/// <summary>
		/// Cast all subspell recursively
		/// </summary>
		/// <param name="target"></param>
		public virtual void CastSubSpells(GameLiving target)
		{
			List<int> subSpellList = new List<int>();
			if (m_spell.SubSpellID > 0)
				subSpellList.Add(m_spell.SubSpellID);
			
			foreach (int spellID in subSpellList.Union(m_spell.MultipleSubSpells))
			{
				Spell spell = SkillBase.GetSpellByID(spellID);
				//we need subspell ID to be 0, we don't want spells linking off the subspell
				if (target != null && spell != null && spell.SubSpellID == 0)
				{
					// We have to scale pet subspells when cast
					if (Caster is GamePet pet && !(Caster is NecromancerPet))
						pet.ScalePetSpell(spell);

					ISpellHandler spellhandler = ScriptMgr.CreateSpellHandler(m_caster, spell, SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells));
                    spellhandler.StartSpell(target);
				}
			}
		}

		public virtual List<GameLiving> GetGroupAndPets(Spell spell)
        {
			List<GameLiving> livings = new List<GameLiving>();

			livings.Add(Caster);
			if (Caster.ControlledBrain != null)
				livings.Add(Caster.ControlledBrain.Body);
			if (Caster.Group != null)
			{
				foreach (GameLiving living in Caster.Group.GetMembersInTheGroup().ToList())
				{
					if (living.GetDistanceTo(Caster) > spell.Range)
					{
						continue;
					}
					
					livings.Add(living);

					if (living.ControlledBrain != null)
						livings.Add(living.ControlledBrain.Body);
				}

			}
			else if (Caster is NecromancerPet nPet && nPet.Owner.Group != null)
            {
				foreach (GameLiving living in nPet.Owner.Group.GetMembersInTheGroup().ToList())
				{
					if (living.GetDistanceTo(Caster) > spell.Range)
					{
						continue;
					}
					
					livings.Add(living);

					if (living.ControlledBrain != null)
						livings.Add(living.ControlledBrain.Body);
				}
			}

			return livings;
		}

		/// <summary>
		/// Tries to start a spell attached to an item (/use with at least 1 charge)
		/// Override this to do a CheckBeginCast if needed, otherwise spell will always cast and item will be used.
		/// </summary>
		/// <param name="target"></param>
		/// <param name="item"></param>
		public virtual bool StartSpell(GameLiving target, InventoryItem item)
		{
			m_spellItem = item;
			return StartSpell(target);
		}


		/// <summary>
		/// Called when spell effect has to be started and applied to targets
		/// This is typically called after calling CheckBeginCast
		/// </summary>
		/// <param name="target">The current target object</param>
		public virtual bool StartSpell(GameLiving target)
		{
			if (Caster.IsMezzed || Caster.IsStunned)
				return false;

			if (this.HasPositiveEffect && target is GamePlayer p && Caster is GamePlayer c && target != Caster && p.NoHelp)
			{
				c.Out.SendMessage(target.Name + " has chosen to walk the path of solitude, and your spell fails.", eChatType.CT_SpellResisted, eChatLoc.CL_SystemWindow);
				return false;
			}

            // For PBAOE spells always set the target to the caster
			if (Spell.SpellType != (byte)eSpellType.TurretPBAoE && (target == null || (Spell.Radius > 0 && Spell.Range == 0)))
			{
				target = Caster;
			}

			if (m_spellTarget == null)
				m_spellTarget = target;

			if (m_spellTarget == null) return false;

			IList<GameLiving> targets;
			if (Spell.Target.ToLower() == "realm" && !Spell.IsConcentration && target == Caster && !Spell.IsHealing && Spell.IsBuff && 
				Spell.SpellType != (byte)eSpellType.Bladeturn)
				targets = GetGroupAndPets(Spell);
			else
				targets = SelectTargets(m_spellTarget);

			double effectiveness = Caster.Effectiveness;

			if(SpellLine.KeyName.Equals("OffensiveProc") &&  Caster is GamePet gpet && !Spell.ScaledToPetLevel)
            {
				gpet.ScalePetSpell(Spell);
			}

			/// [Atlas - Takii] No effectiveness drop in OF MOC.
// 			if (Caster.EffectList.GetOfType<MasteryofConcentrationEffect>() != null)
// 			{
// 				AtlasOF_MasteryofConcentration ra = Caster.GetAbility<AtlasOF_MasteryofConcentration>();
// 				if (ra != null && ra.Level > 0)
// 				{
// 					effectiveness *= System.Math.Round((double)ra.GetAmountForLevel(ra.Level) / 100, 2);
// 				}
// 			}

			//[StephenxPimentel] Reduce Damage if necro is using MoC
// 			if (Caster is NecromancerPet)
// 			{
// 				if ((Caster as NecromancerPet).Owner.EffectList.GetOfType<MasteryofConcentrationEffect>() != null)
// 				{
// 					AtlasOF_MasteryofConcentration necroRA = (Caster as NecromancerPet).Owner.GetAbility<AtlasOF_MasteryofConcentration>();
// 					if (necroRA != null && necroRA.Level > 0)
// 					{
// 						effectiveness *= System.Math.Round((double)necroRA.GetAmountForLevel(necroRA.Level) / 100, 2);
// 					}
// 				}
// 			}

			if (Caster is GamePlayer && (Caster as GamePlayer).CharacterClass.ID == (int)eCharacterClass.Warlock && m_spell.IsSecondary)
			{
				Spell uninterruptibleSpell = Caster.TempProperties.getProperty<Spell>(UninterruptableSpellHandler.WARLOCK_UNINTERRUPTABLE_SPELL);

				if (uninterruptibleSpell != null && uninterruptibleSpell.Value > 0)
				{
					double nerf = uninterruptibleSpell.Value;
					effectiveness *= (1 - (nerf * 0.01));
					Caster.TempProperties.removeProperty(UninterruptableSpellHandler.WARLOCK_UNINTERRUPTABLE_SPELL);
				}
			}
			
			Parallel.ForEach(targets, t =>
			{
				
				// Aggressive NPCs will aggro on every target they hit
				// with an AoE spell, whether it landed or was resisted.

				if (Spell.Radius > 0 && Spell.Target.ToLower() == "enemy"
				    && Caster is GameNPC && (Caster as GameNPC).Brain is IOldAggressiveBrain)
					((Caster as GameNPC).Brain as IOldAggressiveBrain).AddToAggroList(t, 1);

				int spellResistChance = CalculateSpellResistChance(t);
				int randNum = 0;
				bool UseRNGOverride = ServerProperties.Properties.OVERRIDE_DECK_RNG;
				if (spellResistChance > 0)
				{
					if (Caster is GamePlayer caster && !UseRNGOverride)
					{
						randNum = caster.RandomNumberDeck.GetInt();
					}
					else
					{
						randNum = Util.CryptoNextInt(100);
					}

					if (this.Caster is GamePlayer spellCaster && spellCaster.UseDetailedCombatLog)
					{
						spellCaster.Out.SendMessage(
							$"Target chance to resist: {spellResistChance} RandomNumber: {randNum}",
							eChatType.CT_DamageAdd, eChatLoc.CL_SystemWindow);
					}

					if (target is GamePlayer spellTarg && spellTarg.UseDetailedCombatLog)
					{
						spellTarg.Out.SendMessage($"Your chance to resist: {spellResistChance} RandomNumber: {randNum}",
							eChatType.CT_DamageAdd, eChatLoc.CL_SystemWindow);
					}

					if (spellResistChance > randNum)
					{
						OnSpellResisted(t);
						return;
					}
				}
                if (Spell.Radius == 0 || HasPositiveEffect)
				{
					ApplyEffectOnTarget(t, effectiveness);
				}
				else if (Spell.Target.ToLower() == "area")
				{
					int dist = t.GetDistanceTo(Caster.GroundTarget);
					if (dist >= 0)
						ApplyEffectOnTarget(t, (effectiveness - CalculateAreaVariance(t, dist, Spell.Radius)));
				}
				else if (Spell.Target.ToLower() == "cone")
				{
					int dist = t.GetDistanceTo(Caster);
					//Cone spells use the range for their variance!
					if (dist >= 0)
						ApplyEffectOnTarget(t, (effectiveness - CalculateAreaVariance(t, dist, Spell.Range)));
				}
				else
				{
					int dist = t.GetDistanceTo(target);
					if (dist >= 0)
						ApplyEffectOnTarget(t, (effectiveness - CalculateAreaVariance(t, dist, Spell.Radius)));
				}

				if (Caster is GamePet pet && Spell.IsBuff)
					pet.AddBuffedTarget(target);
			});

			if (Spell.Target.ToLower() == "ground")
			{
				ApplyEffectOnTarget(null, 1);
			}

			CastSubSpells(target);
			return true;
		}
		
		/// <summary>
		/// Calculate the variance due to the radius of the spell
		/// </summary>
		/// <param name="distance">The distance away from center of the spell</param>
		/// <param name="radius">The radius of the spell</param>
		/// <returns></returns>
		protected virtual double CalculateAreaVariance(GameLiving target, int distance, int radius)
		{
			return ((double)distance / (double)radius);
		}

		/// <summary>
		/// Calculates the effect duration in milliseconds
		/// </summary>
		/// <param name="target">The effect target</param>
		/// <param name="effectiveness">The effect effectiveness</param>
		/// <returns>The effect duration in milliseconds</returns>
		protected virtual int CalculateEffectDuration(GameLiving target, double effectiveness)
		{
			if (Spell.Duration == 0)
				return 0;
			
			double duration = Spell.Duration;
			duration *= (1.0 + m_caster.GetModified(eProperty.SpellDuration) * 0.01);
			if (Spell.InstrumentRequirement != 0)
			{
				InventoryItem instrument = Caster.AttackWeapon;
				if (instrument != null)
				{
					duration *= 1.0 + Math.Min(1.0, instrument.Level / (double)Caster.Level); // up to 200% duration for songs
					duration *= instrument.Condition / (double)instrument.MaxCondition * instrument.Quality / 100;
				}
			}

			duration *= effectiveness;
			if (duration < 1)
				duration = 1;
			else if (duration > (Spell.Duration * 4))
				duration = (Spell.Duration * 4);
			return (int)duration;
		}

		/// <summary>
		/// Creates the corresponding spell effect for the spell
		/// </summary>
		/// <param name="target"></param>
		/// <param name="effectiveness"></param>
		/// <returns></returns>
		protected virtual GameSpellEffect CreateSpellEffect(GameLiving target, double effectiveness)
		{
			int freq = Spell != null ? Spell.Frequency : 0;
			return new GameSpellEffect(this, CalculateEffectDuration(target, effectiveness), freq, effectiveness);
		}

		/// <summary>
		/// Apply effect on target or do spell action if non duration spell
		/// </summary>
		/// <param name="target">target that gets the effect</param>
		/// <param name="effectiveness">factor from 0..1 (0%-100%)</param>
		public virtual void ApplyEffectOnTarget(GameLiving target, double effectiveness)
		{
            

			/*
            if (target is GamePlayer)
			{
				GameSpellEffect effect1;
				effect1 = SpellHandler.FindEffectOnTarget(target, "Phaseshift");
				if ((effect1 != null && (Spell.SpellType != (byte)eSpellType.SpreadHeal || Spell.SpellType != (byte)eSpellType.Heal || Spell.SpellType != (byte)eSpellType.SpeedEnhancement)))
				{
					MessageToCaster(target.Name + " is Phaseshifted and can't be effected by this Spell!", eChatType.CT_SpellResisted);
					return;
				}
			}*/

			if ((target is Keeps.GameKeepDoor || target is Keeps.GameKeepComponent))
			{
				bool isAllowed = false;
				bool isSilent = false;

				if (Spell.Radius == 0)
				{
					switch (Spell.SpellType)
					{
						case (byte)eSpellType.Archery:
						case (byte)eSpellType.Bolt:
						case (byte)eSpellType.Bomber:
						case (byte)eSpellType.DamageSpeedDecrease:
						case (byte)eSpellType.DirectDamage:
						case (byte)eSpellType.MagicalStrike:
						case (byte)eSpellType.SiegeArrow:
						case (byte)eSpellType.Lifedrain:
						case (byte)eSpellType.SiegeDirectDamage:
						case (byte)eSpellType.SummonTheurgistPet:
						case (byte)eSpellType.DirectDamageWithDebuff:
							isAllowed = true;
							break;
					}
				}

				if (Spell.Radius > 0)
				{
					// pbaoe is allowed, otherwise door is in range of a AOE so don't spam caster with a message
					if (Spell.Range == 0)
						isAllowed = true;
					else
						isSilent = true;
				}

				if (!isAllowed)
				{
					if (!isSilent)
					{
						MessageToCaster(String.Format("Your spell has no effect on the {0}!", target.Name), eChatType.CT_SpellResisted);
					}

					return;
				}
			}
			
			if (Spell.Radius == 0 &&
				//(m_spellLine.KeyName == GlobalSpellsLines.Item_Effects ||
				(m_spellLine.KeyName == GlobalSpellsLines.Combat_Styles_Effect || 
				//m_spellLine.KeyName == GlobalSpellsLines.Potions_Effects || 
				m_spellLine.KeyName == Specs.Savagery || 
				m_spellLine.KeyName == GlobalSpellsLines.Character_Abilities || 
				m_spellLine.KeyName == "OffensiveProc"))
				effectiveness = 1.0; // TODO player.PlayerEffectiveness


			if (Spell.Radius == 0 &&
			    (m_spellLine.KeyName == GlobalSpellsLines.Potions_Effects ||
			    m_spellLine.KeyName == GlobalSpellsLines.Item_Effects)
			    && effectiveness < 1)
			{
				effectiveness = 1.0;
			}

			if (effectiveness <= 0)
				return; // no effect

            // Apply effect for Duration Spell.
            if ((Spell.Duration > 0 && Spell.Target.ToLower() != "area") || Spell.Concentration > 0)
			{
				OnDurationEffectApply(target, effectiveness);
			}
			else
			{
				OnDirectEffect(target, effectiveness);
			}
				
			if (!HasPositiveEffect)
			{
				SendEffectAnimation(target, 0, false, 1);
				// if(Spell.SpellType == (byte)eSpellType.Amnesia) return;
				AttackData ad = new AttackData();
				ad.Attacker = Caster;
				ad.Target = target;
				ad.AttackType = AttackData.eAttackType.Spell;
				ad.SpellHandler = this;
				ad.AttackResult = eAttackResult.HitUnstyled;
				ad.IsSpellResisted = false;
				ad.Damage = (int)Spell.Damage;
				ad.DamageType = Spell.DamageType;

				m_lastAttackData = ad;
				Caster.OnAttackEnemy(ad);

                // Treat non-damaging effects as attacks to trigger an immediate response and BAF
                if (ad.Damage == 0 && ad.Target is GameNPC)
				{
					IOldAggressiveBrain aggroBrain = ((GameNPC)ad.Target).Brain as IOldAggressiveBrain;
					if (aggroBrain != null)
						aggroBrain.AddToAggroList(Caster, 1);
				}

				// Harmful spells that deal no damage (ie. debuffs) should still trigger OnAttackedByEnemy.
				// Exception for DoTs here since the initial landing of the DoT spell reports 0 damage
				// and the first tick damage is done by the pulsing effect, which takes care of firing OnAttackedByEnemy.
				if (ad.Damage == 0 && ad.SpellHandler.Spell.SpellType != (byte)eSpellType.DamageOverTime)
                {
					target.OnAttackedByEnemy(ad);
				}
			}
		}

		/// <summary>
		/// Called when cast sequence is complete
		/// </summary>
		public virtual void OnAfterSpellCastSequence()
		{
			if (CastingCompleteEvent != null)
			{
				CastingCompleteEvent(this);
			}
		}

		/// <summary>
		/// Determines wether this spell is better than given one
		/// </summary>
		/// <param name="oldeffect"></param>
		/// <param name="neweffect"></param>
		/// <returns>true if this spell is better version than compare spell</returns>
		public virtual bool IsNewEffectBetter(GameSpellEffect oldeffect, GameSpellEffect neweffect)
		{
			Spell oldspell = oldeffect.Spell;
			Spell newspell = neweffect.Spell;
//			if (oldspell.SpellType != newspell.SpellType)
//			{
//				if (Log.IsWarnEnabled)
//					Log.Warn("Spell effect compare with different types " + oldspell.SpellType + " <=> " + newspell.SpellType + "\n" + Environment.StackTrace);
//				return false;
//			}
			if (oldspell.IsConcentration)
				return false;
			if (newspell.Damage < oldspell.Damage)
				return false;
			if (newspell.Value < oldspell.Value)
				return false;
			//makes problems for immunity effects
			if (!oldeffect.ImmunityState && !newspell.IsConcentration)
			{
				if (neweffect.Duration <= oldeffect.RemainingTime)
					return false;
			}
			return true;
		}

		/// <summary>
		/// Determines wether this spell is compatible with given spell
		/// and therefore overwritable by better versions
		/// spells that are overwritable cannot stack
		/// </summary>
		/// <param name="compare"></param>
		/// <returns></returns>
		public virtual bool IsOverwritable(GameSpellEffect compare)
		{
			if (Spell.EffectGroup != 0 || compare.Spell.EffectGroup != 0)
				return Spell.EffectGroup == compare.Spell.EffectGroup;
			if (compare.Spell.SpellType != Spell.SpellType)
				return false;
			return true;
		}
		public virtual bool IsOverwritable(ECSGameSpellEffect compare)
		{
			if (Spell.EffectGroup != 0 || compare.SpellHandler.Spell.EffectGroup != 0)
				return Spell.EffectGroup == compare.SpellHandler.Spell.EffectGroup;
			if (compare.SpellHandler.Spell.SpellType != Spell.SpellType)
				return false;
			return true;
		}

		/// <summary>
		/// Determines wether this spell can be disabled
		/// by better versions spells that stacks without overwriting
		/// </summary>
		/// <param name="compare"></param>
		/// <returns></returns>
		public virtual bool IsCancellable(GameSpellEffect compare)
		{
			if (compare.SpellHandler != null)
			{
				if ((compare.SpellHandler.AllowCoexisting || AllowCoexisting)
				    && (!compare.SpellHandler.SpellLine.KeyName.Equals(SpellLine.KeyName, StringComparison.OrdinalIgnoreCase)
				        || compare.SpellHandler.Spell.IsInstantCast != Spell.IsInstantCast))
					return true;
			}
			return false;
		}

		/// <summary>
		/// Determines wether new spell is better than old spell and should disable it
		/// </summary>
		/// <param name="oldeffect"></param>
		/// <param name="neweffect"></param>
		/// <returns></returns>
		public virtual bool IsCancellableEffectBetter(GameSpellEffect oldeffect, GameSpellEffect neweffect)
		{
			if (neweffect.SpellHandler.Spell.Value >= oldeffect.SpellHandler.Spell.Value)
				return true;
			
			return false;
		}
		
		/// <summary>
		/// Execute Duration Spell Effect on Target
		/// </summary>
		/// <param name="target"></param>
		/// <param name="effectiveness"></param>
		public virtual void OnDurationEffectApply(GameLiving target, double effectiveness)
		{
			if (!target.IsAlive || target.effectListComponent == null)
			{
			return;
			}

			// eChatType noOverwrite = (Spell.Pulse == 0) ? eChatType.CT_SpellResisted : eChatType.CT_SpellPulse;

			CreateECSEffect(new ECSGameEffectInitParams(target, CalculateEffectDuration(target, effectiveness), effectiveness, this));
            
            // GameSpellEffect neweffect = CreateSpellEffect(target, effectiveness);
            //
            // // Iterate through Overwritable Effect
            // var overwritenEffects = target.EffectList.OfType<GameSpellEffect>().Where(effect => effect.SpellHandler != null && effect.SpellHandler.IsOverwritable(neweffect));
            //
            // // Store Overwritable or Cancellable
            // var enable = true;
            // var cancellableEffects = new List<GameSpellEffect>(1);
            // GameSpellEffect overwriteEffect = null;
            //
            // foreach (var ovEffect in overwritenEffects)
            // {					
            // 	// If we can cancel spell effect we don't need to overwrite it
            // 	if (ovEffect.SpellHandler.IsCancellable(neweffect))
            // 	{
            // 		// Spell is better than existing "Cancellable" or it should start disabled
            // 		if (IsCancellableEffectBetter(ovEffect, neweffect))
            // 			cancellableEffects.Add(ovEffect);
            // 		else
            // 			enable = false;
            // 	}
            // 	else
            // 	{
            // 		// Check for Overwriting.
            // 		if (IsNewEffectBetter(ovEffect, neweffect))
            // 		{
            // 			// New Spell is overwriting this one.
            // 			overwriteEffect = ovEffect;
            // 		}
            // 		else
            // 		{
            // 			// Old Spell is Better than new one
            // 			SendSpellResistAnimation(target);
            // 			if (target == Caster)
            // 			{
            // 				if (ovEffect.ImmunityState)
            // 					MessageToCaster("You can't have that effect again yet!", noOverwrite);
            // 				else
            // 					MessageToCaster("You already have that effect. Wait until it expires. Spell failed.", noOverwrite);
            // 			}
            // 			else
            // 			{
            // 				if (ovEffect.ImmunityState)
            // 				{
            // 					this.MessageToCaster(noOverwrite, "{0} can't have that effect again yet!", ovEffect.Owner != null ? ovEffect.Owner.GetName(0, true) : "(null)");
            // 				}
            // 				else
            // 				{
            // 					this.MessageToCaster(noOverwrite, "{0} already has that effect.", target.GetName(0, true));
            // 					MessageToCaster("Wait until it expires. Spell Failed.", noOverwrite);
            // 				}
            // 			}
            // 			// Prevent Adding.
            // 			return;
            // 		}
            // 	}
            // }
            //
            // // Register Effect list Changes
            // target.EffectList.BeginChanges();
            // try
            // {
            // 	// Check for disabled effect
            // 	foreach (var disableEffect in cancellableEffects)
            // 		disableEffect.DisableEffect(false);
            // 	
            // 	if (overwriteEffect != null)
            // 	{
            // 		if (enable)
            // 			overwriteEffect.Overwrite(neweffect);
            // 		else
            // 			overwriteEffect.OverwriteDisabled(neweffect);
            // 	}
            // 	else
            // 	{
            // 		if (enable)
            // 		neweffect.Start(target);
            // 		else
            // 			neweffect.StartDisabled(target);
            // 	}
            // }
            // finally
            // {
            // 	target.EffectList.CommitChanges();
            // }
        }
		
		/// <summary>
		/// Called when Effect is Added to target Effect List
		/// </summary>
		/// <param name="effect"></param>
		public virtual void OnEffectAdd(GameSpellEffect effect)
		{
		}
		
		/// <summary>
		/// Check for Spell Effect Removed to Enable Best Cancellable
		/// </summary>
		/// <param name="effect"></param>
		/// <param name="overwrite"></param>
		public virtual void OnEffectRemove(GameSpellEffect effect, bool overwrite)
		{
			if (!overwrite)
			{
				if (Spell.IsFocus)
				{
					FocusSpellAction(/*null, Caster, null*/);
				}
				// Re-Enable Cancellable Effects.
				var enableEffect = effect.Owner.EffectList.OfType<GameSpellEffect>()
					.Where(eff => eff != effect && eff.SpellHandler != null && eff.SpellHandler.IsOverwritable(effect) && eff.SpellHandler.IsCancellable(effect));
				
				// Find Best Remaining Effect
				GameSpellEffect best = null;
				foreach (var eff in enableEffect)
				{
					if (best == null)
						best = eff;
					else if (best.SpellHandler.IsCancellableEffectBetter(best, eff))
						best = eff;
				}
				
				if (best != null)
				{
					effect.Owner.EffectList.BeginChanges();
					try
					{
						// Enable Best Effect
						best.EnableEffect();						
					}
					finally
					{
						effect.Owner.EffectList.CommitChanges();
					}
				}
			}
		}
		
		/// <summary>
		/// execute non duration spell effect on target
		/// </summary>
		/// <param name="target"></param>
		/// <param name="effectiveness"></param>
		public virtual void OnDirectEffect(GameLiving target, double effectiveness)
		{ }

		/// <summary>
		/// When an applied effect starts
		/// duration spells only
		/// </summary>
		/// <param name="effect"></param>
		public virtual void OnEffectStart(GameSpellEffect effect)
		{
			if (Spell.Pulse == 0)
				SendEffectAnimation(effect.Owner, 0, false, 1);
			if (Spell.IsFocus) // Add Event handlers for focus spell
			{
				//GameEventMgr.RemoveHandler(Caster, GameLivingEvent.AttackFinished, new DOLEventHandler(FocusSpellAction));
				//GameEventMgr.RemoveHandler(Caster, GameLivingEvent.CastStarting, new DOLEventHandler(FocusSpellAction));
				//GameEventMgr.RemoveHandler(Caster, GameLivingEvent.Moving, new DOLEventHandler(FocusSpellAction));
				//GameEventMgr.RemoveHandler(Caster, GameLivingEvent.Dying, new DOLEventHandler(FocusSpellAction));
				//GameEventMgr.RemoveHandler(Caster, GameLivingEvent.AttackedByEnemy, new DOLEventHandler(FocusSpellAction));
				//GameEventMgr.RemoveHandler(effect.Owner, GameLivingEvent.Dying, new DOLEventHandler(FocusSpellAction));
				Caster.TempProperties.setProperty(FOCUS_SPELL, effect);
				//GameEventMgr.AddHandler(Caster, GameLivingEvent.AttackFinished, new DOLEventHandler(FocusSpellAction));
				//GameEventMgr.AddHandler(Caster, GameLivingEvent.CastStarting, new DOLEventHandler(FocusSpellAction));
				//GameEventMgr.AddHandler(Caster, GameLivingEvent.Moving, new DOLEventHandler(FocusSpellAction));
				//GameEventMgr.AddHandler(Caster, GameLivingEvent.Dying, new DOLEventHandler(FocusSpellAction));
				//GameEventMgr.AddHandler(Caster, GameLivingEvent.AttackedByEnemy, new DOLEventHandler(FocusSpellAction));
				//GameEventMgr.AddHandler(effect.Owner, GameLivingEvent.Dying, new DOLEventHandler(FocusSpellAction));
			}
		}

		/// <summary>
		/// When an applied effect pulses
		/// duration spells only
		/// </summary>
		/// <param name="effect"></param>
		public virtual void OnEffectPulse(GameSpellEffect effect)
		{
			if (effect.Owner.IsAlive == false)
			{
				effect.Cancel(false);
			}
		}

		/// <summary>
		/// When an applied effect expires.
		/// Duration spells only.
		/// </summary>
		/// <param name="effect">The expired effect</param>
		/// <param name="noMessages">true, when no messages should be sent to player and surrounding</param>
		/// <returns>immunity duration in milliseconds</returns>
		public virtual int OnEffectExpires(GameSpellEffect effect, bool noMessages)
		{
			return 0;
		}

		/// <summary>
		/// Calculates chance of spell getting resisted
		/// </summary>
		/// <param name="target">the target of the spell</param>
		/// <returns>chance that spell will be resisted for specific target</returns>
		public virtual int CalculateSpellResistChance(GameLiving target)
		{
			if (HasPositiveEffect)
			{
				return 0;
			}

			if (m_spellLine.KeyName == GlobalSpellsLines.Item_Effects && m_spellItem != null)
			{
				GamePlayer playerCaster = Caster as GamePlayer;
				if (playerCaster != null)
				{
					int itemSpellLevel = m_spellItem.Template.LevelRequirement > 0 ? m_spellItem.Template.LevelRequirement : Math.Min(playerCaster.MaxLevel, m_spellItem.Level);
					return 100 - (85 + ((itemSpellLevel - target.Level) / 2));
				}
			}

			return 100 - CalculateToHitChance(target);
		}

		/// <summary>
		/// When spell was resisted
		/// </summary>
		/// <param name="target">the target that resisted the spell</param>
		protected virtual void OnSpellResisted(GameLiving target)
		{
			SendSpellResistAnimation(target);
			SendSpellResistMessages(target);
			SendSpellResistNotification(target);
			StartSpellResistInterruptTimer(target);
			StartSpellResistLastAttackTimer(target);
			// Treat resists as attacks to trigger an immediate response and BAF
			if (target is GameNPC)
			{
				IOldAggressiveBrain aggroBrain = ((GameNPC)target).Brain as IOldAggressiveBrain;
				if (aggroBrain != null)
					aggroBrain.AddToAggroList(Caster, 1);

				if (Caster.Realm == 0 || target.Realm == 0)
				{
					target.LastAttackedByEnemyTickPvE = GameLoop.GameLoopTime;
					Caster.LastAttackTickPvE = GameLoop.GameLoopTime;
				}
				else
				{
					target.LastAttackedByEnemyTickPvP = GameLoop.GameLoopTime;
					Caster.LastAttackTickPvP = GameLoop.GameLoopTime;
				}
			}
		}

		/// <summary>
		/// Send Spell Resisted Animation
		/// </summary>
		/// <param name="target"></param>
		public virtual void SendSpellResistAnimation(GameLiving target)
		{
			if (Spell.Pulse == 0 || !HasPositiveEffect)
				SendEffectAnimation(target, 0, false, 0);
		}
		
		/// <summary>
		/// Send Spell Resist Messages to Caster and Target
		/// </summary>
		/// <param name="target"></param>
		public virtual void SendSpellResistMessages(GameLiving target)
		{
			// Deliver message to the target, if the target is a pet, to its
			// owner instead.
			if (target is GameNPC)
			{
				IControlledBrain brain = ((GameNPC)target).Brain as IControlledBrain;
				if (brain != null)
				{
					GamePlayer owner = brain.GetPlayerOwner();
					if (owner != null)
					{
						this.MessageToLiving(owner, eChatType.CT_SpellResisted, "Your {0} resists the effect!", target.Name);
					}
				}
			}
			else
			{
				MessageToLiving(target, "You resist the effect!", eChatType.CT_SpellResisted);
			}

			// Deliver message to the caster as well.
			this.MessageToCaster(eChatType.CT_SpellResisted, "{0} resists the effect!" + " (" + CalculateSpellResistChance(target).ToString("0.0") + "%)", target.GetName(0, true));
		}
		
		/// <summary>
		/// Send Spell Attack Data Notification to Target when Spell is Resisted
		/// </summary>
		/// <param name="target"></param>
		public virtual void SendSpellResistNotification(GameLiving target)
		{
			// Report resisted spell attack data to any type of living object, no need
			// to decide here what to do. For example, NPCs will use their brain.
			// "Just the facts, ma'am, just the facts."
			AttackData ad = new AttackData();
			ad.Attacker = Caster;
			ad.Target = target;
			ad.AttackType = AttackData.eAttackType.Spell;
			ad.SpellHandler = this;
			ad.AttackResult = eAttackResult.Missed;
			ad.IsSpellResisted = true;
			target.OnAttackedByEnemy(ad);
			Caster.OnAttackEnemy(ad);
		}
		
		/// <summary>
		/// Start Spell Interrupt Timer when Spell is Resisted
		/// </summary>
		/// <param name="target"></param>
		public virtual void StartSpellResistInterruptTimer(GameLiving target)
		{
			// Spells that would have caused damage or are not instant will still
			// interrupt a casting player.
			if(!(Spell.SpellType.ToString().IndexOf("debuff", StringComparison.OrdinalIgnoreCase) >= 0 && Spell.CastTime == 0))
				target.StartInterruptTimer(target.SpellInterruptDuration, AttackData.eAttackType.Spell, Caster);			
		}
		
		/// <summary>
		/// Start Last Attack Timer when Spell is Resisted
		/// </summary>
		/// <param name="target"></param>
		public virtual void StartSpellResistLastAttackTimer(GameLiving target)
		{
			if (target.Realm == 0 || Caster.Realm == 0)
			{
				target.LastAttackedByEnemyTickPvE = GameLoop.GameLoopTime;
				Caster.LastAttackTickPvE = GameLoop.GameLoopTime;
			}
			else
			{
				target.LastAttackedByEnemyTickPvP = GameLoop.GameLoopTime;
				Caster.LastAttackTickPvP = GameLoop.GameLoopTime;
			}
		}
		
		#region messages

		/// <summary>
		/// Sends a message to the caster, if the caster is a controlled
		/// creature, to the player instead (only spell hit and resisted
		/// messages).
		/// </summary>
		/// <param name="message"></param>
		/// <param name="type"></param>
		public void MessageToCaster(string message, eChatType type)
		{
			if (Caster is GamePlayer)
			{
				(Caster as GamePlayer).MessageToSelf(message, type);
			}
			else if (Caster is GameNPC && (Caster as GameNPC).Brain is IControlledBrain
			         && (type == eChatType.CT_YouHit || type == eChatType.CT_SpellResisted || type == eChatType.CT_Spell))
			{
				GamePlayer owner = ((Caster as GameNPC).Brain as IControlledBrain).GetPlayerOwner();
				if (owner != null)
				{
					owner.MessageFromControlled(message, type);
				}
			}
		}

		/// <summary>
		/// sends a message to a living
		/// </summary>
		/// <param name="living"></param>
		/// <param name="message"></param>
		/// <param name="type"></param>
		public void MessageToLiving(GameLiving living, string message, eChatType type)
		{
			if (message != null && message.Length > 0)
			{
				living.MessageToSelf(message, type);
			}
		}

        /// <summary>
        /// Hold events for focus spells
        /// </summary>
        /// <param name="e"></param>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        //protected virtual void FocusSpellAction(DOLEvent e, object sender, EventArgs args)
        //{
        //	GameLiving living = sender as GameLiving;
        //	if (living == null) return;

        //	GameSpellEffect currentEffect = (GameSpellEffect)living.TempProperties.getProperty<object>(FOCUS_SPELL, null);
        //	if (currentEffect == null)
        //		return;

        //	//GameEventMgr.RemoveHandler(Caster, GameLivingEvent.AttackFinished, new DOLEventHandler(FocusSpellAction));
        //	//GameEventMgr.RemoveHandler(Caster, GameLivingEvent.CastStarting, new DOLEventHandler(FocusSpellAction));
        //	//GameEventMgr.RemoveHandler(Caster, GameLivingEvent.Moving, new DOLEventHandler(FocusSpellAction));
        //	//GameEventMgr.RemoveHandler(Caster, GameLivingEvent.Dying, new DOLEventHandler(FocusSpellAction));
        //	//GameEventMgr.RemoveHandler(Caster, GameLivingEvent.AttackedByEnemy, new DOLEventHandler(FocusSpellAction));
        //	//GameEventMgr.RemoveHandler(currentEffect.Owner, GameLivingEvent.Dying, new DOLEventHandler(FocusSpellAction));
        //	Caster.TempProperties.removeProperty(FOCUS_SPELL);

        //	CancelPulsingSpell(Caster, currentEffect.Spell.SpellType);
        //	currentEffect.Cancel(false);

        //	MessageToCaster(String.Format("You lose your focus on your {0} spell.", currentEffect.Spell.Name), eChatType.CT_SpellExpires);

        //	if (e == GameLivingEvent.Moving)
        //		MessageToCaster("You move and interrupt your focus!", eChatType.CT_Important);
        //}
        public virtual void FocusSpellAction(bool moving = false)
        {
            //GameLiving living = sender as GameLiving;
            //if (living == null) return;

            //GameSpellEffect currentEffect = (GameSpellEffect)Caster.TempProperties.getProperty<object>(FOCUS_SPELL, null);
            //if (currentEffect == null)
            //    return;

            //GameEventMgr.RemoveHandler(Caster, GameLivingEvent.AttackFinished, new DOLEventHandler(FocusSpellAction));
            //GameEventMgr.RemoveHandler(Caster, GameLivingEvent.CastStarting, new DOLEventHandler(FocusSpellAction));
            //GameEventMgr.RemoveHandler(Caster, GameLivingEvent.Moving, new DOLEventHandler(FocusSpellAction));
            //GameEventMgr.RemoveHandler(Caster, GameLivingEvent.Dying, new DOLEventHandler(FocusSpellAction));
            //GameEventMgr.RemoveHandler(Caster, GameLivingEvent.AttackedByEnemy, new DOLEventHandler(FocusSpellAction));
            //GameEventMgr.RemoveHandler(currentEffect.Owner, GameLivingEvent.Dying, new DOLEventHandler(FocusSpellAction));
            castState = eCastState.Cleanup;
            Caster.TempProperties.removeProperty(FOCUS_SPELL);
            Caster.LastPulseCast = null;
			
			if (this is DamageShieldSpellHandler)
            {
				ECSGameSpellEffect dmgShield = EffectListService.GetSpellEffectOnTarget(Caster?.ControlledBrain?.Body, eEffect.FocusShield);
				//verify the effect is a focus shield and not a timer based damage shield
				if (dmgShield is not null)
                {
					if (dmgShield != null && dmgShield.SpellHandler.Spell.IsFocus)
						EffectService.RequestImmediateCancelEffect(dmgShield);
				}					
            }
            
            //CancelPulsingSpell(Caster, currentEffect.Spell.SpellType);
            //currentEffect.Cancel(false);

            MessageToCaster(String.Format("You lose your focus on your {0} spell.", /*currentEffect.*/Spell.Name), eChatType.CT_SpellExpires);

            //if (e == GameLivingEvent.Moving)
            if (moving)
                MessageToCaster("You move and interrupt your focus!", eChatType.CT_Important);
        }
        #endregion

        /// <summary>
        /// Ability to cast a spell
        /// </summary>
        public ISpellCastingAbilityHandler Ability
		{
			get { return m_ability; }
			set { m_ability = value; }
		}
		/// <summary>
		/// The Spell
		/// </summary>
		public Spell Spell
		{
			get { return m_spell; }
		}

		/// <summary>
		/// The Spell Line
		/// </summary>
		public SpellLine SpellLine
		{
			get { return m_spellLine; }
		}

		/// <summary>
		/// The Caster
		/// </summary>
		public GameLiving Caster
		{
			get { return m_caster; }
		}

		/// <summary>
		/// Is the spell being cast?
		/// </summary>
		public bool IsCasting
		{
			get { return castState == eCastState.Casting; }//return m_castTimer != null && m_castTimer.IsAlive; }
		}

		/// <summary>
		/// Does the spell have a positive effect?
		/// </summary>
		public virtual bool HasPositiveEffect
		{
			get { return m_spell.IsHelpful; }
		}

		/// <summary>
		/// Is this Spell purgeable
		/// </summary>
		public virtual bool IsUnPurgeAble
		{
			get { return false; }
		}

		/// <summary>
		/// Current depth of delve info
		/// </summary>
		public byte DelveInfoDepth
		{
			get { return m_delveInfoDepth; }
			set { m_delveInfoDepth = value; }
		}

		/// <summary>
		/// Delve Info
		/// </summary>
		public virtual IList<string> DelveInfo
		{
			get
			{
				var list = new List<string>(32);
				//list.Add("Function: " + (Spell.SpellType == "" ? "(not implemented)" : Spell.SpellType));
				//list.Add(" "); //empty line
				GamePlayer p = null;

				if (Caster is GamePlayer || Caster is GameNPC && (Caster as GameNPC).Brain is IControlledBrain &&
				((Caster as GameNPC).Brain as IControlledBrain).GetPlayerOwner() != null)
				{
					p = Caster is GamePlayer ? (Caster as GamePlayer) : ((Caster as GameNPC).Brain as IControlledBrain).GetPlayerOwner();
				}
				list.Add(Spell.Description);
				list.Add(" "); //empty line
				if (Spell.InstrumentRequirement != 0)
					list.Add(p != null ? LanguageMgr.GetTranslation(p.Client, "DelveInfo.InstrumentRequire", GlobalConstants.InstrumentTypeToName(Spell.InstrumentRequirement)) : LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.InstrumentRequire", GlobalConstants.InstrumentTypeToName(Spell.InstrumentRequirement)));
				if (Spell.Damage != 0)
					list.Add(p != null ? LanguageMgr.GetTranslation(p.Client, "DelveInfo.Damage", Spell.Damage.ToString("0.###;0.###'%'")) : LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.Damage", Spell.Damage.ToString("0.###;0.###'%'")));
				if (Spell.LifeDrainReturn != 0)
					list.Add(p != null ? LanguageMgr.GetTranslation(p.Client, "DelveInfo.HealthReturned", Spell.LifeDrainReturn) : LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.HealthReturned", Spell.LifeDrainReturn));
				else if (Spell.Value != 0)
					list.Add(p != null ? LanguageMgr.GetTranslation(p.Client, "DelveInfo.Value", Spell.Value.ToString("0.###;0.###'%'")) : LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.Value", Spell.Value.ToString("0.###;0.###'%'")));
				list.Add(p != null ? LanguageMgr.GetTranslation(p.Client, "DelveInfo.Target", Spell.Target) : LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.Target", Spell.Target));
				if (Spell.Range != 0)
					list.Add(p != null ? LanguageMgr.GetTranslation(p.Client, "DelveInfo.Range", Spell.Range) : LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.Range", Spell.Range));
				if (Spell.Duration >= ushort.MaxValue * 1000)
					list.Add(p != null ? LanguageMgr.GetTranslation(p.Client, "DelveInfo.Duration") + " Permanent." : LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.Duration") + " Permanent.");
				else if (Spell.Duration > 60000)
					list.Add(p != null ? LanguageMgr.GetTranslation(p.Client, "DelveInfo.Duration") + " " + Spell.Duration / 60000 + ":" + (Spell.Duration % 60000 / 1000).ToString("00") + " min" : LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.Duration") + " " + Spell.Duration / 60000 + ":" + (Spell.Duration % 60000 / 1000).ToString("00") + " min");
				else if (Spell.Duration != 0)
					list.Add(p != null ? LanguageMgr.GetTranslation(p.Client, "DelveInfo.Duration") + " " + (Spell.Duration / 1000).ToString("0' sec';'Permanent.';'Permanent.'") : LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.Duration") + " " + (Spell.Duration / 1000).ToString("0' sec';'Permanent.';'Permanent.'"));
				if (Spell.Frequency != 0)
					list.Add(p != null ? LanguageMgr.GetTranslation(p.Client, "DelveInfo.Frequency", (Spell.Frequency * 0.001).ToString("0.0")) : LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.Frequency", (Spell.Frequency * 0.001).ToString("0.0")));
				if (Spell.Power != 0)
					list.Add(p != null ? LanguageMgr.GetTranslation(p.Client, "DelveInfo.PowerCost", Spell.Power.ToString("0;0'%'")) : LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.PowerCost", Spell.Power.ToString("0;0'%'")));
				list.Add(p != null ? LanguageMgr.GetTranslation(p.Client, "DelveInfo.CastingTime", (Spell.CastTime * 0.001).ToString("0.0## sec;-0.0## sec;'instant'")) : LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.CastingTime", (Spell.CastTime * 0.001).ToString("0.0## sec;-0.0## sec;'instant'")));
				if (Spell.RecastDelay > 60000)
					list.Add(p != null ? LanguageMgr.GetTranslation(p.Client, "DelveInfo.RecastTime") + " " + (Spell.RecastDelay / 60000).ToString() + ":" + (Spell.RecastDelay % 60000 / 1000).ToString("00") + " min" : LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.RecastTime") + " " + (Spell.RecastDelay / 60000).ToString() + ":" + (Spell.RecastDelay % 60000 / 1000).ToString("00") + " min");
				else if (Spell.RecastDelay > 0)
					list.Add(p != null ? LanguageMgr.GetTranslation(p.Client, "DelveInfo.RecastTime") + " " + (Spell.RecastDelay / 1000).ToString() + " sec" : LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.RecastTime") + " " + (Spell.RecastDelay / 1000).ToString() + " sec");
				if (Spell.Concentration != 0)
					list.Add(p != null ? LanguageMgr.GetTranslation(p.Client, "DelveInfo.ConcentrationCost", Spell.Concentration) : LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.ConcentrationCost", Spell.Concentration));
				if (Spell.Radius != 0)
					list.Add(p != null ? LanguageMgr.GetTranslation(p.Client, "DelveInfo.Radius", Spell.Radius) : LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.Radius", Spell.Radius));
				if (Spell.DamageType != eDamageType.Natural)
					list.Add(p != null ? LanguageMgr.GetTranslation(p.Client, "DelveInfo.Damage", GlobalConstants.DamageTypeToName(Spell.DamageType)) : LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.Damage", GlobalConstants.DamageTypeToName(Spell.DamageType)));
				if (Spell.IsFocus)
					list.Add(p != null ? LanguageMgr.GetTranslation(p.Client, "DelveInfo.Focus") : LanguageMgr.GetTranslation(ServerProperties.Properties.SERV_LANGUAGE, "DelveInfo.Focus"));

				return list;
			}
		}
		// warlock add
		public static GameSpellEffect FindEffectOnTarget(GameLiving target, string spellType, string spellName)
		{
			lock (target.EffectList)
			{
				foreach (IGameEffect fx in target.EffectList)
				{
					if (!(fx is GameSpellEffect))
						continue;
					GameSpellEffect effect = (GameSpellEffect)fx;
					if (fx is GameSpellAndImmunityEffect && ((GameSpellAndImmunityEffect)fx).ImmunityState)
						continue; // ignore immunity effects

					if (effect.SpellHandler.Spell != null && (effect.SpellHandler.Spell.SpellType.ToString() == spellType) && (effect.SpellHandler.Spell.Name == spellName))
					{
						return effect;
					}
				}
			}
			return null;
		}
		/// <summary>
		/// Find effect by spell type
		/// </summary>
		/// <param name="target"></param>
		/// <param name="spellType"></param>
		/// <returns>first occurance of effect in target's effect list or null</returns>
		public static GameSpellEffect FindEffectOnTarget(GameLiving target, string spellType)
		{
			if (target == null)
				return null;

			lock (target.EffectList)
			{
				foreach (IGameEffect fx in target.EffectList)
				{
					if (!(fx is GameSpellEffect))
						continue;
					GameSpellEffect effect = (GameSpellEffect)fx;
					if (fx is GameSpellAndImmunityEffect && ((GameSpellAndImmunityEffect)fx).ImmunityState)
						continue; // ignore immunity effects
					if (effect.SpellHandler.Spell != null && (effect.SpellHandler.Spell.SpellType.ToString() == spellType))
					{
						return effect;
					}
				}
			}
			return null;
		}

		/// <summary>
		/// Find effect by spell handler
		/// </summary>
		/// <param name="target"></param>
		/// <param name="spellHandler"></param>
		/// <returns>first occurance of effect in target's effect list or null</returns>
		public static GameSpellEffect FindEffectOnTarget(GameLiving target, ISpellHandler spellHandler)
		{
			lock (target.EffectList)
			{
				foreach (IGameEffect effect in target.EffectList)
				{
					GameSpellEffect gsp = effect as GameSpellEffect;
					if (gsp == null)
						continue;
					if (gsp.SpellHandler != spellHandler)
						continue;
					if (gsp is GameSpellAndImmunityEffect && ((GameSpellAndImmunityEffect)gsp).ImmunityState)
						continue; // ignore immunity effects
					return gsp;
				}
			}
			return null;
		}

		/// <summary>
		/// Find effect by spell handler
		/// </summary>
		/// <param name="target"></param>
		/// <param name="spellHandler"></param>
		/// <returns>first occurance of effect in target's effect list or null</returns>
		public static GameSpellEffect FindEffectOnTarget(GameLiving target, Type spellHandler)
		{
			if (spellHandler.IsInstanceOfType(typeof(SpellHandler)) == false)
				return null;

			lock (target.EffectList)
			{
				foreach (IGameEffect effect in target.EffectList)
				{
					GameSpellEffect gsp = effect as GameSpellEffect;
					if (gsp == null)
						continue;
					if (gsp.SpellHandler.GetType().IsInstanceOfType(spellHandler) == false)
						continue;
					if (gsp is GameSpellAndImmunityEffect && ((GameSpellAndImmunityEffect)gsp).ImmunityState)
						continue; // ignore immunity effects
					return gsp;
				}
			}
			return null;
		}

		/// <summary>
		/// Returns true if the target has the given static effect, false
		/// otherwise.
		/// </summary>
		/// <param name="target"></param>
		/// <param name="effectType"></param>
		/// <returns></returns>
		public static IGameEffect FindStaticEffectOnTarget(GameLiving target, Type effectType)
		{
			if (target == null)
				return null;

			lock (target.EffectList)
			{
				foreach (IGameEffect effect in target.EffectList)
					if (effect.GetType() == effectType)
						return effect;
			}
			return null;
		}

		/// <summary>
		/// Find pulsing spell by spell handler
		/// </summary>
		/// <param name="living"></param>
		/// <param name="handler"></param>
		/// <returns>first occurance of spellhandler in targets' conc list or null</returns>
		public static PulsingSpellEffect FindPulsingSpellOnTarget(GameLiving living, ISpellHandler handler)
		{
			lock (living.effectListComponent._concentrationEffectsLock)
			{
				foreach (IConcentrationEffect concEffect in living.effectListComponent.ConcentrationEffects)
				{
					PulsingSpellEffect pulsingSpell = concEffect as PulsingSpellEffect;
					if (pulsingSpell == null) continue;
					if (pulsingSpell.SpellHandler == handler)
						return pulsingSpell;
				}
				return null;
			}
		}

		#region various helpers

		/// <summary>
		/// Level mod for effect between target and caster if there is any
		/// </summary>
		/// <returns></returns>
		public virtual double GetLevelModFactor()
		{
			return 0.02;  // Live testing done Summer 2009 by Bluraven, Tolakram  Levels 40, 45, 50, 55, 60, 65, 70
		}

		/// <summary>
		/// Calculates min damage variance %
		/// </summary>
		/// <param name="target">spell target</param>
		/// <param name="min">returns min variance</param>
		/// <param name="max">returns max variance</param>
		public virtual void CalculateDamageVariance(GameLiving target, out double min, out double max)
		{
			if (m_spellLine.KeyName == GlobalSpellsLines.Item_Effects)
			{
				min = .75;
				max = 1.0;
				return;
			}

			if (m_spellLine.KeyName == GlobalSpellsLines.Combat_Styles_Effect)
			{
				if (UseMinVariance)
				{
					min = 1.0;
				}
				else
				{
					min = .75;
				}

				max = 1.0;

				return;
			}

			if (m_spellLine.KeyName == GlobalSpellsLines.Reserved_Spells)
			{
				min = max = 1.0;
				return;
			}

			if (m_spellLine.KeyName == GlobalSpellsLines.Mob_Spells)
			{
				min = .75;
				max = 1.0;
				return;
			}

			int speclevel = 1;

			if (m_caster is GamePet)
			{
				IControlledBrain brain = (m_caster as GameNPC).Brain as IControlledBrain;
				speclevel = brain.GetLivingOwner().Level;
			}
			else if (m_caster is GamePlayer)
			{
				speclevel = ((GamePlayer)m_caster).GetModifiedSpecLevel(m_spellLine.Spec);
			}
			
			/*
			 * June 21st 2022 - Fen: Removing a lot of DoL code that should not be here for 1.65 calculations.
			 *
			 * Vanesyra lays out variance calculations here: https://www.ignboards.com/threads/melee-speed-melee-and-style-damage-or-why-pure-grothrates-are-wrong.452406879/page-3
			 * Most importantly, variance should be .25 at its lowest, 1.0 at its max, and never exceed 1.0.
			 *
			 * Base DoL calculations were adding an extra 10-30% damage above 1.0, which has now been removed.
			 */
			min = .2;
			max = 1;
			
			if (target.Level > 0)
			{
				var varianceMod = (speclevel - 1) / (double) target.Level;
				if (varianceMod > 1) varianceMod = 1;
				min = varianceMod;
			}
			/*
			if (speclevel - 1 > target.Level)
			{
				double overspecBonus = (speclevel - 1 - target.Level) * 0.005;
				min += overspecBonus;
				max += overspecBonus;
				Console.WriteLine($"overspec bonus {overspecBonus}");
			}*/
			
			// add level mod
			if (m_caster is GamePlayer)
			{
				min += GetLevelModFactor() * (m_caster.Level - target.Level);
				max += GetLevelModFactor() * (m_caster.Level - target.Level);
			}
			else if (m_caster is GameNPC && ((GameNPC)m_caster).Brain is IControlledBrain)
			{
				//Get the root owner
				GameLiving owner = ((IControlledBrain)((GameNPC)m_caster).Brain).GetLivingOwner();
				if (owner != null)
				{
					min += GetLevelModFactor() * (owner.Level - target.Level);
					max += GetLevelModFactor() * (owner.Level - target.Level);
				}
			}

			if (max < 0.25)
				max = 0.25;
			if (min > max)
				min = max;
			if (min < .2)
				min = .2;
		}

		/// <summary>
		/// Player pet damage cap
		/// This simulates a player casting a baseline nuke with the capped damage near (but not exactly) that of the equivilent spell of the players level.
		/// This cap is not applied if the player is level 50
		/// </summary>
		/// <param name="player"></param>
		/// <returns></returns>
		public virtual double CapPetSpellDamage(double damage, GamePlayer player)
		{
			double cappedDamage = damage;

			if (player.Level < 13)
			{
				cappedDamage = 4.1 * player.Level;
			}

			if (player.Level < 50)
			{
				cappedDamage = 3.8 * player.Level;
			}

			return Math.Min(damage, cappedDamage);
		}


		/// <summary>
		/// Put a calculated cap on NPC damage to solve a problem where an npc is given a high level spell but needs damage
		/// capped to the npc level.  This uses player spec nukes to calculate damage cap.
		/// NPC's level 50 and above are not capped
		/// </summary>
		/// <param name="damage"></param>
		/// <param name="player"></param>
		/// <returns></returns>
		public virtual double CapNPCSpellDamage(double damage, GameNPC npc)
		{
			if (npc.Level < 50)
			{
				return Math.Min(damage, 4.7 * npc.Level);
			}

			return damage;
		}

		/// <summary>
		/// Calculates the base 100% spell damage which is then modified by damage variance factors
		/// </summary>
		/// <returns></returns>
		public virtual double CalculateDamageBase(GameLiving target)
		{
			double spellDamage = Spell.Damage;
			GamePlayer player = Caster as GamePlayer;

			if (Spell.SpellType == (byte)eSpellType.Lifedrain)
				spellDamage *= (1 + Spell.LifeDrainReturn * .001);

			// For pets the stats of the owner have to be taken into account.

			if (Caster is GameNPC && ((Caster as GameNPC).Brain) is IControlledBrain)
			{
				player = (((Caster as GameNPC).Brain) as IControlledBrain).Owner as GamePlayer;
			}

			if (player != null)
			{
				if (Caster is GamePet pet)
				{
					// There is no reason to cap pet spell damage if it's being scaled anyway.
					if (ServerProperties.Properties.PET_SCALE_SPELL_MAX_LEVEL <= 0)
						spellDamage = CapPetSpellDamage(spellDamage, player);

					if (pet is NecromancerPet nPet)
					{
						/*
						int ownerIntMod = 125;
						if (pet.Owner is GamePlayer own) ownerIntMod += own.Intelligence;
						spellDamage *= ((nPet.GetModified(eProperty.Intelligence) + ownerIntMod) / 275.0);
						if (spellDamage < Spell.Damage) spellDamage = Spell.Damage;
*/
						
						if (pet.Owner is GamePlayer own)
						{
							//Delve * (acu/200+1) * (plusskillsfromitems/200+1) * (Relicbonus+1) * (mom+1) * (1 - enemyresist) 
							int manaStatValue = own.GetModified((eProperty)own.CharacterClass.ManaStat);
							//spellDamage *= ((manaStatValue - 50) / 275.0) + 1;
							spellDamage *= ((manaStatValue - own.Level) * 0.005) + 1;
						}
						
					}
					else
					{
						int ownerIntMod = 125;
						if (pet.Owner is GamePlayer own) ownerIntMod += own.Intelligence / 2;
						spellDamage *= ((pet.Intelligence + ownerIntMod ) / 275.0);
					}
						
					
					int modSkill = pet.Owner.GetModifiedSpecLevel(m_spellLine.Spec) -
					               pet.Owner.GetBaseSpecLevel(m_spellLine.Spec);
					spellDamage *= 1 + (modSkill * .005);
				}
				else if (SpellLine.KeyName == GlobalSpellsLines.Combat_Styles_Effect)
				{
					double weaponskillScalar = (3 + .02 * player.GetWeaponStat(player.AttackWeapon)) /
					                           (1 + .005 * player.GetWeaponStat(player.AttackWeapon));
					spellDamage *= (player.GetWeaponSkill(player.AttackWeapon) * weaponskillScalar /3 + 200) / 200;
				}
				else if (player.CharacterClass.ManaStat != eStat.UNDEFINED
				    && SpellLine.KeyName != GlobalSpellsLines.Combat_Styles_Effect
				    && m_spellLine.KeyName != GlobalSpellsLines.Mundane_Poisons
				    && SpellLine.KeyName != GlobalSpellsLines.Item_Effects
				    && player.CharacterClass.ID != (int)eCharacterClass.MaulerAlb
				    && player.CharacterClass.ID != (int)eCharacterClass.MaulerMid
				    && player.CharacterClass.ID != (int)eCharacterClass.MaulerHib
				    && player.CharacterClass.ID != (int)eCharacterClass.Vampiir)
				{
					//Delve * (acu/200+1) * (plusskillsfromitems/200+1) * (Relicbonus+1) * (mom+1) * (1 - enemyresist) 
					int manaStatValue = player.GetModified((eProperty)player.CharacterClass.ManaStat);
					//spellDamage *= ((manaStatValue - 50) / 275.0) + 1;
					spellDamage *= ((manaStatValue - player.Level) * 0.005) + 1;
					int modSkill = player.GetModifiedSpecLevel(m_spellLine.Spec) -
					               player.GetBaseSpecLevel(m_spellLine.Spec);
					spellDamage *= 1 + (modSkill * .005);

					//list casters get a little extra sauce
					if ((eCharacterClass) player.CharacterClass.ID is eCharacterClass.Wizard
					    or eCharacterClass.Theurgist
					    or eCharacterClass.Cabalist or eCharacterClass.Sorcerer or eCharacterClass.Necromancer
					    or eCharacterClass.Eldritch or eCharacterClass.Enchanter or eCharacterClass.Mentalist
					    or eCharacterClass.Animist or eCharacterClass.Valewalker
					    or eCharacterClass.Runemaster or eCharacterClass.Spiritmaster or eCharacterClass.Bonedancer)
					{
						spellDamage *= 1.10;
					}
					
					if (spellDamage < Spell.Damage) spellDamage = Spell.Damage;
				}
			}
			else if (Caster is GameNPC)
			{
				var npc = (GameNPC) Caster;
				int manaStatValue = npc.GetModified(eProperty.Intelligence);
				spellDamage = CapNPCSpellDamage(spellDamage, npc)*(manaStatValue + 200)/275.0;
			}

			if (spellDamage < 0)
				spellDamage = 0;

			return spellDamage;
		}

		/// <summary>
		/// Calculates the chance that the spell lands on target
		/// can be negative or above 100%
		/// </summary>
		/// <param name="target">spell target</param>
		/// <returns>chance that the spell lands on target</returns>
		public virtual int CalculateToHitChance(GameLiving target)
		{
			int spellLevel = Spell.Level;

			GameLiving caster = null;
			if (m_caster is GameNPC && (m_caster as GameNPC).Brain is ControlledNpcBrain)
			{
				caster = ((ControlledNpcBrain)((GameNPC)m_caster).Brain).Owner;
			}
			else
			{
				caster = m_caster;
			}

			int spellbonus = caster.GetModified(eProperty.SpellLevel);
			spellLevel += spellbonus;

			GamePlayer playerCaster = caster as GamePlayer;

			if (playerCaster != null)
			{
				if (spellLevel > playerCaster.MaxLevel)
				{
					spellLevel = playerCaster.MaxLevel;
				}
			}

			if (playerCaster != null && (m_spellLine.KeyName == GlobalSpellsLines.Combat_Styles_Effect || m_spellLine.KeyName.StartsWith(GlobalSpellsLines.Champion_Lines_StartWith)))
			{
				AttackData lastAD = playerCaster.TempProperties.getProperty<AttackData>("LastAttackData", null);
				spellLevel = (lastAD != null && lastAD.Style != null) ? lastAD.Style.Level : Math.Min(playerCaster.MaxLevel, target.Level);
			}
			//Console.WriteLine($"Spell level {spellLevel}");

			int bonustohit = m_caster.GetModified(eProperty.ToHitBonus);

			//Piercing Magic affects to-hit bonus too
			/*GameSpellEffect resPierce = SpellHandler.FindEffectOnTarget(m_caster, "PenetrateResists");
			if (resPierce != null)
				bonustohit += (int)resPierce.Spell.Value;
			*/
			/*
			http://www.camelotherald.com/news/news_article.php?storyid=704

			Q: Spell resists. Can you give me more details as to how the system works?

			A: Here's the answer, straight from the desk of the spell designer:

			"Spells have a factor of (spell level / 2) added to their chance to hit. (Spell level defined as the level the spell is awarded, chance to hit defined as
			the chance of avoiding the "Your target resists the spell!" message.) Subtracted from the modified to-hit chance is the target's (level / 2).
			So a L50 caster casting a L30 spell at a L50 monster or player, they have a base chance of 85% to hit, plus 15%, minus 25% for a net chance to hit of 75%.
			If the chance to hit goes over 100% damage or duration is increased, and if it goes below 55%, you still have a 55% chance to hit but your damage
			or duration is penalized. If the chance to hit goes below 0, you cannot hit at all. Once the spell hits, damage and duration are further modified
			by resistances.

			Note:  The last section about maintaining a chance to hit of 55% has been proven incorrect with live testing.  The code below is very close to live like.
			- Tolakram
			 */

			int hitchance = 88 + ((spellLevel - target.Level) / 2) + bonustohit;

			if (!(caster is GamePlayer && target is GamePlayer))
			{
				double mobScalar = m_caster.GetConLevel(target) > 3 ? 3 : m_caster.GetConLevel(target);
				hitchance -= (int)(mobScalar * ServerProperties.Properties.PVE_SPELL_CONHITPERCENT);
				hitchance += Math.Max(0, target.attackComponent.Attackers.Count - 1) * ServerProperties.Properties.MISSRATE_REDUCTION_PER_ATTACKERS;
			}

			if (Caster is GameNPC)
            {
				hitchance = (int)(87.5 - (target.Level - Caster.Level));
            }
			
			if (m_caster.effectListComponent.ContainsEffectForEffectType(eEffect.PiercingMagic))
			{
				var ecsSpell = m_caster.effectListComponent.GetSpellEffects()
					.FirstOrDefault(e => e.EffectType == eEffect.PiercingMagic);
				
				if (ecsSpell != null)
					hitchance += (int)ecsSpell.SpellHandler.Spell.Value;
			}

			//check for active RAs
			if (Caster.effectListComponent.ContainsEffectForEffectType(eEffect.MajesticWill))
			{
				var effect = Caster.effectListComponent
					.GetAllEffects().FirstOrDefault(e => e.EffectType == eEffect.MajesticWill);
				if (effect != null)
				{
					hitchance += (int)effect.Effectiveness * 5;
				}
			}

			return hitchance;
		}

		/// <summary>
		/// Calculates damage to target with resist chance and stores it in ad
		/// </summary>
		/// <param name="target">spell target</param>
		/// <returns>attack data</returns>
		public AttackData CalculateDamageToTarget(GameLiving target)
		{
			return CalculateDamageToTarget(target, 1);
		}


		/// <summary>
		/// Adjust damage based on chance to hit.
		/// </summary>
		/// <param name="damage"></param>
		/// <param name="hitChance"></param>
		/// <returns></returns>
		public virtual int AdjustDamageForHitChance(int damage, int hitChance)
		{
			int adjustedDamage = damage;

			if (hitChance < 55)
			{
				adjustedDamage += (int)(adjustedDamage * (hitChance - 55) * ServerProperties.Properties.SPELL_HITCHANCE_DAMAGE_REDUCTION_MULTIPLIER * 0.01);
			}

			return Math.Max(adjustedDamage, 1);
		}


		/// <summary>
		/// Calculates damage to target with resist chance and stores it in ad
		/// </summary>
		/// <param name="target">spell target</param>
		/// <param name="effectiveness">value from 0..1 to modify damage</param>
		/// <returns>attack data</returns>
		public virtual AttackData CalculateDamageToTarget(GameLiving target, double effectiveness)
		{
			AttackData ad = new AttackData();
			ad.Attacker = m_caster;
			ad.Target = target;
			ad.AttackType = AttackData.eAttackType.Spell;
			ad.SpellHandler = this;
			ad.AttackResult = eAttackResult.HitUnstyled;

			double minVariance;
			double maxVariance;
			
			CalculateDamageVariance(target, out minVariance, out maxVariance);
			double spellDamage = CalculateDamageBase(target);

			if (m_caster is GamePlayer)
			{
				effectiveness += m_caster.GetModified(eProperty.SpellDamage) * 0.01;

				// Relic bonus applied to damage, does not alter effectiveness or increase cap
				spellDamage *= (1.0 + RelicMgr.GetRelicBonusModifier(m_caster.Realm, eRelicType.Magic));

				/*
				eProperty skillProp = SkillBase.SpecToSkill(m_spellLine.Spec);
				if (skillProp != eProperty.Undefined)
				{
					var level = m_caster.GetModifiedFromItems(skillProp);
					spellDamage *= (1 + level / 200.0);
				}*/
			}

			// Apply casters effectiveness
			spellDamage *= m_caster.Effectiveness;

			int finalDamage = Util.Random((int)(minVariance * spellDamage), (int)(maxVariance * spellDamage));

			// Live testing done Summer 2009 by Bluraven, Tolakram  Levels 40, 45, 50, 55, 60, 65, 70
			// Damage reduced by chance < 55, no extra damage increase noted with hitchance > 100
			int hitChance = CalculateToHitChance(ad.Target);
			finalDamage = AdjustDamageForHitChance(finalDamage, hitChance);

			// apply spell effectiveness
			finalDamage = (int)(finalDamage * effectiveness);

			if ((m_caster is GamePlayer || (m_caster is GameNPC && (m_caster as GameNPC).Brain is IControlledBrain && m_caster.Realm != 0)))
			{
				if (target is GamePlayer)
					finalDamage = (int)((double)finalDamage * ServerProperties.Properties.PVP_SPELL_DAMAGE);
				else if (target is GameNPC)
					finalDamage = (int)((double)finalDamage * ServerProperties.Properties.PVE_SPELL_DAMAGE);
			}

			int cdamage = 0;
			if (finalDamage < 0)
				finalDamage = 0;

			eDamageType damageType = DetermineSpellDamageType();

			#region Resists
			eProperty property = target.GetResistTypeForDamage(damageType);
			// The Daoc resistsystem is since 1.65 a 2category system.
			// - First category are Item/Race/Buff/RvrBanners resists that are displayed in the characteroverview.
			// - Second category are resists that are given through RAs like avoidance of magic, brilliance aura of deflection.
			//   Those resist affect ONLY the spelldamage. Not the duration, not the effectiveness of debuffs.
			// so calculation is (finaldamage * Category1Modification) * Category2Modification
			// -> Remark for the future: VampirResistBuff is Category2 too.
			// - avi

			#region Primary Resists
			int primaryResistModifier = ad.Target.GetResist(damageType);

			/* Resist Pierce
			 * Resipierce is a special bonus which has been introduced with TrialsOfAtlantis.
			 * At the calculation of SpellDamage, it reduces the resistance that the victim recives
			 * through ITEMBONUSES for the specified percentage.
			 * http://de.daocpedia.eu/index.php/Resistenz_durchdringen (translated)
			 */
			int resiPierce = Caster.GetModified(eProperty.ResistPierce);
			GamePlayer ply = Caster as GamePlayer;
			if (resiPierce > 0 && Spell.SpellType != (byte)eSpellType.Archery)
			{
				//substract max ItemBonus of property of target, but atleast 0.
				primaryResistModifier -= Math.Max(0, Math.Min(ad.Target.ItemBonus[(int)property], resiPierce));
			}
			#endregion

			#region Secondary Resists
			//Using the resist BuffBonusCategory2 - its unused in ResistCalculator
			int secondaryResistModifier = target.SpecBuffBonusCategory[(int)property];

			if (secondaryResistModifier > 80)
				secondaryResistModifier = 80;
			#endregion

			int resistModifier = 0;
			//primary resists
			resistModifier += (int)(finalDamage * (double)primaryResistModifier * -0.01);
			//secondary resists
			resistModifier += (int)((finalDamage + (double)resistModifier) * (double)secondaryResistModifier * -0.01);
			//apply resists
			finalDamage += resistModifier;

			#endregion

			// Apply damage cap (this can be raised by effectiveness)
			if (finalDamage > DamageCap(effectiveness))
			{
				finalDamage = (int)DamageCap(effectiveness);
			}

			if (finalDamage < 0)
				finalDamage = 0;

			int criticalchance;

			if (this is DoTSpellHandler dot)
            {
				criticalchance = 0; //atlas - DoTs can only crit with Wild Arcana. This is handled by the DoTSpellHandler directly
				cdamage = 0;
            }
            else
            {
				criticalchance = m_caster.SpellCriticalChance;
            }			

			int randNum = Util.CryptoNextInt(1, 100); //grab our random number
			int critCap = Math.Min(50, criticalchance); //crit chance can be at most  50%

			if (this.Caster is GamePlayer spellCaster && spellCaster.UseDetailedCombatLog && critCap > 0)
			{
				spellCaster.Out.SendMessage($"spell crit chance: {critCap} random: {randNum}", eChatType.CT_DamageAdd, eChatLoc.CL_SystemWindow);
			}

			if (critCap > randNum && (finalDamage >= 1))
			{
				int critmax = (ad.Target is GamePlayer) ? finalDamage / 2 : finalDamage;
				cdamage = Util.Random(finalDamage / 10, critmax); //think min crit is 10% of damage
			}
			//Andraste
			if(ad.Target is GamePlayer && ad.Target.GetModified(eProperty.Conversion)>0)
			{
				int manaconversion=(int)Math.Round(((double)ad.Damage+(double)ad.CriticalDamage)*(double)ad.Target.GetModified(eProperty.Conversion)/200);
				//int enduconversion=(int)Math.Round((double)manaconversion*(double)ad.Target.MaxEndurance/(double)ad.Target.MaxMana);
				int enduconversion=(int)Math.Round(((double)ad.Damage+(double)ad.CriticalDamage)*(double)ad.Target.GetModified(eProperty.Conversion)/200);
				if(ad.Target.Mana+manaconversion>ad.Target.MaxMana) manaconversion=ad.Target.MaxMana-ad.Target.Mana;
				if(ad.Target.Endurance+enduconversion>ad.Target.MaxEndurance) enduconversion=ad.Target.MaxEndurance-ad.Target.Endurance;
				if(manaconversion<1) manaconversion=0;
				if(enduconversion<1) enduconversion=0;
				if(manaconversion>=1) (ad.Target as GamePlayer).Out.SendMessage("You gain "+manaconversion+" power points", eChatType.CT_Spell, eChatLoc.CL_SystemWindow);
				if(enduconversion>=1) (ad.Target as GamePlayer).Out.SendMessage("You gain "+enduconversion+" endurance points", eChatType.CT_Spell, eChatLoc.CL_SystemWindow);
				ad.Target.Endurance+=enduconversion; if(ad.Target.Endurance>ad.Target.MaxEndurance) ad.Target.Endurance=ad.Target.MaxEndurance;
				ad.Target.Mana+=manaconversion; if(ad.Target.Mana>ad.Target.MaxMana) ad.Target.Mana=ad.Target.MaxMana;
			}

			ad.Damage = finalDamage;
			ad.CriticalDamage = cdamage;
			ad.DamageType = damageType;
			ad.Modifier = resistModifier;

			// Attacked living may modify the attack data.  Primarily used for keep doors and components.
			ad.Target.ModifyAttack(ad);

			m_lastAttackData = ad;
			return ad;
		}

		public virtual double DamageCap(double effectiveness)
		{
			return Spell.Damage * 3.0 * effectiveness;
		}

		/// <summary>
		/// What damage type to use.  Overriden by archery
		/// </summary>
		/// <returns></returns>
		public virtual eDamageType DetermineSpellDamageType()
		{
			return Spell.DamageType;
		}

		/// <summary>
		/// Sends damage text messages but makes no damage
		/// </summary>
		/// <param name="ad"></param>
		public virtual void SendDamageMessages(AttackData ad)
		{
            string modmessage = "";
            if (ad.Modifier > 0)
                modmessage = " (+" + ad.Modifier + ")";
            if (ad.Modifier < 0)
                modmessage = " (" + ad.Modifier + ")";
            if (Caster is GamePlayer || Caster is NecromancerPet)
                MessageToCaster(string.Format("You hit {0} for {1}{2} damage!", ad.Target.GetName(0, false), ad.Damage, modmessage), eChatType.CT_YouHit);
            else if (Caster is GameNPC)
                MessageToCaster(string.Format("Your " + Caster.Name + " hits {0} for {1}{2} damage!",
                                              ad.Target.GetName(0, false), ad.Damage, modmessage), eChatType.CT_YouHit);
            if (ad.CriticalDamage > 0)
                MessageToCaster("You critically hit for an additional " + ad.CriticalDamage + " damage!" + " (" + m_caster.SpellCriticalChance + "%)", eChatType.CT_YouHit);
        }

		/// <summary>
		/// Make damage to target and send spell effect but no messages
		/// </summary>
		/// <param name="ad"></param>
		/// <param name="showEffectAnimation"></param>
		public virtual void DamageTarget(AttackData ad, bool showEffectAnimation)
		{
			DamageTarget(ad, showEffectAnimation, 0x14); //spell damage attack result
		}

		/// <summary>
		/// Make damage to target and send spell effect but no messages
		/// </summary>
		/// <param name="ad"></param>
		/// <param name="showEffectAnimation"></param>
		/// <param name="attackResult"></param>
		public virtual void DamageTarget(AttackData ad, bool showEffectAnimation, int attackResult)
		{
			ad.AttackResult = eAttackResult.HitUnstyled;
			if (showEffectAnimation)
			{
				SendEffectAnimation(ad.Target, 0, false, 1);
			}

			
			// send animation before dealing damage else dead livings show no animation
			ad.Target.OnAttackedByEnemy(ad);
			ad.Attacker.DealDamage(ad);
			if (ad.Damage == 0 && ad.Target is GameNPC)
			{
				IOldAggressiveBrain aggroBrain = ((GameNPC)ad.Target).Brain as IOldAggressiveBrain;
				if (aggroBrain != null)
					aggroBrain.AddToAggroList(Caster, 1);
				
				if (this is not DoTSpellHandler and not StyleBleeding)
				{
					if (Caster.Realm == 0 || ad.Target.Realm == 0)
					{
						ad.Target.LastAttackedByEnemyTickPvE = GameLoop.GameLoopTime;
						Caster.LastAttackTickPvE = GameLoop.GameLoopTime;
					}
					else
					{
						ad.Target.LastAttackedByEnemyTickPvP = GameLoop.GameLoopTime;
						Caster.LastAttackTickPvP = GameLoop.GameLoopTime;
					}
				}
			}

			if (ad.Damage > 0)
			{
				Parallel.ForEach((ad.Target.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE)).OfType<GamePlayer>(), player =>
				{
					player.Out.SendCombatAnimation(ad.Attacker, ad.Target, 0, 0, 0, 0, (byte)attackResult, ad.Target.HealthPercent);
				});
			}
				


			m_lastAttackData = ad;
		}

		#endregion

		#region saved effects
		public virtual PlayerXEffect GetSavedEffect(GameSpellEffect effect)
		{
			return null;
		}

		public virtual void OnEffectRestored(GameSpellEffect effect, int[] vars)
		{ }

		public virtual int OnRestoredEffectExpires(GameSpellEffect effect, int[] vars, bool noMessages)
		{
			return 0;
		}
		#endregion
		
		#region tooltip handling
		/// <summary>
		/// Return the given Delve Writer with added keyvalue pairs.
		/// </summary>
		/// <param name="dw"></param>
		/// <param name="id"></param>
		public virtual void TooltipDelve(ref MiniDelveWriter dw)
		{
			if (dw == null)
				return;

			dw.AddKeyValuePair("Function", GetDelveType((eSpellType)Spell.SpellType));
			dw.AddKeyValuePair("Index", unchecked((ushort)Spell.InternalID));
			dw.AddKeyValuePair("Name", Spell.Name);
			
			if (Spell.CastTime > 2000)
				dw.AddKeyValuePair("cast_timer", Spell.CastTime - 2000); //minus 2 seconds (why mythic?)
			else if (!Spell.IsInstantCast)
				dw.AddKeyValuePair("cast_timer", 0); //minus 2 seconds (why mythic?)
			
			if (Spell.IsInstantCast)
				dw.AddKeyValuePair("instant","1");
			if ((int)Spell.DamageType > 0)
			{
				//Added to fix the mis-match between client and server
				int addTo = 1;
				switch ((int)Spell.DamageType)
                {
					case 10:
						addTo = 6;
						break;
					case 12:
						addTo = 10;
						break;
					case 15:
						addTo = 2;
						break;
					default:
						addTo = 1;
						break;
				}
				dw.AddKeyValuePair("damage_type", (int)Spell.DamageType + addTo); // Damagetype not the same as dol
			}
			if (Spell.Level > 0)
			{
				dw.AddKeyValuePair("level", Spell.Level);
				dw.AddKeyValuePair("power_level", Spell.Level);
			}
			if (Spell.CostPower)
				dw.AddKeyValuePair("power_cost", Spell.Power);
			if (Spell.Range > 0)
				dw.AddKeyValuePair("range", Spell.Range);
			if (Spell.Duration > 0)
				dw.AddKeyValuePair("duration", Spell.Duration / 1000); //seconds
			if (GetDurationType() > 0)
				dw.AddKeyValuePair("dur_type", GetDurationType());
			if (Spell.HasRecastDelay)
				dw.AddKeyValuePair("timer_value", Spell.RecastDelay / 1000);
			
			if (GetSpellTargetType() > 0)
				dw.AddKeyValuePair("target", GetSpellTargetType());

			//if (!string.IsNullOrEmpty(Spell.Description))
			//	dw.AddKeyValuePair("description_string", Spell.Description);

			if (Spell.IsAoE)
				dw.AddKeyValuePair("radius", Spell.Radius);
			if (Spell.IsConcentration)
				dw.AddKeyValuePair("concentration_points", Spell.Concentration);
			if (Spell.Frequency > 0)
				dw.AddKeyValuePair("frequency", Spell.SpellType == (byte)eSpellType.OffensiveProc || Spell.SpellType == (byte)eSpellType.OffensiveProc ? Spell.Frequency / 100 : Spell.Frequency);

			WriteBonus(ref dw);
			WriteParm(ref dw);
			WriteDamage(ref dw);
			WriteSpecial(ref dw);

			if (Spell.HasSubSpell)
				if (Spell.SpellType == (byte)eSpellType.Bomber || Spell.SpellType == (byte)eSpellType.SummonAnimistFnF)
					dw.AddKeyValuePair("delve_spell", SkillBase.GetSpellByID(Spell.SubSpellID).InternalID);
				else
					dw.AddKeyValuePair("parm", SkillBase.GetSpellByID(Spell.SubSpellID).InternalID);

			if (!dw.Values.ContainsKey("parm") && (eSpellType)Spell.SpellType != eSpellType.MesmerizeDurationBuff)
				dw.AddKeyValuePair("parm", "1");
		}

		private string GetDelveType(eSpellType spellType)
        {
			switch (spellType)
            {
				case eSpellType.AblativeArmor:
					return "hit_buffer";
				case eSpellType.AcuityBuff:
				case eSpellType.DexterityQuicknessBuff:
				case eSpellType.StrengthConstitutionBuff:
					return "twostat";
				case eSpellType.Amnesia:
					return "amnesia";
				case eSpellType.ArmorAbsorptionBuff:
					return "absorb";
				case eSpellType.ArmorAbsorptionDebuff:
					return "nabsorb";
				case eSpellType.ArmorFactorBuff:
				case eSpellType.PaladinArmorFactorBuff:
					return "shield";
				case eSpellType.Bolt:
					return "bolt";
				case eSpellType.Bladeturn:
				case eSpellType.CelerityBuff:
				case eSpellType.CombatSpeedBuff:
				case eSpellType.CombatSpeedDebuff:
				case eSpellType.Confusion:
				case eSpellType.Mesmerize:
				case eSpellType.Mez:
				case eSpellType.Nearsight:
				case eSpellType.SavageCombatSpeedBuff:
				case eSpellType.SavageEvadeBuff:
				case eSpellType.SavageParryBuff:
				case eSpellType.SpeedEnhancement:
					return "combat";
				case eSpellType.BodyResistBuff:
				case eSpellType.BodySpiritEnergyBuff:
				case eSpellType.ColdResistBuff:
				case eSpellType.EnergyResistBuff:
				case eSpellType.HeatColdMatterBuff:
				case eSpellType.HeatResistBuff:
				case eSpellType.MatterResistBuff:
				case eSpellType.SavageCrushResistanceBuff:
				case eSpellType.SavageSlashResistanceBuff:
				case eSpellType.SavageThrustResistanceBuff:
				case eSpellType.SpiritResistBuff:
					return "resistance";
				case eSpellType.BodyResistDebuff:
				case eSpellType.ColdResistDebuff:
				case eSpellType.EnergyResistDebuff:
				case eSpellType.HeatResistDebuff:
				case eSpellType.MatterResistDebuff:
				case eSpellType.SpiritResistDebuff:
					return "nresistance";
				case eSpellType.SummonTheurgistPet:
				case eSpellType.Bomber:
				case eSpellType.SummonAnimistFnF:
					return "dsummon";
				case eSpellType.Charm:
					return "charm";
				case eSpellType.CombatHeal:
				case eSpellType.Heal:
					return "heal";
				case eSpellType.ConstitutionBuff:
				case eSpellType.DexterityBuff:
				case eSpellType.StrengthBuff:
				case eSpellType.AllStatsBarrel:
					return "stat";
				case eSpellType.ConstitutionDebuff:
				case eSpellType.DexterityDebuff:
				case eSpellType.StrengthDebuff:
					return "nstat";
				case eSpellType.CureDisease:
				case eSpellType.CurePoison:
				case eSpellType.CureNearsightCustom:
					return "rem_eff_ty";
				case eSpellType.CureMezz:
					return "remove_eff";
				case eSpellType.DamageAdd:
					return "dmg_add";
				case eSpellType.DamageOverTime:
				case eSpellType.StyleBleeding:
					return "dot";
				case eSpellType.DamageShield:
					return "dmg_shield";
				case eSpellType.DamageSpeedDecrease:
				case eSpellType.SpeedDecrease:
				case eSpellType.UnbreakableSpeedDecrease:
					return "snare";
				case eSpellType.DefensiveProc:
					return "def_proc";
				case eSpellType.DexterityQuicknessDebuff:
				case eSpellType.StrengthConstitutionDebuff:
					return "ntwostat";
				case eSpellType.DirectDamage:
					return "direct";
				case eSpellType.DirectDamageWithDebuff:
					return "nresist_dam";
				case eSpellType.Disease:
					return "disease";
				case eSpellType.EnduranceRegenBuff:
				case eSpellType.HealthRegenBuff:
				case eSpellType.PowerRegenBuff:
					return "enhancement";
				case eSpellType.HealOverTime:
					return "regen";
				case eSpellType.Lifedrain:
					return "lifedrain";
				case eSpellType.LifeTransfer:
					return "transfer";
				case eSpellType.MeleeDamageDebuff:
					return "ndamage";
				case eSpellType.MesmerizeDurationBuff:
					return "mez_dampen";
				case eSpellType.OffensiveProc:
					return "off_proc";
				case eSpellType.PetConversion:
					return "reclaim";
				case eSpellType.Resurrect:
					return "raise_dead";
				case eSpellType.SavageEnduranceHeal:
					return "fat_heal";
				case eSpellType.SpreadHeal:
					return "spreadheal";
				case eSpellType.Stun:
					return "paralyze";				
				case eSpellType.SummonCommander:
				case eSpellType.SummonDruidPet:
				case eSpellType.SummonHunterPet:
				case eSpellType.SummonSimulacrum:
				case eSpellType.SummonSpiritFighter:
				case eSpellType.SummonUnderhill:
					return "summon";
				case eSpellType.SummonMinion:
					return "gsummon";
				case eSpellType.SummonNecroPet:
					return "ssummon";
				case eSpellType.StyleCombatSpeedDebuff:
				case eSpellType.StyleStun:
				case eSpellType.StyleSpeedDecrease:				
					return "add_effect";
				case eSpellType.StyleTaunt:
					if (Spell.Value > 0)
						return "taunt";
					else
						return "detaunt";
				case eSpellType.Taunt:
					return "taunt";
				case eSpellType.PetSpell:
				case eSpellType.SummonAnimistPet:
					return "petcast";
				case eSpellType.PetLifedrain:
					return "lifedrain";
				case eSpellType.PowerDrainPet:
					return "powerdrain";
				case eSpellType.PowerTransferPet:
					return "power_xfer";
				case eSpellType.ArmorFactorDebuff:
					return "nshield";
				case eSpellType.Grapple:
					return "Grapple";
				default:
					return "light";

			}
        }

		private void WriteBonus(ref MiniDelveWriter dw)
        {
			switch ((eSpellType)Spell.SpellType)
			{
				case eSpellType.AblativeArmor:
					dw.AddKeyValuePair("bonus", Spell.Damage > 0 ? Spell.Damage : 25);
					break;
				case eSpellType.AcuityBuff:
				case eSpellType.ArmorAbsorptionBuff:
				case eSpellType.ArmorAbsorptionDebuff:
				case eSpellType.ArmorFactorBuff:
				case eSpellType.BodyResistBuff:
				case eSpellType.BodyResistDebuff:
				case eSpellType.BodySpiritEnergyBuff:
				case eSpellType.ColdResistBuff:
				case eSpellType.ColdResistDebuff:
				case eSpellType.CombatSpeedBuff:
				case eSpellType.CelerityBuff:
				case eSpellType.ConstitutionBuff:
				case eSpellType.ConstitutionDebuff:
				case eSpellType.DexterityBuff:
				case eSpellType.DexterityDebuff:
				case eSpellType.DexterityQuicknessBuff:
				case eSpellType.DexterityQuicknessDebuff:
				case eSpellType.DirectDamageWithDebuff:
				case eSpellType.EnergyResistBuff:
				case eSpellType.EnergyResistDebuff:
				case eSpellType.HealOverTime:
				case eSpellType.HeatColdMatterBuff:
				case eSpellType.HeatResistBuff:
				case eSpellType.HeatResistDebuff:
				case eSpellType.MatterResistBuff:
				case eSpellType.MatterResistDebuff:
				case eSpellType.MeleeDamageBuff:
				case eSpellType.MeleeDamageDebuff:
				case eSpellType.MesmerizeDurationBuff:
				case eSpellType.PaladinArmorFactorBuff:
				case eSpellType.PetConversion:
				case eSpellType.SavageCombatSpeedBuff:
				case eSpellType.SavageCrushResistanceBuff:
				case eSpellType.SavageDPSBuff:
				case eSpellType.SavageEvadeBuff:
				case eSpellType.SavageParryBuff:
				case eSpellType.SavageSlashResistanceBuff:
				case eSpellType.SavageThrustResistanceBuff:
				case eSpellType.SpeedEnhancement:
				case eSpellType.SpeedOfTheRealm:
				case eSpellType.SpiritResistBuff:
				case eSpellType.SpiritResistDebuff:
				case eSpellType.StrengthBuff:
				case eSpellType.AllStatsBarrel:
				case eSpellType.StrengthConstitutionBuff:
				case eSpellType.StrengthConstitutionDebuff:
				case eSpellType.StrengthDebuff:
				case eSpellType.ToHitBuff:
				case eSpellType.FumbleChanceDebuff:
				case eSpellType.AllStatsPercentDebuff:
				case eSpellType.CrushSlashThrustDebuff:
				case eSpellType.EffectivenessDebuff:
				case eSpellType.ParryBuff:
				case eSpellType.SavageEnduranceHeal:
				case eSpellType.SlashResistDebuff:
				case eSpellType.ArmorFactorDebuff:
				case eSpellType.WeaponSkillBuff:
				case eSpellType.FlexibleSkillBuff:
					dw.AddKeyValuePair("bonus", Spell.Value);
					break;
				case eSpellType.DamageSpeedDecrease:
				case eSpellType.SpeedDecrease:
				case eSpellType.StyleSpeedDecrease:
				case eSpellType.UnbreakableSpeedDecrease:
					dw.AddKeyValuePair("bonus", 100 - Spell.Value);
					break;
				case eSpellType.DefensiveProc:
				case eSpellType.OffensiveProc:
					dw.AddKeyValuePair("bonus", Spell.Frequency / 100);
					break;
				case eSpellType.Lifedrain:
				case eSpellType.PetLifedrain:
					dw.AddKeyValuePair("bonus", Spell.LifeDrainReturn / 10);
					break;
				case eSpellType.PowerDrainPet:
					dw.AddKeyValuePair("bonus", Spell.LifeDrainReturn);
					break;
				case eSpellType.Resurrect:
					dw.AddKeyValuePair("bonus", Spell.ResurrectMana);
					break;
			}
		}

		private void WriteParm(ref MiniDelveWriter dw)
        {
			string parm = "parm";
			switch ((eSpellType)Spell.SpellType)
            {
				case eSpellType.CombatSpeedDebuff:
				
				case eSpellType.DexterityBuff:
				case eSpellType.DexterityDebuff:
				case eSpellType.DexterityQuicknessBuff:
				case eSpellType.DexterityQuicknessDebuff:
				case eSpellType.PowerRegenBuff:
				case eSpellType.StyleCombatSpeedDebuff:
					dw.AddKeyValuePair(parm, "2");
					break;
				case eSpellType.AcuityBuff:
				case eSpellType.ConstitutionBuff:
				case eSpellType.AllStatsBarrel:
				case eSpellType.ConstitutionDebuff:
				case eSpellType.EnduranceRegenBuff:
					dw.AddKeyValuePair(parm, "3");
					break;
				case eSpellType.Confusion:
					dw.AddKeyValuePair(parm, "5");
					break;
				case eSpellType.CureMezz:
				case eSpellType.Mesmerize:
					dw.AddKeyValuePair(parm, "6");
					break;
				case eSpellType.Bladeturn:
					dw.AddKeyValuePair(parm, "9");
					break;
				case eSpellType.HeatResistBuff:
				case eSpellType.HeatResistDebuff:
				case eSpellType.SpeedEnhancement:
					dw.AddKeyValuePair(parm, "10");
					break;
				case eSpellType.ColdResistBuff:
				case eSpellType.ColdResistDebuff:
				case eSpellType.CurePoison:
				case eSpellType.Nearsight:
				case eSpellType.CureNearsightCustom:
					dw.AddKeyValuePair(parm, "12");
					break;
				case eSpellType.MatterResistBuff:
				case eSpellType.MatterResistDebuff:
				case eSpellType.SavageParryBuff:
					dw.AddKeyValuePair(parm, "15");
					break;
				case eSpellType.BodyResistBuff:
				case eSpellType.BodyResistDebuff:
				case eSpellType.SavageEvadeBuff:
					dw.AddKeyValuePair(parm, "16");
					break;
				case eSpellType.SpiritResistBuff:
				case eSpellType.SpiritResistDebuff:
					dw.AddKeyValuePair(parm, "17");
					break;
				case eSpellType.StyleBleeding:
					dw.AddKeyValuePair(parm, "20");
					break;
				case eSpellType.EnergyResistBuff:
				case eSpellType.EnergyResistDebuff:
					dw.AddKeyValuePair(parm, "22");
					break;
				case eSpellType.SpeedOfTheRealm:
					dw.AddKeyValuePair(parm, "35");
					break;
				case eSpellType.CelerityBuff:
				case eSpellType.SavageCombatSpeedBuff:
				case eSpellType.CombatSpeedBuff:
					dw.AddKeyValuePair(parm, "36");
					break;
				case eSpellType.HeatColdMatterBuff:
					dw.AddKeyValuePair(parm, "97");
					break;
				case eSpellType.BodySpiritEnergyBuff:
					dw.AddKeyValuePair(parm, "98");
					break;
				case eSpellType.DirectDamageWithDebuff:
					//Added to fix the mis-match between client and server
					int addTo = 1;
					switch ((int)Spell.DamageType)
					{
						case 10:
							addTo = 6;
							break;
						case 12:
							addTo = 10;
							break;
						case 15:
							addTo = 2;
							break;
						default:
							addTo = 1;
							break;
					}
					dw.AddKeyValuePair(parm, (int)Spell.DamageType + addTo);
					break;
				case eSpellType.SavageCrushResistanceBuff:
					dw.AddKeyValuePair(parm, (int)eDamageType.Crush);
					break;
				case eSpellType.SavageSlashResistanceBuff:
					dw.AddKeyValuePair(parm, (int)eDamageType.Slash);
					break;
				case eSpellType.SavageThrustResistanceBuff:
					dw.AddKeyValuePair(parm, (int)eDamageType.Thrust);
					break;
                case eSpellType.DefensiveProc:
                case eSpellType.OffensiveProc:
                    dw.AddKeyValuePair(parm, SkillBase.GetSpellByID((int)Spell.Value).InternalID);
                    break;
            }
		}

		private void WriteDamage(ref MiniDelveWriter dw)
        {
			switch ((eSpellType)Spell.SpellType)
            {
				case eSpellType.AblativeArmor:
				case eSpellType.CombatHeal:
				case eSpellType.EnduranceRegenBuff:
				case eSpellType.Heal:
				case eSpellType.HealOverTime:
				case eSpellType.HealthRegenBuff:
				case eSpellType.LifeTransfer:
				case eSpellType.PowerRegenBuff:
				case eSpellType.SavageEnduranceHeal:
				case eSpellType.SpreadHeal:
				case eSpellType.Taunt:
					dw.AddKeyValuePair("damage", Spell.Value);
					break;
				case eSpellType.Bolt:
				case eSpellType.DamageAdd:
				case eSpellType.DamageShield:
				case eSpellType.DamageSpeedDecrease:
				case eSpellType.DirectDamage:
				case eSpellType.DirectDamageWithDebuff:
				case eSpellType.Lifedrain:
				case eSpellType.PetLifedrain:
				case eSpellType.PowerDrainPet:
					dw.AddKeyValuePair("damage", Spell.Damage * 10);
					break;
				case eSpellType.DamageOverTime:
				case eSpellType.StyleBleeding:
					dw.AddKeyValuePair("damage", Spell.Damage);
					break;
				case eSpellType.Resurrect:
					dw.AddKeyValuePair("damage", Spell.ResurrectHealth);
					break;
				case eSpellType.StyleTaunt:
					dw.AddKeyValuePair("damage", Spell.Value < 0 ? -Spell.Value : Spell.Value);
					break;
				case eSpellType.PowerTransferPet:
					dw.AddKeyValuePair("damage", Spell.Value * 10);
					break;								
				case eSpellType.SummonHunterPet:
				case eSpellType.SummonSimulacrum:
				case eSpellType.SummonSpiritFighter:
				case eSpellType.SummonUnderhill:
					dw.AddKeyValuePair("damage", 44);
					break;
				case eSpellType.SummonCommander:
				case eSpellType.SummonDruidPet:
				case eSpellType.SummonMinion:
					dw.AddKeyValuePair("damage", Spell.Value);
					break;
			}
        }

		private void WriteSpecial(ref MiniDelveWriter dw)
		{
			switch ((eSpellType)Spell.SpellType)
			{
				case eSpellType.Bomber:
					//dw.AddKeyValuePair("description_string", "Summon an elemental sprit to fight for the caster briefly.");
						break;
				case eSpellType.Charm:
					dw.AddKeyValuePair("power_level", Spell.Value);

					// var baseMessage = "Attempts to bring the target monster under the caster's control.";
					switch ((CharmSpellHandler.eCharmType)Spell.AmnesiaChance)
					{
						case CharmSpellHandler.eCharmType.All:
							// Message: Attempts to bring the target monster under the caster's control. Spell works on all monster types. Cannot charm named or epic monsters.
							dw.AddKeyValuePair("delve_string", LanguageMgr.GetTranslation(((GamePlayer) Caster).Client, "CharmSpell.DelveInfo.Desc.AllMonsterTypes"));
							break;
						case CharmSpellHandler.eCharmType.Animal:
							// Message: Attempts to bring the target monster under the caster's control. Spell only works on animals. Cannot charm named or epic monsters.
							dw.AddKeyValuePair("delve_string", LanguageMgr.GetTranslation(((GamePlayer) Caster).Client, "CharmSpell.DelveInfo.Desc.Animal"));
							break;
						case CharmSpellHandler.eCharmType.Humanoid:
							// Message: Attempts to bring the target monster under the caster's control. Spell only works on humanoids. Cannot charm named or epic monsters.
							dw.AddKeyValuePair("delve_string", LanguageMgr.GetTranslation(((GamePlayer) Caster).Client, "CharmSpell.DelveInfo.Desc.Humanoid"));
							break;
						case CharmSpellHandler.eCharmType.Insect:
							// Message: Attempts to bring the target monster under the caster's control. Spell only works on insects. Cannot charm named or epic monsters.
							dw.AddKeyValuePair("delve_string", LanguageMgr.GetTranslation(((GamePlayer) Caster).Client, "CharmSpell.DelveInfo.Desc.Insect"));
							break;
						case CharmSpellHandler.eCharmType.HumanoidAnimal:
							// Message: Attempts to bring the target monster under the caster's control. Spell only works on animals and humanoids. Cannot charm named or epic monsters.
							dw.AddKeyValuePair("delve_string", LanguageMgr.GetTranslation(((GamePlayer) Caster).Client, "CharmSpell.DelveInfo.Desc.HumanoidAnimal"));
							break;
						case CharmSpellHandler.eCharmType.HumanoidAnimalInsect:
							// Message: Attempts to bring the target monster under the caster's control. Spell only works on animals, humanoids, insects, and reptiles. Cannot charm named or epic monsters.
							dw.AddKeyValuePair("delve_string", LanguageMgr.GetTranslation(((GamePlayer) Caster).Client, "CharmSpell.DelveInfo.Desc.HumanoidAnimalInsect"));
							break;
						case CharmSpellHandler.eCharmType.HumanoidAnimalInsectMagical:
							// Message: Attempts to bring the target monster under the caster's control. Spell only works on animals, elemental, humanoids, insects, magical, plant, and reptile monster types. Cannot charm named or epic monsters.
							dw.AddKeyValuePair("delve_string", LanguageMgr.GetTranslation(((GamePlayer) Caster).Client, "CharmSpell.DelveInfo.Desc.HumanoidAnimalInsectMagical"));
							break;
						case CharmSpellHandler.eCharmType.HumanoidAnimalInsectMagicalUndead:
							// Message: Attempts to bring the target monster under the caster's control. Spell only works on animals, elemental, humanoids, insects, magical, plant, reptile, and undead monster types. Cannot charm named or epic monsters.
							dw.AddKeyValuePair("delve_string", LanguageMgr.GetTranslation(((GamePlayer) Caster).Client, "CharmSpell.DelveInfo.Desc.HumanoidAnimalInsectMagicalUndead"));
							break;
					}
					break;
				case eSpellType.CombatSpeedBuff:
				case eSpellType.CelerityBuff:
					dw.AddKeyValuePair("power_level", Spell.Value * 2);
					break;

				case eSpellType.Confusion:
					dw.AddKeyValuePair("power_level", Spell.Value > 0 ? Spell.Value : 100);
					break;
				case eSpellType.CombatSpeedDebuff:
					dw.AddKeyValuePair("power_level", -Spell.Value);
					break;
				case eSpellType.CureMezz:
					dw.AddKeyValuePair("type1", "8");
					break;
				case eSpellType.Disease:
					dw.AddKeyValuePair("delve_string", "Inflicts a wasting disease on the target that slows target by 15 %, reduces strength by 7.5 % and inhibits healing by 50 %");
					break;
				//case eSpellType.DefensiveProc:
				//case eSpellType.OffensiveProc:
				//	dw.AddKeyValuePair("delve_spell", Spell.Value);
				//	break;
				case eSpellType.FatigueConsumptionBuff:
					dw.AddKeyValuePair("delve_string", $"The target's actions require {(int)Spell.Value}% less endurance.");
					break;
				case eSpellType.FatigueConsumptionDebuff:
					dw.AddKeyValuePair("delve_string", $"The target's actions require {(int)Spell.Value}% more endurance.");
					break;
				case eSpellType.MeleeDamageBuff:
					dw.AddKeyValuePair("delve_string", $"Increases your melee damage by {(int)Spell.Value}%.");
					break;
				case eSpellType.MesmerizeDurationBuff:
					dw.AddKeyValuePair("damage_type", "22");
					dw.AddKeyValuePair("dur_type", "2");
					dw.AddKeyValuePair("power_level", "29");
					break;
				case eSpellType.Nearsight:
					dw.AddKeyValuePair("power_level", Spell.Value);
					break;
				case eSpellType.PetConversion:
					dw.AddKeyValuePair("delve_string", "Banishes the caster's pet and reclaims some of its energy.");
					break;
				case eSpellType.Resurrect:
					dw.AddKeyValuePair("amount_increase", Spell.ResurrectMana);
					dw.AddKeyValuePair("type1", "65");
					dw.Values["target"] = 8.ToString();
					break;
				case eSpellType.SavageCombatSpeedBuff:
					dw.AddKeyValuePair("cost_type", "2");
					dw.AddKeyValuePair("power_level", Spell.Value * 2);
					break;
				case eSpellType.SavageCrushResistanceBuff:
				case eSpellType.SavageEnduranceHeal:
				case eSpellType.SavageParryBuff:
				case eSpellType.SavageSlashResistanceBuff:
				case eSpellType.SavageThrustResistanceBuff:
					dw.AddKeyValuePair("cost_type", "2");
					break;
				case eSpellType.SavageDPSBuff:
					dw.AddKeyValuePair("cost_type", "2");
					dw.AddKeyValuePair("delve_string", $"Increases your melee damage by {(int)Spell.Value}%.");
					break;
				case eSpellType.SavageEvadeBuff:
					dw.AddKeyValuePair("cost_type", "2");
					dw.AddKeyValuePair("delve_string", $"Increases your chance to evade by {(int)Spell.Value}%.");
					break;
				case eSpellType.SummonAnimistPet:
				case eSpellType.SummonCommander:
				case eSpellType.SummonDruidPet:
				case eSpellType.SummonHunterPet:
				case eSpellType.SummonNecroPet:
				case eSpellType.SummonSimulacrum:
				case eSpellType.SummonSpiritFighter:
				case eSpellType.SummonUnderhill:
					dw.AddKeyValuePair("power_level", Spell.Damage);
					//dw.AddKeyValuePair("delve_string", "Summons a Pet to serve you.");
					//dw.AddKeyValuePair("description_string", "Summons a Pet to serve you.");
					break;
				case eSpellType.SummonMinion:
					dw.AddKeyValuePair("power_level", Spell.Value);
					break;
				case eSpellType.StyleStun:
					dw.AddKeyValuePair("type1", "22");
					break;
				case eSpellType.StyleSpeedDecrease:
					dw.AddKeyValuePair("type1", "39");
					break;
				case eSpellType.StyleCombatSpeedDebuff:
					dw.AddKeyValuePair("type1", "8");
					dw.AddKeyValuePair("power_level", -Spell.Value);
					break;
				case eSpellType.TurretPBAoE:
					dw.AddKeyValuePair("delve_string", $"Target takes {(int)Spell.Damage} damage. Spell affects everyone in the immediate radius of the caster's pet, and does less damage the further away they are from the caster's pet.");
					break;
				case eSpellType.TurretsRelease:
					dw.AddKeyValuePair("delve_string", "Unsummons all the animist turret(s) in range.");
					break;
				case eSpellType.StyleRange:
					dw.AddKeyValuePair("delve_string", $"Hits target up to {(int)Spell.Value} units away.");
					break;
				case eSpellType.MultiTarget:
					dw.AddKeyValuePair("delve_string", $"Hits {(int)Spell.Value} additonal target(s) within melee range.");
					break;
				case eSpellType.PiercingMagic:
					dw.AddKeyValuePair("delve_string", $"Effectiveness of the target's spells is increased by {(int)Spell.Value}%. Against higher level opponents than the target, this should reduce the chance of a full resist.");
					break;
				case eSpellType.StyleTaunt:
					if (Spell.Value < 0)
						dw.AddKeyValuePair("delve_string", $"Decreases your threat to monster targets by {-(int)Spell.Value} damage.");
					break;
				case eSpellType.NaturesShield:
					dw.AddKeyValuePair("delve_string", $"Gives the user a {(int)Spell.Value}% base chance to block ranged melee attacks while this style is prepared.");
					break;
				case eSpellType.SlashResistDebuff:
					dw.AddKeyValuePair("delve_string", $"Decreases target's resistance to Slash by {(int)Spell.Value}% for {(int)Spell.Duration / 1000} seconds.");
					break;
					
			}
		}

		/// <summary>
		/// Returns delve code for target
		/// </summary>
		/// <param name="target"></param>
		/// <returns></returns>
		protected virtual int GetSpellTargetType()
		{
			switch (Spell.Target)
			{
				case "Realm":
					return 7;
				case "Self":
					return 0;
				case "Enemy":
					return 1;
				case "Pet":
					return 6;
				case "Group":
					return 3;
				case "Area":
					return 0; // TODO
				default:
					return 0;
			}
		}

		protected virtual int GetDurationType()
		{
			//2-seconds,4-conc,5-focus
			if (Spell.Duration>0)
			{
				return 2;
			}
			if (Spell.IsConcentration)
			{
				return 4;
			}


			return 0;
		}
		#endregion

	}
}
