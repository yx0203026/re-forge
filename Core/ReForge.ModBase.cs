#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

/// <summary>
/// ReForge 模组基类：提供统一初始化流程，尽量减少第三方 ModMain 样板代码。
/// </summary>
public abstract class ReForgeModBase
{
	protected abstract string ModId { get; }

	/// <summary>
	/// 是否在初始化期间自动注册 RunStarted 回调（带重试）。
	/// </summary>
	protected virtual bool EnableRunStartedHook => false;

	/// <summary>
	/// Mixin 严格模式，默认关闭以提高第三方模组容错。
	/// </summary>
	protected virtual bool StrictMixinMode => false;

	/// <summary>
	/// 初始化前阶段注册（可用于非常早期的基础绑定）。
	/// </summary>
	protected virtual IEnumerable<Action> PreInitializationRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 初始化主阶段注册（建议放置常规 Configure 逻辑）。
	/// </summary>
	protected virtual IEnumerable<Action> InitializationRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 阶段（如难度/层级）注册。
	/// </summary>
	protected virtual IEnumerable<Action> AscensionRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 卡牌注册。
	/// </summary>
	protected virtual IEnumerable<Action> CardRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 力量注册。
	/// </summary>
	protected virtual IEnumerable<Action> PowerRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 遗物注册。
	/// </summary>
	protected virtual IEnumerable<Action> RelicRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 药水注册（占位）。
	/// </summary>
	protected virtual IEnumerable<Action> PotionRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 怪物注册（占位）。
	/// </summary>
	protected virtual IEnumerable<Action> MonsterRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// Boss 注册（占位）。
	/// </summary>
	protected virtual IEnumerable<Action> BossRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 卡池注册。
	/// </summary>
	protected virtual IEnumerable<Action> CardPoolRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 事件注册。
	/// </summary>
	protected virtual IEnumerable<Action> EventRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 战斗及时事件注册。
	/// 与普通 EventRegistrations 分离，避免语义混淆。
	/// </summary>
	protected virtual IEnumerable<Action> BattleEventRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 远古事件注册。
	/// </summary>
	protected virtual IEnumerable<Action> AncientEventRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 模型注入阶段注册（在模型池注册前执行）。
	/// </summary>
	protected virtual IEnumerable<Action> ModelInjectionRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 模型池注册阶段。
	/// </summary>
	protected virtual IEnumerable<Action> ModelPoolRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 需注册的卡牌立绘路径对 (modelEntry, resourcePath)。
	/// </summary>
	protected virtual IEnumerable<(string modelEntry, string resourcePath)> CardPortraitRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 需注册的遗物图标路径对 (modelEntry, resourcePath)。
	/// </summary>
	protected virtual IEnumerable<(string modelEntry, string resourcePath)> RelicIconRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 需注册的遗物描边图标路径对 (modelEntry, resourcePath)。
	/// </summary>
	protected virtual IEnumerable<(string modelEntry, string resourcePath)> RelicIconOutlineRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 需注册的遗物大图路径对 (modelEntry, resourcePath)。
	/// </summary>
	protected virtual IEnumerable<(string modelEntry, string resourcePath)> RelicBigIconRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 力量小图标注册 (modelEntry, resourcePath)。
	/// </summary>
	protected virtual IEnumerable<(string modelEntry, string resourcePath)> PowerIconRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 力量大图标注册 (modelEntry, resourcePath)。
	/// </summary>
	protected virtual IEnumerable<(string modelEntry, string resourcePath)> PowerBigIconRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 需注册的药水纹理路径对 (textureKey, resourcePath)。
	/// </summary>
	protected virtual IEnumerable<(string textureKey, string resourcePath)> PotionPortraitRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 需注册的怪物纹理路径对 (textureKey, resourcePath)。
	/// </summary>
	protected virtual IEnumerable<(string textureKey, string resourcePath)> MonsterPortraitRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 需注册的 Boss 纹理路径对 (textureKey, resourcePath)。
	/// </summary>
	protected virtual IEnumerable<(string textureKey, string resourcePath)> BossPortraitRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 需注册的 UI 纹理路径对 (textureKey, resourcePath)。
	/// </summary>
	protected virtual IEnumerable<(string textureKey, string resourcePath)> UiTextureRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 需注册的背景纹理路径对 (textureKey, resourcePath)。
	/// </summary>
	protected virtual IEnumerable<(string textureKey, string resourcePath)> BackgroundTextureRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 需注册的其他纹理路径对 (textureKey, resourcePath)。
	/// </summary>
	protected virtual IEnumerable<(string textureKey, string resourcePath)> MiscTextureRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 初始化后阶段注册（可用于依赖前置注册结果的收尾逻辑）。
	/// </summary>
	protected virtual IEnumerable<Action> PostInitializationRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// RunStarted 阶段注册，先于 OnRunStarted 执行。
	/// </summary>
	protected virtual IEnumerable<Action<RunState>> RunStartedRegistrations
	{
		get
		{
			yield break;
		}
	}

