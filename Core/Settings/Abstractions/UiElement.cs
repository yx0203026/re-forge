#nullable enable

using System;
using Godot;
using ReForgeFramework.Settings.Runtime;

namespace ReForgeFramework.Settings.Abstractions;

/// <summary>
/// 鍩虹 UI 鍏冪礌鏋勫缓鍩虹被锛屾彁渚涢摼寮忚皟鐢ㄧ殑甯冨眬銆佷氦浜掍笌瑙嗚澶栬閰嶇疆鎺ュ彛銆?/// </summary>
public abstract class UiElement : IUiElement
{
	private Control? _cachedControl;
	private readonly UiLayoutOptions _layoutOptions = new();
	private readonly UiInteractionOptions _interactionOptions = new();
	private readonly UiVisualOptions _visualOptions = new();

	protected Control? BuiltControl => _cachedControl;

	/// <summary>
/// 璁剧疆 UI 鍏冪礌鐨勫搴︺€?/// </summary>
/// <param name="width">鐩爣瀹藉害鍊笺€?/param>
/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
public UiElement WithWidth(float width)
	{
		_layoutOptions.Width = width;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
/// 璁剧疆 UI 鍏冪礌鐨勬渶灏忓搴︺€?/// </summary>
/// <param name="minWidth">鏈€灏忓搴﹀€笺€?/param>
/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
public UiElement WithMinWidth(float minWidth)
	{
		_layoutOptions.MinWidth = minWidth;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
/// 璁剧疆 UI 鍏冪礌鐨勬渶澶у搴︺€?/// </summary>
/// <param name="maxWidth">鏈€澶у搴﹀€笺€?/param>
/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
public UiElement WithMaxWidth(float maxWidth)
	{
		_layoutOptions.MaxWidth = maxWidth;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
/// 璁剧疆 UI 鍏冪礌鐨勯珮搴︺€?/// </summary>
/// <param name="height">鐩爣楂樺害鍊笺€?/param>
/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
public UiElement WithHeight(float height)
	{
		_layoutOptions.Height = height;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
/// 璁剧疆 UI 鍏冪礌鐨勬渶灏忛珮搴︺€?/// </summary>
/// <param name="minHeight">鏈€灏忛珮搴﹀€笺€?/param>
/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
public UiElement WithMinHeight(float minHeight)
	{
		_layoutOptions.MinHeight = minHeight;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
/// 璁剧疆 UI 鍏冪礌鐨勬渶澶ч珮搴︺€?/// </summary>
/// <param name="maxHeight">鏈€澶ч珮搴﹀€笺€?/param>
/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
public UiElement WithMaxHeight(float maxHeight)
	{
		_layoutOptions.MaxHeight = maxHeight;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
/// 璁剧疆 UI 鍏冪礌鐨勯敋鐐归璁剧被鍨嬨€?/// </summary>
/// <param name="preset">閿氱偣棰勮绫诲瀷锛堜緥濡傚眳涓€佸～婊＄瓑锛夈€?/param>
/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
public UiElement WithAnchor(UiAnchorPreset preset)
	{
		_layoutOptions.AnchorPreset = preset;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
/// 璁剧疆 UI 鍏冪礌鐨勫潗鏍囧亸绉汇€?/// </summary>
/// <param name="x">X杞村亸绉婚噺銆?/param>
/// <param name="y">Y杞村亸绉婚噺銆?/param>
/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
public UiElement WithPositionOffset(float x, float y)
	{
		return WithPositionOffset(new Vector2(x, y));
	}

	/// <summary>
/// 鏍规嵁缁欏畾鐨勪簩缁村悜閲忚缃?UI 鍏冪礌鐨勫潗鏍囧亸绉汇€?/// </summary>
/// <param name="offset">浜岀淮鍚戦噺琛ㄧず鐨勫亸绉婚噺銆?/param>
/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
public UiElement WithPositionOffset(Vector2 offset)
	{
		_layoutOptions.PositionOffset = offset;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
/// 涓烘墍鏈夎竟缂樿缃浉鍚岀殑鍐呰竟璺濄€?/// </summary>
/// <param name="all">鍥涘懆鍐呰竟璺濆€笺€?/param>
/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
public UiElement WithPadding(float all)
	{
		return WithPadding(UiSpacing.All(all));
	}

	/// <summary>
/// 鍒嗗埆涓烘按骞冲拰鍨傜洿鏂瑰悜璁剧疆鍐呰竟璺濄€?/// </summary>
/// <param name="horizontal">姘村钩绔唴杈硅窛銆?/param>
/// <param name="vertical">鍨傜洿绔唴杈硅窛銆?/param>
/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
public UiElement WithPadding(float horizontal, float vertical)
	{
		return WithPadding(UiSpacing.Axis(horizontal, vertical));
	}

	/// <summary>
/// 鍒嗗埆涓哄洓涓竟缂樿缃唴杈硅窛銆?/// </summary>
/// <param name="left">宸︿晶鍐呰竟璺濄€?/param>
/// <param name="top">椤堕儴鍐呰竟璺濄€?/param>
/// <param name="right">鍙充晶鍐呰竟璺濄€?/param>
/// <param name="bottom">搴曢儴鍐呰竟璺濄€?/param>
/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
public UiElement WithPadding(float left, float top, float right, float bottom)
	{
		return WithPadding(new UiSpacing(left, top, right, bottom));
	}

	/// <summary>
/// 鏍规嵁杈硅窛瀵硅薄璁剧疆鍐呰竟璺濄€?/// </summary>
/// <param name="spacing">琛ㄧず鍐呰竟璺濆睘鎬х殑瀵硅薄銆?/param>
/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
public UiElement WithPadding(UiSpacing spacing)
	{
		_layoutOptions.Padding = spacing;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
/// 涓烘墍鏈夎竟缂樿缃浉鍚岀殑澶栬竟璺濄€?/// </summary>
/// <param name="all">鍥涘懆澶栬竟璺濆€笺€?/param>
/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
public UiElement WithMargin(float all)
	{
		return WithMargin(UiSpacing.All(all));
	}

	/// <summary>
	/// 鍒嗗埆璁剧疆姘村钩鏂瑰悜涓庡瀭鐩存柟鍚戝杈硅窛銆?	/// </summary>
	/// <param name="horizontal">姘村钩澶栬竟璺濄€?/param>
	/// <param name="vertical">鍨傜洿澶栬竟璺濄€?/param>
	/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
	public UiElement WithMargin(float horizontal, float vertical)
	{
		return WithMargin(UiSpacing.Axis(horizontal, vertical));
	}

	/// <summary>
	/// 鍒嗗埆璁剧疆鍥涗釜鏂瑰悜澶栬竟璺濄€?	/// </summary>
	/// <param name="left">宸﹀杈硅窛銆?/param>
	/// <param name="top">涓婂杈硅窛銆?/param>
	/// <param name="right">鍙冲杈硅窛銆?/param>
	/// <param name="bottom">涓嬪杈硅窛銆?/param>
	/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
	public UiElement WithMargin(float left, float top, float right, float bottom)
	{
		return WithMargin(new UiSpacing(left, top, right, bottom));
	}

	/// <summary>
	/// 浣跨敤杈硅窛瀵硅薄璁剧疆澶栬竟璺濄€?	/// </summary>
	/// <param name="spacing">澶栬竟璺濆璞°€?/param>
	/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
	public UiElement WithMargin(UiSpacing spacing)
	{
		_layoutOptions.Margin = spacing;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
/// 闄勫姞榧犳爣鎮诞杩涘叆浜嬩欢澶勭悊鍣ㄣ€?/// </summary>
/// <param name="handler">鍖呭惈褰撳墠缁戝畾鎺т欢浣滀负鍙傛暟鐨勫洖璋冩搷浣溿€?/param>
/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
public UiElement OnHoverEnter(Action<Control> handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		_interactionOptions.HoverEnterHandlers.Add(handler);
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
/// 闄勫姞榧犳爣鎮诞绂诲紑浜嬩欢澶勭悊鍣ㄣ€?/// </summary>
/// <param name="handler">鍖呭惈褰撳墠缁戝畾鎺т欢浣滀负鍙傛暟鐨勫洖璋冩搷浣溿€?/param>
/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
public UiElement OnHoverExit(Action<Control> handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		_interactionOptions.HoverExitHandlers.Add(handler);
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
	/// 缁戝畾宸﹂敭鎸変笅鍥炶皟銆?	/// </summary>
	/// <param name="handler">宸﹂敭鎸変笅澶勭悊鍣ㄣ€?/param>
	/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
	public UiElement OnLeftMouseDown(Action<Control, InputEventMouseButton> handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		_interactionOptions.LeftMouseDownHandlers.Add(handler);
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
	/// 缁戝畾鍙抽敭鎸変笅鍥炶皟銆?	/// </summary>
	/// <param name="handler">鍙抽敭鎸変笅澶勭悊鍣ㄣ€?/param>
	/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
	public UiElement OnRightMouseDown(Action<Control, InputEventMouseButton> handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		_interactionOptions.RightMouseDownHandlers.Add(handler);
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
	/// 缁戝畾榧犳爣鎷栨嫿鍥炶皟銆?	/// </summary>
	/// <param name="handler">鎷栨嫿浜嬩欢澶勭悊鍣ㄣ€?/param>
	/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
	public UiElement OnDrag(Action<Control, InputEventMouseMotion> handler)
	{
		ArgumentNullException.ThrowIfNull(handler);
		_interactionOptions.DragHandlers.Add(handler);
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
	/// 浠ョ粺涓€鍊嶇巼璁剧疆鎺т欢缂╂斁銆?	/// </summary>
	/// <param name="uniformScale">缁熶竴缂╂斁鍊嶇巼銆?/param>
	/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
	public UiElement WithScale(float uniformScale)
	{
		return WithScale(new Vector2(uniformScale, uniformScale));
	}

	/// <summary>
	/// 鎸変簩缁村悜閲忚缃帶浠剁缉鏀俱€?	/// </summary>
	/// <param name="scale">缂╂斁鍚戦噺銆?/param>
	/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
	public UiElement WithScale(Vector2 scale)
	{
		_visualOptions.Scale = scale;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
	/// 璁剧疆鎺т欢璋冨埗棰滆壊銆?	/// </summary>
	/// <param name="color">璋冨埗棰滆壊銆?/param>
	/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
	public UiElement WithModulate(Color color)
	{
		_visualOptions.SelfModulate = color;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
	/// 璁剧疆鎺т欢灞傜骇浼樺厛绾э紙鏄犲皠鍒?Godot 鐨?ZIndex / ZAsRelative锛夈€?	/// </summary>
	public UiElement WithLayerPriority(int priority, bool relativeToParent = true)
	{
		_visualOptions.LayerPriority = priority;
		_visualOptions.LayerPriorityRelative = relativeToParent;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
	/// 璁剧疆鎺т欢鏄剧ず浣滅敤鍩熴€?	/// </summary>
	public UiElement WithScope(UiVisibilityScope scope)
	{
		_visualOptions.VisibilityScope = scope;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
	/// 璁剧疆 UI 鑷韩鏋㈣酱閿氱偣銆?	/// 涓庡竷灞€閿氱偣涓嶅悓锛岃閿氱偣浣滅敤浜庢帶浠惰嚜韬苟褰卞搷缂╂斁/鏃嬭浆瀵归綈鍩哄噯銆?	/// </summary>
	/// <param name="preset">UI 鑷韩鏋㈣酱閿氱偣棰勮銆?/param>
	/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
	public UiElement WithUiAnchor(UiPivotAnchorPreset preset)
	{
		_visualOptions.PivotAnchorPreset = preset;
		_visualOptions.CenterPivot = false;
		_layoutOptions.PositionAnchorPreset = preset;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
	/// 鍚敤鎴栧叧闂腑蹇冪偣鏋㈣酱銆?	/// </summary>
	/// <param name="enabled">鏄惁鍚敤銆?/param>
	/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
	public UiElement WithCenterPivot(bool enabled = true)
	{
		_visualOptions.CenterPivot = enabled;
		_visualOptions.PivotAnchorPreset = enabled ? UiPivotAnchorPreset.Center : null;
		_layoutOptions.PositionAnchorPreset = enabled ? UiPivotAnchorPreset.Center : null;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
	/// 璁剧疆绾圭悊鍙婂叾鏄剧ず鍙傛暟銆?	/// </summary>
	/// <param name="texture">绾圭悊瀵硅薄銆?/param>
	/// <param name="stretchMode">绾圭悊鎷変几妯″紡銆?/param>
	/// <param name="showBehindParent">鏄惁缁樺埗鍦ㄧ埗鎺т欢鍚庢柟銆?/param>
	/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
	public UiElement WithTexture(
		Texture2D texture,
		TextureRect.StretchModeEnum stretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
		bool showBehindParent = true)
	{
		ArgumentNullException.ThrowIfNull(texture);
		_visualOptions.Texture = texture;
		_visualOptions.TextureStretchMode = stretchMode;
		_visualOptions.TextureShowBehindParent = showBehindParent;
		ReapplyIfBuilt();
		return this;
	}

	/// <summary>
	/// 閫氳繃璧勬簮璺緞鍔犺浇骞惰缃汗鐞嗐€?	/// </summary>
	/// <param name="texturePath">绾圭悊璧勬簮璺緞銆?/param>
	/// <param name="stretchMode">绾圭悊鎷変几妯″紡銆?/param>
	/// <param name="showBehindParent">鏄惁缁樺埗鍦ㄧ埗鎺т欢鍚庢柟銆?/param>
	/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
	public UiElement WithTexturePath(
		string texturePath,
		TextureRect.StretchModeEnum stretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
		bool showBehindParent = true)
	{
		ArgumentNullException.ThrowIfNull(texturePath);

		if (!ResourceLoader.Exists(texturePath))
		{
			GD.Print($"[ReForge.UI] Texture path does not exist: '{texturePath}'.");
			return this;
		}

		Texture2D? texture = ResourceLoader.Load<Texture2D>(texturePath);
		if (texture == null)
		{
			GD.Print($"[ReForge.UI] Failed to load texture from path: '{texturePath}'.");
			return this;
		}

		return WithTexture(texture, stretchMode, showBehindParent);
	}

	/// <summary>
	/// 娓呴櫎褰撳墠鎺т欢绾圭悊閰嶇疆銆?	/// </summary>
	/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
	public UiElement ClearTexture()
	{
		_visualOptions.Texture = null;
		ReapplyIfBuilt();
		return this;
	}

	// 鍙傝€冨畼鏂逛富鑿滃崟鎸夐挳鐨勭缉鏀惧弽棣堬紝蹇€熺粰浠绘剰鎺т欢闄勫姞鎮诞缂╂斁鍔ㄧ敾銆?	/// <summary>
/// 涓哄綋鍓嶆帶浠舵坊鍔犻紶鏍囨偓娴椂鐨勭缉鏀惧姩鐢诲弽棣堛€?/// </summary>
/// <param name="hoverScale">鎮诞鏃剁殑缂╂斁鍊嶇巼锛岄粯璁や负 1.05f銆?/param>
/// <param name="hoverDuration">鎮诞鍔ㄧ敾鐨勮繃娓℃椂闂达紝榛樿涓?0.05 绉掋€?/param>
/// <param name="unhoverDuration">绂诲紑鎮诞鏃舵仮澶嶅姩鐢荤殑杩囨浮鏃堕棿锛岄粯璁や负 0.5 绉掋€?/param>
/// <returns>褰撳墠 UI 鍏冪礌鐨勯摼寮忓疄渚嬨€?/returns>
public UiElement WithHoverScaleAnimation(float hoverScale = 1.05f, float hoverDuration = 0.05f, float unhoverDuration = 0.5f)
	{
		Vector2? baseScale = _visualOptions.Scale;

		OnHoverEnter(control =>
		{
			baseScale ??= control.Scale;
			UiTweenAnimation.TweenScale(control, baseScale.Value * hoverScale, hoverDuration, Tween.TransitionType.Cubic, Tween.EaseType.Out);
		});

		OnHoverExit(control =>
		{
			Vector2 target = baseScale ?? _visualOptions.Scale ?? Vector2.One;
			UiTweenAnimation.TweenScale(control, target, unhoverDuration, Tween.TransitionType.Expo, Tween.EaseType.Out);
		});

		return this;
	}

	/// <summary>
/// 鏋勫缓骞跺簲鐢ㄥ搴旂殑 Godot 鎺т欢瀹炰緥銆?br/>
/// 濡傛灉鎺т欢宸茬粡寤虹珛杩囦簡锛屽垯鍙簲鐢ㄤ箣鍓嶉摼寮忚皟鐢ㄦ墍甯︽潵鐨勯厤缃彉鏇淬€?/// </summary>
/// <returns>鏋勫缓鍚庣殑鐩爣 Control 鑺傜偣銆?/returns>
public Control Build()
	{
		if (_cachedControl != null && !GodotObject.IsInstanceValid(_cachedControl))
		{
			_cachedControl = null;
		}

		_cachedControl ??= CreateControl();
		UiLayoutApplier.Apply(_cachedControl, _layoutOptions);
		UiVisualApplier.Apply(_cachedControl, _visualOptions);
		UiInteractionApplier.Apply(_cachedControl, _interactionOptions);
		return _cachedControl;
	}

	protected abstract Control CreateControl();

	private void ReapplyIfBuilt()
	{
		if (_cachedControl == null)
		{
			return;
		}

		UiLayoutApplier.Apply(_cachedControl, _layoutOptions);
		UiVisualApplier.Apply(_cachedControl, _visualOptions);
		UiInteractionApplier.Apply(_cachedControl, _interactionOptions);
	}
}

