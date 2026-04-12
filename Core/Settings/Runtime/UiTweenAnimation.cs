#nullable enable

using Godot;

namespace ReForgeFramework.Settings.Runtime;

/// <summary>
/// 杞婚噺 Tween 杈呭姪锛氱粺涓€绠＄悊缂╂斁鍔ㄧ敾锛岄伩鍏嶅彔鍔犲啿绐併€?
/// </summary>
internal static class UiTweenAnimation
{
	private static readonly StringName MetaScaleTween = "__reforge_scale_tween";

	/// <summary>
	/// 瀵圭洰鏍囨帶浠舵墽琛岀缉鏀捐ˉ闂村姩鐢汇€?
	/// </summary>
	/// <param name="control">鐩爣鎺т欢銆?/param>
	/// <param name="targetScale">鐩爣缂╂斁銆?/param>
	/// <param name="duration">鍔ㄧ敾鏃堕暱锛堢锛夈€?/param>
	/// <param name="transitionType">缂撳姩杩囨浮绫诲瀷銆?/param>
	/// <param name="easeType">缂撳姩鏂瑰悜銆?/param>
	public static void TweenScale(
		Control control,
		Vector2 targetScale,
		float duration,
		Tween.TransitionType transitionType,
		Tween.EaseType easeType)
	{
		if (!GodotObject.IsInstanceValid(control))
		{
			return;
		}

		StopScaleTween(control);

		if (duration <= 0f)
		{
			control.Scale = targetScale;
			return;
		}

		Tween tween = control.CreateTween();
		tween.TweenProperty(control, "scale", targetScale, duration).SetTrans(transitionType).SetEase(easeType);
		control.SetMeta(MetaScaleTween, tween);
		tween.Connect(Tween.SignalName.Finished, Callable.From(() => CleanupTweenMeta(control, tween)));
	}

	private static void CleanupTweenMeta(Control control, Tween tween)
	{
		if (!GodotObject.IsInstanceValid(control) || !control.HasMeta(MetaScaleTween))
		{
			return;
		}

		GodotObject? currentTween = control.GetMeta(MetaScaleTween).AsGodotObject();
		if (currentTween == tween)
		{
			control.RemoveMeta(MetaScaleTween);
		}
	}

	private static void StopScaleTween(Control control)
	{
		if (!control.HasMeta(MetaScaleTween))
		{
			return;
		}

		if (control.GetMeta(MetaScaleTween).AsGodotObject() is Tween runningTween && GodotObject.IsInstanceValid(runningTween))
		{
			runningTween.Kill();
		}

		control.RemoveMeta(MetaScaleTween);
	}
}