	/// <summary>
	/// 子类初始化入口（在 Mixin 注册前执行）。
	/// </summary>
	protected virtual void OnInitialize()
	{
	}

	/// <summary>
	/// 子类 RunStarted 回调。
	/// </summary>
	protected virtual void OnRunStarted(RunState _)
	{
	}

	/// <summary>
	/// 帮助方法：构造模型注入注册项。
	/// </summary>
	protected static Action RegisterModelInjection<TModel>(string? logOwner = null)
		where TModel : AbstractModel
	{
		return () => _ = ReForge.Models.TryInjectModelOnce<TModel>(logOwner);
	}

	/// <summary>
	/// 帮助方法：构造卡牌模型注册项。
	/// </summary>
	protected static Action RegisterCard<TCard>(string? logOwner = null)
		where TCard : AbstractModel
	{
		return RegisterModelInjection<TCard>(logOwner);
	}

	/// <summary>
	/// 帮助方法：构造力量模型注册项。
	/// </summary>
	protected static Action RegisterPower<TPower>(string? logOwner = null)
		where TPower : AbstractModel
	{
		return RegisterModelInjection<TPower>(logOwner);
	}

	/// <summary>
	/// 帮助方法：构造遗物模型注册项。
	/// </summary>
	protected static Action RegisterRelic<TRelic>(string? logOwner = null)
		where TRelic : AbstractModel
	{
		return RegisterModelInjection<TRelic>(logOwner);
	}

	/// <summary>
	/// 帮助方法：构造药水模型注册项（占位）。
	/// </summary>
	protected static Action RegisterPotion<TPotion>(string? logOwner = null)
		where TPotion : AbstractModel
	{
		return RegisterModelInjection<TPotion>(logOwner);
	}

	/// <summary>
	/// 帮助方法：构造怪物模型注册项（占位）。
	/// </summary>
	protected static Action RegisterMonster<TMonster>(string? logOwner = null)
		where TMonster : AbstractModel
	{
		return RegisterModelInjection<TMonster>(logOwner);
	}

	/// <summary>
	/// 帮助方法：构造 Boss 模型注册项（占位）。
	/// </summary>
	protected static Action RegisterBoss<TBoss>(string? logOwner = null)
		where TBoss : AbstractModel
	{
		return RegisterModelInjection<TBoss>(logOwner);
	}

	/// <summary>
	/// 帮助方法：构造模型池注册项。
	/// </summary>
	protected static Action RegisterModelPool<TPool, TModel>(string? logOwner = null)
		where TPool : AbstractModel, IPoolModel
		where TModel : AbstractModel
	{
		return () => _ = ReForge.Models.TryAddModelToPoolOnce<TPool, TModel>(logOwner);
	}

	/// <summary>
	/// 帮助方法：构造卡池注册项。
	/// </summary>
	protected static Action RegisterCardPool<TPool, TCard>(string? logOwner = null)
		where TPool : AbstractModel, IPoolModel
		where TCard : AbstractModel
	{
		return RegisterModelPool<TPool, TCard>(logOwner);
	}

	/// <summary>
	/// 帮助方法：构造 Ascension 模型注册项。
	/// </summary>
	protected static Action RegisterAscensionModel<TAscensionModel>(string? logOwner = null)
		where TAscensionModel : ReForgeAscensionModel, new()
	{
		return () => _ = ReForge.Models.TryRegisterAscensionModelOnce<TAscensionModel>(logOwner);
	}

	/// <summary>
	/// 帮助方法：构造力量小图标注册项。
	/// </summary>
	protected static Action RegisterPowerIcon(string modelEntry, string resourcePath, string? logOwner = null)
	{
		return () => _ = ReForge.ModelTextures.TryRegisterPowerIconFromModResource(modelEntry, resourcePath, logOwner);
	}

