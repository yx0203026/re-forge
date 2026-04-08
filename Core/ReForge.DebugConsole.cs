#nullable enable

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Godot;

public static partial class ReForge
{
	private static class DebugConsole
	{
		private static bool _opened;

		public static void EnsureOpened()
		{
			if (_opened)
			{
				return;
			}

			if (!OperatingSystem.IsWindows())
			{
				GD.Print("[ReForge.Debug] Debug console is only supported on Windows in current implementation.");
				return;
			}

			try
			{
				if (GetConsoleWindow() == IntPtr.Zero)
				{
					if (!AllocConsole())
					{
						int code = Marshal.GetLastWin32Error();
						GD.PrintErr($"[ReForge.Debug] AllocConsole failed. Win32Error={code}.");
						return;
					}
				}

				Console.OutputEncoding = Encoding.UTF8;
				Console.InputEncoding = Encoding.UTF8;

				try
				{
					StreamWriter stdout = new(Console.OpenStandardOutput())
					{
						AutoFlush = true,
					};
					Console.SetOut(stdout);
				}
				catch
				{
					// 若标准输出绑定失败，至少保证控制台存在。
				}

				_opened = true;
				Console.WriteLine("[ReForge] Debug console enabled.");
				GD.Print("[ReForge.Debug] Debug console opened.");
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ReForge.Debug] Failed to open debug console. {ex}");
			}
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool AllocConsole();

		[DllImport("kernel32.dll")]
		private static extern IntPtr GetConsoleWindow();
	}
}
