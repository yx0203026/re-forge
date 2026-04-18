#nullable enable

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace ReForgeFramework.EventWheel;

/// <summary>
/// EventWheel 池化选择工具。
/// 提供稳定种子生成与加权索引选择，保证跨端一致性。
/// </summary>
internal static class EventWheelPoolSelection
{
	/// <summary>
	/// 按权重从集合中选择索引。
	/// </summary>
	public static int SelectWeightedIndex<T>(IReadOnlyList<T> items, string seed, Func<T, int> weightSelector)
	{
		if (items == null || items.Count == 0)
		{
			return -1;
		}

		if (items.Count == 1)
		{
			return 0;
		}

		int totalWeight = 0;
		int[] weights = new int[items.Count];
		for (int i = 0; i < items.Count; i++)
		{
			int weight = weightSelector(items[i]);
			if (weight <= 0)
			{
				weight = 1;
			}

			weights[i] = weight;
			totalWeight += weight;
		}

		if (totalWeight <= 0)
		{
			return 0;
		}

		uint selector = HashToUInt(BuildSeed(seed));
		int roll = (int)(selector % (uint)totalWeight);
		int cumulative = 0;
		for (int i = 0; i < weights.Length; i++)
		{
			cumulative += weights[i];
			if (roll < cumulative)
			{
				return i;
			}
		}

		return items.Count - 1;
	}

	/// <summary>
	/// 使用分隔符拼接种子片段。
	/// </summary>
	public static string BuildSeed(params string?[] parts)
	{
		if (parts == null || parts.Length == 0)
		{
			return string.Empty;
		}

		StringBuilder builder = new();
		for (int i = 0; i < parts.Length; i++)
		{
			if (i > 0)
			{
				builder.Append('\u001f');
			}

			builder.Append(parts[i]?.Trim() ?? string.Empty);
		}

		return builder.ToString();
	}

	private static byte[] BuildSeed(string seed)
	{
		return Encoding.UTF8.GetBytes(seed ?? string.Empty);
	}

	private static uint HashToUInt(byte[] payload)
	{
		byte[] hash = SHA256.HashData(payload);
		if (hash.Length < 4)
		{
			return 0;
		}

		return BitConverter.ToUInt32(hash, 0);
	}
}