	/// <summary>
	/// 帮助方法：构造力量大图标注册项。
	/// </summary>
	protected static Action RegisterPowerBigIcon(string modelEntry, string resourcePath, string? logOwner = null)
	{
		return () => _ = ReForge.ModelTextures.TryRegisterPowerBigIconFromModResource(modelEntry, resourcePath, logOwner);
	}

	/// <summary>
	/// 帮助方法：构造遗物图标注册项。
	/// </summary>
	protected static Action RegisterRelicIcon(string modelEntry, string resourcePath, string? logOwner = null)
	{
		return () => _ = ReForge.ModelTextures.TryRegisterRelicIconFromModResource(modelEntry, resourcePath, logOwner);
	}

	/// <summary>
	/// 帮助方法：构造遗物描边图标注册项。
	/// </summary>
	protected static Action RegisterRelicIconOutline(string modelEntry, string resourcePath, string? logOwner = null)
	{
		return () => _ = ReForge.ModelTextures.TryRegisterRelicIconOutlineFromModResource(modelEntry, resourcePath, logOwner);
	}

	/// <summary>
	/// 帮助方法：构造遗物大图注册项。
	/// </summary>
	protected static Action RegisterRelicBigIcon(string modelEntry, string resourcePath, string? logOwner = null)
	{
		return () => _ = ReForge.ModelTextures.TryRegisterRelicBigIconFromModResource(modelEntry, resourcePath, logOwner);
	}

	/// <summary>
	/// 帮助方法：构造通用命名纹理注册项。
	/// </summary>
	protected static Action RegisterNamedTexture(string textureKey, string resourcePath, string? logOwner = null)
	{
		return () => _ = ReForge.ModelTextures.TryRegisterNamedTextureFromModResource(textureKey, resourcePath, logOwner);
	}

	/// <summary>
	/// 供 ModMain 的静态 Initialize 调用。
	/// </summary>
	protected static void Bootstrap<TMod>() where TMod : ReForgeModBase, new()
	{
		if (!ReForgeModBootstrapStateStore.TryBegin<TMod>(() => new TMod(), out ReForgeModBase? instance))
		{
			return;
		}

		ReForgeModBase activeInstance = instance!;
		try
		{
			activeInstance.InitializeInternal();
			ReForgeModBootstrapStateStore.MarkInitialized<TMod>();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[{activeInstance.ModId}] initialize failed. {ex}");
		}
		finally
		{
			ReForgeModBootstrapStateStore.EndAttempt<TMod>();
		}
	}

	private void InitializeInternal()
	{
		GD.Print($"[{ModId}] registration pipeline start.");

		if (EnableRunStartedHook)
		{
			bool attached = ReForge.Mods.TryHookRunStartedWithRetry(HandleRunStarted, ModId);
			GD.Print($"[{ModId}] run-started hook requested. attachedImmediately={attached}.");
		}

		ExecutePhase(PreInitializationRegistrations, "pre-initialize");
		OnInitialize();
		ExecutePhase(InitializationRegistrations, "initialize");
		ExecutePhase(AscensionRegistrations, "ascension");
		ExecutePhase(CardRegistrations, "card");
		ExecutePhase(PowerRegistrations, "power");
		ExecutePhase(RelicRegistrations, "relic");
		ExecutePhase(PotionRegistrations, "potion");
		ExecutePhase(MonsterRegistrations, "monster");
		ExecutePhase(BossRegistrations, "boss");
		ExecutePhase(CardPoolRegistrations, "card-pool");
		ExecutePhase(EventRegistrations, "event");
		ExecutePhase(BattleEventRegistrations, "battle-event");
		ExecutePhase(AncientEventRegistrations, "ancient-event");
		ExecutePhase(ModelInjectionRegistrations, "model-injection");
		ExecutePhase(ModelPoolRegistrations, "model-pool");
		RegisterCardPortraits();
		RegisterRelicIcons();
		RegisterRelicIconOutlines();
		RegisterRelicBigIcons();
		RegisterPowerIcons();
		RegisterPowerBigIcons();
		RegisterPotionPortraits();
		RegisterMonsterPortraits();
		RegisterBossPortraits();
		RegisterUiTextures();
		RegisterBackgroundTextures();
		RegisterMiscTextures();
		ExecutePhase(PostInitializationRegistrations, "post-initialize");

		_ = ReForge.Mixins.TryRegister(GetType().Assembly, ModId, strictMode: StrictMixinMode);
		GD.Print($"[{ModId}] registration pipeline completed.");
		GD.Print($"[{ModId}] initialized.");
	}

