#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using ReForgeFramework.Networking;

namespace ReForgeFramework.Api.Ui;

/// <summary>
/// 负面 Power 选择面板构建器：支持逐条添加 Debuff（含层数），并弹出统一选择 UI。
/// </summary>
public sealed class DebuffSelectionPanelBuilder
{
	private static readonly object NetworkSyncLock = new();
	private static bool _networkSyncHandlerRegistered;
	private static readonly MessageHandlerDelegate<ReForgeDebuffSelectionSyncMessage> DebuffSelectionSyncHandler = OnDebuffSelectionSynced;

	private readonly List<DebuffSelectionEntry> _entries = new();
	private LocString? _title;
	private int _minSelect = 1;
	private int _maxSelect = 1;
	private bool _cancelable = true;
	private bool _randomModeEnabled;
	private int _maxDisplayCount;

	internal static void InitializeNetworkSyncRuntime()
	{
		if (_networkSyncHandlerRegistered)
		{
			return;
		}

		lock (NetworkSyncLock)
		{
			if (_networkSyncHandlerRegistered)
			{
				return;
			}

			try
			{
				ReForge.Network.RegisterHandler(DebuffSelectionSyncHandler);
				_networkSyncHandlerRegistered = true;
				GD.Print("[ReForge.UI.DebuffSelection] Network sync runtime registered.");
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ReForge.UI.DebuffSelection] Network sync registration failed. {ex.GetType().Name}: {ex.Message}");
			}
		}
	}

	/// <summary>
	/// 设置弹窗标题（本地化文本）。
	/// </summary>
	public DebuffSelectionPanelBuilder WithTitle(LocString title)
	{
		_title = title;
		return this;
	}

	/// <summary>
	/// 通过本地化表与键设置弹窗标题。
	/// </summary>
	public DebuffSelectionPanelBuilder WithTitle(string table, string key)
	{
		if (string.IsNullOrWhiteSpace(table))
		{
			throw new ArgumentException("Localization table cannot be null or whitespace.", nameof(table));
		}

		if (string.IsNullOrWhiteSpace(key))
		{
			throw new ArgumentException("Localization key cannot be null or whitespace.", nameof(key));
		}

		_title = new LocString(table, key);
		return this;
	}

	/// <summary>
	/// 设置可选择数量区间（用于单选/多选）。
	/// </summary>
	public DebuffSelectionPanelBuilder WithSelectCount(int minSelect, int maxSelect)
	{
		if (minSelect < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(minSelect), minSelect, "minSelect must be >= 0.");
		}

		if (maxSelect <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maxSelect), maxSelect, "maxSelect must be > 0.");
		}

		if (minSelect > maxSelect)
		{
			throw new ArgumentException("minSelect must be <= maxSelect.");
		}

		_minSelect = minSelect;
		_maxSelect = maxSelect;
		return this;
	}

	/// <summary>
	/// 设置是否允许取消。
	/// </summary>
	public DebuffSelectionPanelBuilder WithCancelable(bool cancelable)
	{
		_cancelable = cancelable;
		return this;
	}

	/// <summary>
	/// 设置是否启用随机模式。
	/// 启用后，若同时设置了有效的最大展示数量，将从已注册 Debuff 中随机抽样展示。
	/// </summary>
	public DebuffSelectionPanelBuilder WithRandomMode(bool enabled = true)
	{
		_randomModeEnabled = enabled;
		return this;
	}

	/// <summary>
	/// 设置最大展示数量。
	/// 传入 0 表示不限制（保持全部展示）。
	/// </summary>
	public DebuffSelectionPanelBuilder WithMaxDisplayCount(int maxDisplayCount)
	{
		if (maxDisplayCount < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maxDisplayCount), maxDisplayCount, "maxDisplayCount must be >= 0.");
		}

		_maxDisplayCount = maxDisplayCount;
		return this;
	}

	/// <summary>
	/// 添加一个 Debuff 候选项。
	/// </summary>
	public DebuffSelectionPanelBuilder AddDebuff(PowerModel debuff, int amount = 1)
	{
		ArgumentNullException.ThrowIfNull(debuff);
		if (amount <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(amount), amount, "amount must be > 0.");
		}

		EnsureDebuff(debuff);

		_entries.Add(new DebuffSelectionEntry(debuff, amount));
		return this;
	}

	/// <summary>
	/// 按 Debuff 类型添加候选项。
	/// </summary>
	public DebuffSelectionPanelBuilder AddDebuff<TPower>(int amount = 1) where TPower : PowerModel
	{
		return AddDebuff(ModelDb.Power<TPower>(), amount);
	}

	/// <summary>
	/// 按完整 ModelId 添加候选项。
	/// </summary>
	public DebuffSelectionPanelBuilder AddDebuff(ModelId powerId, int amount = 1)
	{
		PowerModel? power = ModelDb.GetByIdOrNull<PowerModel>(powerId);
		if (power == null)
		{
			throw new InvalidOperationException($"Power model '{powerId}' was not found in ModelDb.");
		}

		return AddDebuff(power, amount);
	}

	/// <summary>
	/// 按 Entry 名称添加候选项（大小写不敏感）。
	/// </summary>
	public DebuffSelectionPanelBuilder AddDebuff(string powerEntry, int amount = 1)
	{
		if (string.IsNullOrWhiteSpace(powerEntry))
		{
			throw new ArgumentException("powerEntry cannot be null or whitespace.", nameof(powerEntry));
		}

		ModelId id = new(ModelId.SlugifyCategory<PowerModel>(), powerEntry.ToUpperInvariant());
		return AddDebuff(id, amount);
	}

	/// <summary>
	/// 构建并显示“选择负面 Buff”弹窗，返回所选结果。
	/// </summary>
	public async Task<IReadOnlyList<DebuffSelectionResult>> ShowAsync()
	{
		if (_entries.Count == 0)
		{
			throw new InvalidOperationException("No debuff entries were provided. Call AddDebuff before ShowAsync.");
		}

		List<DebuffSelectionEntry> entriesToShow = ResolveEntriesForDisplay();

		if (_minSelect > entriesToShow.Count)
		{
			throw new InvalidOperationException($"minSelect ({_minSelect}) cannot be greater than entry count ({entriesToShow.Count}).");
		}

		int resolvedMax = Math.Min(_maxSelect, entriesToShow.Count);
		LocString resolvedTitle = _title ?? new LocString("gameplay_ui", "REFORGE.UI.DEBUFF_SELECTION.TITLE");

		await ReForge.LifecycleSafety.WaitForOverlayReadyAsync();
		ReForge.LifecycleSafety.EnsureOverlayReady();

		IReadOnlyList<DebuffSelectionEntry> selected = await DebuffSelectionOverlayScreen.ShowAndWait(
			entriesToShow,
			resolvedTitle,
			_minSelect,
			resolvedMax,
			_cancelable);

		return selected
			.Select(static e => new DebuffSelectionResult(e.Debuff.Id, e.Debuff, e.Amount))
			.ToList();
	}

	/// <summary>
	/// 显示并将选择结果应用到目标生物。
	/// </summary>
	public async Task<IReadOnlyList<DebuffSelectionResult>> ShowAndApplyAsync(
		Creature target,
		Creature? applier = null,
		CardModel? cardSource = null,
		bool silent = false)
	{
		ArgumentNullException.ThrowIfNull(target);
		InitializeNetworkSyncRuntime();

		IReadOnlyList<DebuffSelectionResult> selected = await ShowAsync();
		if (selected.Count == 0)
		{
			return selected;
		}

		if (!ReForge.Network.IsConnected || !target.IsPlayer || target.Player == null)
		{
			await ApplySelectionToTargetAsync(selected, target, applier, cardSource, silent);
			return selected;
		}

		ReForgeDebuffSelectionSyncMessage request = BuildSyncMessage(
			target,
			applier,
			selected,
			silent,
			isAuthoritativeBroadcast: false);

		if (ReForge.Network.IsHostAuthority)
		{
			await ApplySelectionToTargetAsync(selected, target, applier, cardSource, silent);
			BroadcastSelectionFromHost(request);
			return selected;
		}

		ulong hostPeerId = ReForge.Network.HostPeerId;
		if (hostPeerId == 0)
		{
			GD.PrintErr("[ReForge.UI.DebuffSelection] Host peer id unavailable. Selection request was not sent.");
			return selected;
		}

		ReForge.Network.SendTo(hostPeerId, request);
		GD.Print($"[ReForge.UI.DebuffSelection] Client request sent. items={request.Items.Count}.");
		return selected;
	}

	private static ReForgeDebuffSelectionSyncMessage BuildSyncMessage(
		Creature target,
		Creature? applier,
		IReadOnlyList<DebuffSelectionResult> selected,
		bool silent,
		bool isAuthoritativeBroadcast)
	{
		ReForgeDebuffSelectionSyncMessage message = new()
		{
			TargetPlayerNetId = target.Player!.NetId,
			ApplierPlayerNetId = applier?.Player?.NetId ?? 0,
			Silent = silent,
			IsAuthoritativeBroadcast = isAuthoritativeBroadcast
		};

		for (int i = 0; i < selected.Count; i++)
		{
			DebuffSelectionResult item = selected[i];
			message.Items.Add(new ReForgeDebuffSelectionSyncItem
			{
				PowerCategory = item.DebuffId.Category,
				PowerEntry = item.DebuffId.Entry,
				Amount = item.Amount
			});
		}

		return message;
	}

	private static void BroadcastSelectionFromHost(ReForgeDebuffSelectionSyncMessage request)
	{
		ReForgeDebuffSelectionSyncMessage broadcast = request.CloneForBroadcast();
		ReForge.Network.Send(broadcast);
		GD.Print($"[ReForge.UI.DebuffSelection] Host broadcast sent. items={broadcast.Items.Count}.");
	}

	private static void OnDebuffSelectionSynced(ReForgeDebuffSelectionSyncMessage message, ulong senderId)
	{
		_ = OnDebuffSelectionSyncedAsync(message, senderId);
	}

	private static async Task OnDebuffSelectionSyncedAsync(ReForgeDebuffSelectionSyncMessage message, ulong senderId)
	{
		try
		{
			if (!message.IsAuthoritativeBroadcast)
			{
				if (!ReForge.Network.IsHostAuthority)
				{
					return;
				}

				if (senderId == ReForge.Network.LocalPeerId)
				{
					return;
				}

				if (!TryResolveTargetAndApplier(message, out Creature requestTarget, out Creature? requestApplier))
				{
					return;
				}

				IReadOnlyList<DebuffSelectionResult> appliedOnHost = await ApplySelectionMessageToTargetAsync(message, requestTarget, requestApplier);
				if (appliedOnHost.Count > 0)
				{
					BroadcastSelectionFromHost(message);
				}

				return;
			}

			if (ReForge.Network.IsHostAuthority)
			{
				// 主机已在请求阶段权威执行，避免广播回环二次应用。
				return;
			}

			if (!TryResolveTargetAndApplier(message, out Creature target, out Creature? applier))
			{
				return;
			}

			IReadOnlyList<DebuffSelectionResult> appliedOnClient = await ApplySelectionMessageToTargetAsync(message, target, applier);
			GD.Print($"[ReForge.UI.DebuffSelection] Client applied authoritative broadcast. applied={appliedOnClient.Count}.");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.UI.DebuffSelection] Sync handling failed. {ex}");
		}
	}

	private static bool TryResolveTargetAndApplier(
		ReForgeDebuffSelectionSyncMessage message,
		out Creature target,
		out Creature? applier)
	{
		target = null!;
		applier = null;

		RunState? runState = RunManager.Instance?.DebugOnlyGetState();
		if (runState == null)
		{
			GD.PrintErr("[ReForge.UI.DebuffSelection] Sync skipped: RunState unavailable.");
			return false;
		}

		var targetPlayer = runState.GetPlayer(message.TargetPlayerNetId);
		if (targetPlayer == null)
		{
			GD.PrintErr($"[ReForge.UI.DebuffSelection] Sync skipped: target player not found. netId={message.TargetPlayerNetId}.");
			return false;
		}

		target = targetPlayer.Creature;
		if (message.ApplierPlayerNetId != 0)
		{
			applier = runState.GetPlayer(message.ApplierPlayerNetId)?.Creature;
		}

		return true;
	}

	private static async Task<IReadOnlyList<DebuffSelectionResult>> ApplySelectionMessageToTargetAsync(
		ReForgeDebuffSelectionSyncMessage message,
		Creature target,
		Creature? applier)
	{
		List<DebuffSelectionResult> applied = new(message.Items.Count);
		for (int i = 0; i < message.Items.Count; i++)
		{
			ReForgeDebuffSelectionSyncItem item = message.Items[i];
			if (item.Amount <= 0 || string.IsNullOrWhiteSpace(item.PowerCategory) || string.IsNullOrWhiteSpace(item.PowerEntry))
			{
				continue;
			}

			ModelId powerId = new(item.PowerCategory, item.PowerEntry);
			PowerModel? debuff = ModelDb.GetByIdOrNull<PowerModel>(powerId);
			if (debuff == null)
			{
				GD.PrintErr($"[ReForge.UI.DebuffSelection] Sync skipped: debuff not found. powerId={powerId}.");
				continue;
			}

			EnsureDebuff(debuff);
			PowerModel mutablePower = debuff.ToMutable();
			await PowerCmd.Apply(mutablePower, target, item.Amount, applier, cardSource: null, message.Silent);
			applied.Add(new DebuffSelectionResult(powerId, debuff, item.Amount));
		}

		return applied;
	}

	private static async Task ApplySelectionToTargetAsync(
		IReadOnlyList<DebuffSelectionResult> selected,
		Creature target,
		Creature? applier,
		CardModel? cardSource,
		bool silent)
	{
		foreach (DebuffSelectionResult result in selected)
		{
			PowerModel mutablePower = result.Debuff.ToMutable();
			await PowerCmd.Apply(mutablePower, target, result.Amount, applier, cardSource, silent);
		}
	}

	private static void EnsureDebuff(PowerModel power)
	{
		if (power.TypeForCurrentAmount != PowerType.Debuff)
		{
			throw new InvalidOperationException(
				$"Power '{power.Id}' is not Debuff (actual: {power.TypeForCurrentAmount}).");
		}
	}

	private List<DebuffSelectionEntry> ResolveEntriesForDisplay()
	{
		if (!_randomModeEnabled || _maxDisplayCount <= 0 || _entries.Count <= _maxDisplayCount)
		{
			return new List<DebuffSelectionEntry>(_entries);
		}

		// Fisher-Yates 洗牌后截断，保证每个候选有均等概率进入展示池。
		List<DebuffSelectionEntry> shuffled = new(_entries);
		Random random = new();
		for (int i = shuffled.Count - 1; i > 0; i--)
		{
			int swapIndex = random.Next(i + 1);
			(shuffled[i], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[i]);
		}

		return shuffled.Take(_maxDisplayCount).ToList();
	}
}

/// <summary>
/// Debuff 选择结果。
/// </summary>
public sealed record DebuffSelectionResult(ModelId DebuffId, PowerModel Debuff, int Amount);

internal sealed record DebuffSelectionEntry(PowerModel Debuff, int Amount);
