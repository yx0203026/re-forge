#nullable enable

using System;
using System.IO;
using System.Linq;

namespace ReForgeFramework.ModLoading;

public sealed class ReForgeModFileIo
{
	public bool DirectoryExists(string path)
	{
		return Directory.Exists(path);
	}

	public bool FileExists(string path)
	{
		return File.Exists(path);
	}

	public string[] GetFilesAt(string path)
	{
		if (!Directory.Exists(path))
		{
			return Array.Empty<string>();
		}

		return Directory.GetFiles(path).Select(Path.GetFileName).Where(name => name != null).ToArray()!;
	}

	public string[] GetDirectoriesAt(string path)
	{
		if (!Directory.Exists(path))
		{
			return Array.Empty<string>();
		}

		return Directory.GetDirectories(path).Select(Path.GetFileName).Where(name => name != null).ToArray()!;
	}

	public string ReadAllText(string path)
	{
		return File.ReadAllText(path);
	}
}
