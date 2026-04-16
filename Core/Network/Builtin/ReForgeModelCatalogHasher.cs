#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;

namespace ReForgeFramework.Networking;

internal static class ReForgeModelCatalogHasher
{
	public static ReForgeModelCatalogHashSnapshot Compute()
	{
		try
		{
			List<(Type type, Mod? mod)> modelTypes = new();
			foreach (Type type in AbstractModelSubtypes.All)
			{
				modelTypes.Add((type, null));
			}

			foreach (Mod mod in ModManager.Mods)
			{
				if (mod.state != ModLoadState.Loaded || mod.assembly == null)
				{
					continue;
				}

				foreach (Type type in ReflectionHelper.GetSubtypesFromAssembly(mod.assembly, typeof(AbstractModel)))
				{
					modelTypes.Add((type, mod));
				}
			}

			modelTypes.Sort((left, right) =>
			{
				int byName = string.CompareOrdinal(left.type.Name, right.type.Name);
				if (byName != 0)
				{
					return byName;
				}

				if (left.mod == null && right.mod != null)
				{
					return -1;
				}

				if (left.mod != null && right.mod == null)
				{
					return 1;
				}

				if (left.mod == null && right.mod == null)
				{
					return 0;
				}

				string leftId = left.mod?.manifest?.id ?? string.Empty;
				string rightId = right.mod?.manifest?.id ?? string.Empty;
				return string.CompareOrdinal(leftId, rightId);
			});

			HashSet<string> categories = new(StringComparer.Ordinal)
			{
				ModelId.none.Category,
			};

			HashSet<string> entries = new(StringComparer.Ordinal)
			{
				ModelId.none.Entry,
			};

			foreach ((Type type, _) in modelTypes)
			{
				ModelId id = ModelDb.GetId(type);
				categories.Add(id.Category);
				entries.Add(id.Entry);
			}

			List<string> sortedCategories = categories.OrderBy(x => x, StringComparer.Ordinal).ToList();
			List<string> sortedEntries = entries.OrderBy(x => x, StringComparer.Ordinal).ToList();

			byte[] payload = BuildPayload(sortedCategories, sortedEntries);
			byte[] digest = SHA256.HashData(payload);
			uint hash = BinaryPrimitives.ReadUInt32LittleEndian(digest.AsSpan(0, 4));

			return new ReForgeModelCatalogHashSnapshot(
				hash,
				sortedCategories.Count,
				sortedEntries.Count
			);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ReForge.Network] Failed to compute model catalog hash. {ex}");
			return new ReForgeModelCatalogHashSnapshot(0, 0, 0);
		}
	}

	private static byte[] BuildPayload(IReadOnlyList<string> categories, IReadOnlyList<string> entries)
	{
		using MemoryStream stream = new();
		using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);

		writer.Write(categories.Count);
		for (int i = 0; i < categories.Count; i++)
		{
			writer.Write(categories[i]);
		}

		writer.Write(entries.Count);
		for (int i = 0; i < entries.Count; i++)
		{
			writer.Write(entries[i]);
		}

		writer.Flush();
		return stream.ToArray();
	}
}