	private void HandleRunStarted(RunState run)
	{
		GD.Print($"[{ModId}] HandleRunStarted begin. floor={run.TotalFloor}, actFloor={run.ActFloor}, players={run.Players.Count}.");
		foreach (Action<RunState> registration in RunStartedRegistrations)
		{
			registration(run);
		}

		OnRunStarted(run);
		GD.Print($"[{ModId}] HandleRunStarted completed.");
	}

	private void ExecutePhase(IEnumerable<Action> registrations, string phaseName)
	{
		List<Action> actions = new(registrations);
		GD.Print($"[{ModId}] phase '{phaseName}' start. registrations={actions.Count}.");

		for (int i = 0; i < actions.Count; i++)
		{
			Action registration = actions[i];
			try
			{
				registration();
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException($"[{ModId}] phase '{phaseName}' failed at index {i + 1}/{actions.Count}.", ex);
			}
		}

		GD.Print($"[{ModId}] phase '{phaseName}' completed. registrations={actions.Count}.");
	}

	private void RegisterCardPortraits()
	{
		foreach ((string modelEntry, string resourcePath) in CardPortraitRegistrations)
		{
			_ = ReForge.ModelTextures.TryRegisterCardPortraitFromModResource(modelEntry, resourcePath, ModId);
		}
	}

	private void RegisterPowerIcons()
	{
		foreach ((string modelEntry, string resourcePath) in PowerIconRegistrations)
		{
			_ = ReForge.ModelTextures.TryRegisterPowerIconFromModResource(modelEntry, resourcePath, ModId);
		}
	}

	private void RegisterRelicIcons()
	{
		foreach ((string modelEntry, string resourcePath) in RelicIconRegistrations)
		{
			_ = ReForge.ModelTextures.TryRegisterRelicIconFromModResource(modelEntry, resourcePath, ModId);
		}
	}

	private void RegisterRelicIconOutlines()
	{
		foreach ((string modelEntry, string resourcePath) in RelicIconOutlineRegistrations)
		{
			_ = ReForge.ModelTextures.TryRegisterRelicIconOutlineFromModResource(modelEntry, resourcePath, ModId);
		}
	}

	private void RegisterRelicBigIcons()
	{
		foreach ((string modelEntry, string resourcePath) in RelicBigIconRegistrations)
		{
			_ = ReForge.ModelTextures.TryRegisterRelicBigIconFromModResource(modelEntry, resourcePath, ModId);
		}
	}

	private void RegisterPowerBigIcons()
	{
		foreach ((string modelEntry, string resourcePath) in PowerBigIconRegistrations)
		{
			_ = ReForge.ModelTextures.TryRegisterPowerBigIconFromModResource(modelEntry, resourcePath, ModId);
		}
	}

	private void RegisterPotionPortraits()
	{
		RegisterNamedTextures(PotionPortraitRegistrations, "potion");
	}

	private void RegisterMonsterPortraits()
	{
		RegisterNamedTextures(MonsterPortraitRegistrations, "monster");
	}

	private void RegisterBossPortraits()
	{
		RegisterNamedTextures(BossPortraitRegistrations, "boss");
	}

	private void RegisterUiTextures()
	{
		RegisterNamedTextures(UiTextureRegistrations, "ui");
	}

	private void RegisterBackgroundTextures()
	{
		RegisterNamedTextures(BackgroundTextureRegistrations, "background");
	}

	private void RegisterMiscTextures()
	{
		RegisterNamedTextures(MiscTextureRegistrations, "misc");
	}

	private void RegisterNamedTextures(IEnumerable<(string textureKey, string resourcePath)> registrations, string category)
	{
		foreach ((string textureKey, string resourcePath) in registrations)
		{
			string namespacedKey = $"{category}:{textureKey}";
			_ = ReForge.ModelTextures.TryRegisterNamedTextureFromModResource(namespacedKey, resourcePath, ModId);
		}
	}

}
