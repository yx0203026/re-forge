#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;

namespace ReForgeFramework.EventWheel.Patches;

/// <summary>
/// 将 OptionIndexChosenMessage 中 optionIndex 位宽从 4-bit 扩展到 8-bit。
/// 默认关闭：仅在设置开关启用时才会应用补丁，避免影响普通联机兼容性。
/// </summary>
[HarmonyPatch]
internal static class OptionIndexChosenMessageBitWidthPatch
{
	private const int OldBitWidth = 4;
	private const int NewBitWidth = 8;

	private static readonly string[] MessageTypeCandidates =
	{
		"MegaCrit.Sts2.Core.Multiplayer.Game.Messages.OptionIndexChosenMessage",
		"MegaCrit.Sts2.Core.Multiplayer.Messages.OptionIndexChosenMessage",
		"OptionIndexChosenMessage"
	};

	private static readonly string[] CandidateMethodNames =
	{
		"Serialize",
		"Deserialize",
		"Write",
		"Read",
		"Encode",
		"Decode"
	};

	[HarmonyPrepare]
	private static bool Prepare()
	{
		if (!global::ReForge.IsOptionIndex8BitPatchEnabled())
		{
			GD.Print("[ReForge.OptionIndexBits] patch disabled by settings.");
			return false;
		}

		Type? messageType = ResolveMessageType();
		if (messageType == null)
		{
			GD.PrintErr("[ReForge.OptionIndexBits] OptionIndexChosenMessage type not found, patch skipped.");
			return false;
		}

		GD.Print($"[ReForge.OptionIndexBits] prepare ok. type='{messageType.FullName}', bitWidth={NewBitWidth}.");
		return true;
	}

	[HarmonyTargetMethods]
	private static IEnumerable<MethodBase> TargetMethods()
	{
		Type? messageType = ResolveMessageType();
		if (messageType == null)
		{
			yield break;
		}

		HashSet<MethodBase> dedupe = new();
		for (int i = 0; i < CandidateMethodNames.Length; i++)
		{
			string methodName = CandidateMethodNames[i];

			MethodInfo? declared = AccessTools.DeclaredMethod(messageType, methodName);
			if (declared != null && dedupe.Add(declared))
			{
				yield return declared;
			}

			MethodInfo[] all = AccessTools.GetDeclaredMethods(messageType)
				.Where(m => StringComparer.Ordinal.Equals(m.Name, methodName))
				.ToArray();

			for (int j = 0; j < all.Length; j++)
			{
				if (dedupe.Add(all[j]))
				{
					yield return all[j];
				}
			}
		}
	}

	[HarmonyTranspiler]
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
	{
		List<CodeInstruction> codes = instructions.ToList();
		int patchedCount = 0;

		for (int i = 0; i < codes.Count; i++)
		{
			if (!IsLdcI4(codes[i], OldBitWidth))
			{
				continue;
			}

			int nextIndex = FindNextExecutableInstruction(codes, i + 1);
			if (nextIndex < 0)
			{
				continue;
			}

			if (!IsBitWidthConsumingCall(codes[nextIndex]))
			{
				continue;
			}

			codes[i] = new CodeInstruction(OpCodes.Ldc_I4, NewBitWidth);
			patchedCount++;
		}

		if (patchedCount > 0)
		{
			GD.Print($"[ReForge.OptionIndexBits] transpiled '{original.DeclaringType?.FullName}.{original.Name}', patched={patchedCount}, newBitWidth={NewBitWidth}.");
		}
		else
		{
			GD.PrintErr($"[ReForge.OptionIndexBits] no patch point found in '{original.DeclaringType?.FullName}.{original.Name}'.");
		}

		return codes;
	}

	private static Type? ResolveMessageType()
	{
		for (int i = 0; i < MessageTypeCandidates.Length; i++)
		{
			Type? t = AccessTools.TypeByName(MessageTypeCandidates[i]);
			if (t != null)
			{
				return t;
			}
		}

		return null;
	}

	private static bool IsLdcI4(CodeInstruction code, int value)
	{
		if (code.opcode == OpCodes.Ldc_I4 && code.operand is int i)
		{
			return i == value;
		}

		return value switch
		{
			-1 => code.opcode == OpCodes.Ldc_I4_M1,
			0 => code.opcode == OpCodes.Ldc_I4_0,
			1 => code.opcode == OpCodes.Ldc_I4_1,
			2 => code.opcode == OpCodes.Ldc_I4_2,
			3 => code.opcode == OpCodes.Ldc_I4_3,
			4 => code.opcode == OpCodes.Ldc_I4_4,
			5 => code.opcode == OpCodes.Ldc_I4_5,
			6 => code.opcode == OpCodes.Ldc_I4_6,
			7 => code.opcode == OpCodes.Ldc_I4_7,
			8 => code.opcode == OpCodes.Ldc_I4_8,
			_ => false
		};
	}

	private static int FindNextExecutableInstruction(List<CodeInstruction> codes, int startIndex)
	{
		for (int i = startIndex; i < codes.Count; i++)
		{
			OpCode op = codes[i].opcode;
			if (op == OpCodes.Nop)
			{
				continue;
			}

			return i;
		}

		return -1;
	}

	private static bool IsBitWidthConsumingCall(CodeInstruction code)
	{
		if (code.opcode != OpCodes.Call && code.opcode != OpCodes.Callvirt)
		{
			return false;
		}

		if (code.operand is not MethodInfo mi)
		{
			return false;
		}

		if (!LooksLikeBitIoMethodName(mi.Name))
		{
			return false;
		}

		ParameterInfo[] ps = mi.GetParameters();
		if (ps.Length == 0)
		{
			return false;
		}

		return ps[^1].ParameterType == typeof(int);
	}

	private static bool LooksLikeBitIoMethodName(string methodName)
	{
		return methodName.Contains("WriteUInt", StringComparison.Ordinal)
			|| methodName.Contains("ReadUInt", StringComparison.Ordinal)
			|| methodName.Contains("WriteInt", StringComparison.Ordinal)
			|| methodName.Contains("ReadInt", StringComparison.Ordinal)
			|| methodName.Contains("WriteBits", StringComparison.Ordinal)
			|| methodName.Contains("ReadBits", StringComparison.Ordinal);
	}
}
