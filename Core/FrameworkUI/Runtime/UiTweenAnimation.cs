#nullable enable

using Godot;

namespace ReForgeFramework.UI.Runtime;

/// <summary>
/// 轻量 Tween 辅助：统一管理缩放动画，避免叠加冲突。
/// </summary>
internal static class UiTweenAnimation
{
	private static readonly StringName MetaScaleTween = "__reforge_scale_tween";

	/// <summary>
	/// 对目标控件执行缩放补间动画。
	/// </summary>
	/// <param name="control">目标控件。</param>
	/// <param name="targetScale">目标缩放。</param>
	/// <param name="duration">动画时长（秒）。</param>
	/// <param name="transitionType">缓动过渡类型。</param>
	/// <param name="easeType">缓动方向。</param>
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
