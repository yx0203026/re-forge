#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Runs;
using Sts2Player = MegaCrit.Sts2.Core.Entities.Players.Player;

/// <summary>
/// 解锁当前角色的下一阶段（或指定阶段）。
/// </summary>
public sealed class NextAscensionConsoleCmd : AbstractConsoleCmd
{
	public override string CmdName => "nextascension";

	public override string Args => "[ascension:int]";

	public override string Description => "Unlock next ascension for current character, or unlock to specified ascension (>10).";

	public override bool IsNetworked => false;

	public override CmdResult Process(Sts2Player? issuingPlayer, string[] args)
	{
		if (!TryParseOptionalAscension(args, out int? target, out string parseError))
		{
			return new CmdResult(success: false, parseError);
		}

		if (!TryResolveCurrentCharacterId(out ModelId characterId))
		{
			int fallback = UnlockAllCharacters(target);
			return new CmdResult(success: true, $"Current character not found. Applied to all characters, unlocked to A{fallback}.");
		}

		int current = ReForge.Ascension.GetMaxAscensionByCharacter(characterId);
		int next = Math.Clamp((target ?? (current + 1)), 11, ReForge.Ascension.MaxAscension);
		ReForge.Ascension.SetMaxAscensionByCharacter(characterId, next);
		ReForge.Ascension.SetPreferredAscensionByCharacter(characterId, next);

		int applied = ReForge.Ascension.GetMaxAscensionByCharacter(characterId);
		return new CmdResult(success: true, $"Character '{characterId.Entry}' unlocked to A{applied}.");
	}

	private static bool TryResolveCurrentCharacterId(out ModelId characterId)
	{
		characterId = ModelId.none;

		try
		{
			RunState? runState = RunManager.Instance.DebugOnlyGetState();
			if (runState != null)
			{
				Sts2Player? me = LocalContext.GetMe(runState);
				if (me != null && me.Character?.Id != null && me.Character.Id != ModelId.none && me.Character is not RandomCharacter)
				{
					characterId = me.Character.Id;
					return true;
				}
			}
		}
		catch
		{
		}

		return false;
	}

	private static int UnlockAllCharacters(int? targetAscension)
	{
		List<CharacterModel> characters = ModelDb.AllCharacters
			.Where(static c => c is not RandomCharacter)
			.ToList();

		if (characters.Count == 0)
		{
			return 10;
		}

		int currentMax = characters.Max(static c => ReForge.Ascension.GetMaxAscensionByCharacter(c.Id));
		int target = Math.Clamp((targetAscension ?? (currentMax + 1)), 11, ReForge.Ascension.MaxAscension);

		for (int i = 0; i < characters.Count; i++)
		{
			ModelId id = characters[i].Id;
			ReForge.Ascension.SetMaxAscensionByCharacter(id, target);
			ReForge.Ascension.SetPreferredAscensionByCharacter(id, target);
		}

		return target;
	}

	private static bool TryParseOptionalAscension(string[] args, out int? target, out string error)
	{
		target = null;
		error = string.Empty;

		if (args.Length == 0)
		{
			return true;
		}

		if (args.Length > 1)
		{
			error = "Too many arguments. Usage: nextascension [ascension:int].";
			return false;
		}

		if (!int.TryParse(args[0], out int parsed))
		{
			error = "Ascension must be an integer.";
			return false;
		}

		if (parsed <= 10)
		{
			error = "Ascension must be > 10.";
			return false;
		}

		target = parsed;
		return true;
	}
}

/// <summary>
/// 解锁多人模式的下一阶段（或指定阶段）。
/// </summary>
public sealed class NextMpAscensionConsoleCmd : AbstractConsoleCmd
{
	public override string CmdName => "nextmpascension";

	public override string Args => "[ascension:int]";

	public override string Description => "Unlock next multiplayer ascension, or unlock to specified ascension (>10).";

	public override bool IsNetworked => false;

	public override CmdResult Process(Sts2Player? issuingPlayer, string[] args)
	{
		if (!TryParseOptionalAscension(args, out int? target, out string parseError))
		{
			return new CmdResult(success: false, parseError);
		}

		int current = ReForge.Ascension.GetMultiplayerUnlockedMaxAscension();
		int next = Math.Clamp((target ?? (current + 1)), 11, ReForge.Ascension.MaxAscension);
		int applied = ReForge.Ascension.UnlockMultiplayerAscensionTo(next);
		return new CmdResult(success: true, $"Multiplayer unlocked to A{applied}.");
	}

	private static bool TryParseOptionalAscension(string[] args, out int? target, out string error)
	{
		target = null;
		error = string.Empty;

		if (args.Length == 0)
		{
			return true;
		}

		if (args.Length > 1)
		{
			error = "Too many arguments. Usage: nextmpascension [ascension:int].";
			return false;
		}

		if (!int.TryParse(args[0], out int parsed))
		{
			error = "Ascension must be an integer.";
			return false;
		}

		if (parsed <= 10)
		{
			error = "Ascension must be > 10.";
			return false;
		}

		target = parsed;
		return true;
	}
}

/// <summary>
/// 解锁所有角色到下一阶段（或指定阶段）。
/// </summary>
public sealed class NextAscensionAllConsoleCmd : AbstractConsoleCmd
{
	public override string CmdName => "nextascensionall";

	public override string Args => "[ascension:int]";

	public override string Description => "Unlock next ascension for all characters, or unlock all to specified ascension (>10).";

	public override bool IsNetworked => false;

	public override CmdResult Process(Sts2Player? issuingPlayer, string[] args)
	{
		if (!TryParseOptionalAscension(args, out int? target, out string parseError))
		{
			return new CmdResult(success: false, parseError);
		}

		List<CharacterModel> characters = ModelDb.AllCharacters
			.Where(static c => c is not RandomCharacter)
			.ToList();

		if (characters.Count == 0)
		{
			return new CmdResult(success: false, "No playable characters found.");
		}

		int currentMax = characters.Max(static c => ReForge.Ascension.GetMaxAscensionByCharacter(c.Id));
		int next = Math.Clamp((target ?? (currentMax + 1)), 11, ReForge.Ascension.MaxAscension);

		for (int i = 0; i < characters.Count; i++)
		{
			ModelId id = characters[i].Id;
			ReForge.Ascension.SetMaxAscensionByCharacter(id, next);
			ReForge.Ascension.SetPreferredAscensionByCharacter(id, next);
		}

		return new CmdResult(success: true, $"All characters unlocked to A{next}.");
	}

	private static bool TryParseOptionalAscension(string[] args, out int? target, out string error)
	{
		target = null;
		error = string.Empty;

		if (args.Length == 0)
		{
			return true;
		}

		if (args.Length > 1)
		{
			error = "Too many arguments. Usage: nextascensionall [ascension:int].";
			return false;
		}

		if (!int.TryParse(args[0], out int parsed))
		{
			error = "Ascension must be an integer.";
			return false;
		}

		if (parsed <= 10)
		{
			error = "Ascension must be > 10.";
			return false;
		}

		target = parsed;
		return true;
	}
}
