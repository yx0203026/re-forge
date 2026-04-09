#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using ReForgeFramework.UI.Abstractions;
using ReForgeFramework.UI.Controls;
using ReForgeFramework.UI.Localization;
using UiButton = ReForgeFramework.UI.Controls.Button;
using UiRichText = ReForgeFramework.UI.Controls.RichText;

namespace ReForgeFramework.ModLoading.UI;

internal sealed partial class ReForgeModManagerDashboard : UiElement
{
	private readonly bool _devMode;

	internal ReForgeModManagerDashboard(bool devMode = false)
	{
		_devMode = devMode;
	}

	protected override Control CreateControl()
	{
		return new RuntimeDashboard(_devMode);
	}

	private sealed partial class RuntimeDashboard : MarginContainer
	{
		private readonly bool _devMode;
		private readonly Dictionary<string, ModRowView> _rowsById = new(StringComparer.OrdinalIgnoreCase);

		private VBoxContainer _rowsContainer = null!;
		private RichTextLabel _summaryLabel = null!;
		private RichTextLabel _detailsLabel = null!;
		private RichTextLabel _pendingLabel = null!;
		private VBoxContainer _devModsRowsContainer = null!;
		private RichTextLabel _devModsSummaryLabel = null!;
		private RichTextLabel _devBuildStatusLabel = null!;
		private CanvasLayer _createModDialogLayer = null!;
		private Control _createModDialogOverlay = null!;
		private LineEdit _createModNameInput = null!;
		private LineEdit _createModAuthorInput = null!;
		private LineEdit _createModVersionInput = null!;
		private RichTextLabel _createModDialogMessage = null!;
		private bool _devBuildRunning;
		private string? _selectedModId;

		internal RuntimeDashboard(bool devMode)
		{
			_devMode = devMode;
		}

		/// <summary>
		/// Initializes the runtime dashboard layout and binds localization refresh callbacks.
		/// </summary>
		public override void _Ready()
		{
			BuildLayout();
			Refresh(preserveSelection: false);
			UiLocalization.LocaleChanged += OnLocaleChanged;
			TreeExiting += OnTreeExiting;
		}

		private void OnTreeExiting()
		{
			UiLocalization.LocaleChanged -= OnLocaleChanged;
		}

		private void OnLocaleChanged()
		{
			if (!GodotObject.IsInstanceValid(this))
			{
				return;
			}

			Refresh(preserveSelection: true);
		}

		private void BuildLayout()
		{
			Name = "ReForgeModManagerDashboard";
			SizeFlagsHorizontal = SizeFlags.ExpandFill;
			SizeFlagsVertical = SizeFlags.ShrinkBegin;
			CustomMinimumSize = new Vector2(0f, 680f);
			AddThemeConstantOverride("margin_left", 10);
			AddThemeConstantOverride("margin_top", 10);
			AddThemeConstantOverride("margin_right", 10);
			AddThemeConstantOverride("margin_bottom", 10);

			VBoxContainer root = new()
			{
				Name = "Root",
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ShrinkBegin
			};
			root.AddThemeConstantOverride("separation", 10);
			AddChild(root);

			HBoxContainer titleRow = new()
			{
				Name = "TitleRow",
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				CustomMinimumSize = new Vector2(0f, 56f)
			};
			titleRow.AddThemeConstantOverride("separation", 8);
			root.AddChild(titleRow);

			string titleFallback = _devMode ? "[gold]My Mods (Dev)[/gold]" : "[gold]Mod Manager[/gold]";
			string titleLocKey = _devMode ? "REFORGE.MOD_MANAGER.DEV_DASHBOARD_TITLE" : "REFORGE.MOD_MANAGER.TITLE";
			RichTextLabel titleLabel = BuildStaticText(
				fallbackText: titleFallback,
				locKey: titleLocKey,
				minHeight: 56f);
			titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			titleRow.AddChild(titleLabel);

			HBoxContainer agreementRow = new()
			{
				Name = "AgreementRow",
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				CustomMinimumSize = new Vector2(0f, 60f)
			};
			agreementRow.AddThemeConstantOverride("separation", 12);
			root.AddChild(agreementRow);

			RichTextLabel agreementLabel = BuildStaticText(
				fallbackText: "[gold]Allow Mod Loading[/gold]",
				locKey: "REFORGE.MOD_MANAGER.ALLOW_LOADING",
				minHeight: 52f);
			agreementLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			agreementLabel.Visible = !_devMode;
			agreementRow.AddChild(agreementLabel);

			if (_devMode)
			{
				RichTextLabel devHintLabel = BuildStaticText(
					fallbackText: "[gold]Manage development mods and build in game[/gold]",
					locKey: "REFORGE.MOD_MANAGER.DEV_DASHBOARD_HINT",
					minHeight: 52f);
				devHintLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				agreementRow.AddChild(devHintLabel);
			}

			Control agreementTick = new TickBox(
				initialChecked: ReForgeModManager.GetPersistedSettings().PlayerAgreedToModLoading,
				onToggled: OnAgreementToggled)
				.Build();
			agreementTick.CustomMinimumSize = new Vector2(52f, 52f);
			agreementTick.Visible = !_devMode;
			agreementRow.AddChild(agreementTick);

			Godot.Button refreshButton = BuildButton(
				fallbackText: "Refresh",
				locKey: "REFORGE.MOD_MANAGER.REFRESH_BUTTON",
				onPressed: () => Refresh(preserveSelection: true),
				preset: UiButtonStylePreset.OfficialConfirm,
				minimumSize: new Vector2(156f, 42f));
			agreementRow.AddChild(refreshButton);

			Godot.Button createNewModButton = BuildButton(
				fallbackText: "Create New Mod",
				locKey: "REFORGE.MOD_MANAGER.CREATE_MOD_BUTTON",
				onPressed: OpenCreateModDialog,
				preset: UiButtonStylePreset.OfficialConfirm,
				minimumSize: new Vector2(240f, 42f));
			createNewModButton.Visible = _devMode;
			agreementRow.AddChild(createNewModButton);

			_summaryLabel = BuildDynamicText(minHeight: 64f, minWidth: 0f, bbcodeEnabled: true);
			_summaryLabel.AddThemeColorOverride("default_color", StsColors.cream);
			_summaryLabel.Visible = !_devMode;
			root.AddChild(_summaryLabel);

			HSplitContainer split = new()
			{
				Name = "Split",
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ShrinkBegin,
				CustomMinimumSize = new Vector2(0f, 420f)
			};
			split.SplitOffset = 520;
			root.AddChild(split);

			PanelContainer leftPanel = BuildSectionPanel(
				_devMode ? "My Mods (Dev)" : "Installed Mods",
				_devMode ? "REFORGE.MOD_MANAGER.DEV_MODS_TITLE" : "REFORGE.MOD_MANAGER.INSTALLED_MODS_TITLE");
			leftPanel.CustomMinimumSize = new Vector2(520f, 420f);
			split.AddChild(leftPanel);

			PanelContainer detailsPanel = BuildSectionPanel(
				_devMode ? "Build Output" : "Mod Details",
				_devMode ? "REFORGE.MOD_MANAGER.DEV_BUILD_OUTPUT_TITLE" : "REFORGE.MOD_MANAGER.DETAILS_TITLE");
			detailsPanel.CustomMinimumSize = new Vector2(520f, 420f);
			split.AddChild(detailsPanel);

			if (!_devMode)
			{
				_rowsContainer = new VBoxContainer
				{
					Name = "Rows",
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					SizeFlagsVertical = SizeFlags.ShrinkBegin
				};
				_rowsContainer.AddThemeConstantOverride("separation", 12);

				ScrollContainer listScroll = new()
				{
					Name = "RowsScroll",
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					SizeFlagsVertical = SizeFlags.ExpandFill,
					FollowFocus = true,
					HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
					VerticalScrollMode = ScrollContainer.ScrollMode.Auto
				};
				listScroll.AddChild(_rowsContainer);
				GetSectionBody(leftPanel).AddChild(listScroll);
			}
			else
			{
				VBoxContainer myModsBody = GetSectionBody(leftPanel);

				_devModsSummaryLabel = BuildDynamicText(minHeight: 38f, minWidth: 0f, bbcodeEnabled: false);
				_devModsSummaryLabel.AddThemeColorOverride("default_color", StsColors.halfTransparentCream);
				myModsBody.AddChild(_devModsSummaryLabel);

				ScrollContainer devRowsScroll = new()
				{
					Name = "DevRowsScroll",
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					SizeFlagsVertical = SizeFlags.ExpandFill,
					HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
					VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
					FollowFocus = true
				};

				_devModsRowsContainer = new VBoxContainer
				{
					Name = "DevRows",
					SizeFlagsHorizontal = SizeFlags.ExpandFill,
					SizeFlagsVertical = SizeFlags.ShrinkBegin
				};
				_devModsRowsContainer.AddThemeConstantOverride("separation", 8);
				devRowsScroll.AddChild(_devModsRowsContainer);
				myModsBody.AddChild(devRowsScroll);

				_devBuildStatusLabel = BuildDynamicText(minHeight: 38f, minWidth: 0f, bbcodeEnabled: false);
				_devBuildStatusLabel.AddThemeColorOverride("default_color", StsColors.cream);
				myModsBody.AddChild(_devBuildStatusLabel);
			}

			_detailsLabel = BuildDynamicText(minHeight: 420f, minWidth: 520f, bbcodeEnabled: false);
			_detailsLabel.AddThemeColorOverride("default_color", StsColors.cream);
			_detailsLabel.AddThemeConstantOverride("line_separation", 6);
			_detailsLabel.SizeFlagsVertical = SizeFlags.ShrinkBegin;

			ScrollContainer detailsScroll = new()
			{
				Name = "DetailsScroll",
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill,
				HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
				VerticalScrollMode = ScrollContainer.ScrollMode.Auto
			};
			detailsScroll.AddChild(_detailsLabel);
			detailsScroll.Resized += () =>
			{
				if (!GodotObject.IsInstanceValid(_detailsLabel))
				{
					return;
				}

				// 详情文本强制与滚动区域同宽，避免宽度塌陷导致看似“内容丢失”。
				float contentWidth = Math.Max(420f, detailsScroll.Size.X - 22f);
				_detailsLabel.CustomMinimumSize = new Vector2(contentWidth, _detailsLabel.CustomMinimumSize.Y);
			};
			GetSectionBody(detailsPanel).AddChild(detailsScroll);

			_pendingLabel = BuildDynamicText(minHeight: 40f, minWidth: 0f, bbcodeEnabled: false);
			_pendingLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_pendingLabel.AddThemeColorOverride("default_color", new Color(1f, 0.333333f, 0.333333f, 1f));
			_pendingLabel.Visible = !_devMode;
			root.AddChild(_pendingLabel);

			if (_devMode)
			{
				BuildCreateModDialog();
			}
		}

		private void BuildCreateModDialog()
		{
			_createModDialogLayer = new CanvasLayer
			{
				Name = "CreateModDialogLayer",
				Layer = 120,
				Visible = false
			};
			AddChild(_createModDialogLayer);

			_createModDialogOverlay = new Control
			{
				Name = "CreateModDialogOverlay",
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Stop,
				FocusMode = FocusModeEnum.All
			};
			_createModDialogOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
			_createModDialogLayer.AddChild(_createModDialogOverlay);

			ColorRect dimBackground = new()
			{
				Name = "DimBackground",
				Color = new Color(0f, 0f, 0f, 0.64f),
				MouseFilter = MouseFilterEnum.Stop
			};
			dimBackground.SetAnchorsPreset(LayoutPreset.FullRect);
			_createModDialogOverlay.AddChild(dimBackground);

			ScrollContainer overlayScroll = new()
			{
				Name = "OverlayScroll",
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill,
				HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
				VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
				MouseFilter = MouseFilterEnum.Stop,
				FollowFocus = true
			};
			overlayScroll.SetAnchorsPreset(LayoutPreset.FullRect);
			_createModDialogOverlay.AddChild(overlayScroll);

			CenterContainer center = new()
			{
				Name = "DialogCenter",
				MouseFilter = MouseFilterEnum.Ignore,
				CustomMinimumSize = new Vector2(0f, 0f)
			};
			overlayScroll.AddChild(center);
			overlayScroll.Resized += () =>
			{
				if (!GodotObject.IsInstanceValid(center) || !GodotObject.IsInstanceValid(overlayScroll))
				{
					return;
				}

				// 让居中容器至少与视口同尺寸，保证可滚动时也维持居中体验。
				center.CustomMinimumSize = overlayScroll.Size;
			};
			Callable.From(() =>
			{
				if (!GodotObject.IsInstanceValid(center) || !GodotObject.IsInstanceValid(overlayScroll))
				{
					return;
				}

				center.CustomMinimumSize = overlayScroll.Size;
			}).CallDeferred();

			PanelContainer panel = new()
			{
				Name = "DialogPanel",
				CustomMinimumSize = new Vector2(680f, 420f),
				MouseFilter = MouseFilterEnum.Stop,
				SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
				SizeFlagsVertical = SizeFlags.ShrinkCenter
			};
			panel.AddThemeStyleboxOverride("panel", CreateDialogPanelStyle());
			center.AddChild(panel);

			MarginContainer panelMargin = new()
			{
				Name = "DialogMargin",
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill
			};
			panelMargin.AddThemeConstantOverride("margin_left", 18);
			panelMargin.AddThemeConstantOverride("margin_top", 16);
			panelMargin.AddThemeConstantOverride("margin_right", 18);
			panelMargin.AddThemeConstantOverride("margin_bottom", 16);
			panel.AddChild(panelMargin);

			VBoxContainer body = new()
			{
				Name = "DialogBody",
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill
			};
			body.AddThemeConstantOverride("separation", 12);
			panelMargin.AddChild(body);

			RichTextLabel dialogTitle = BuildStaticText(
				fallbackText: "[gold]Create New Mod[/gold]",
				locKey: "REFORGE.MOD_MANAGER.CREATE_MOD_DIALOG_TITLE",
				minHeight: 44f);
			dialogTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			body.AddChild(dialogTitle);

			_createModNameInput = new LineEdit
			{
				Name = "CreateModNameInput",
				PlaceholderText = T("REFORGE.MOD_MANAGER.CREATE_MOD_DIALOG_PLACEHOLDER", "Input mod name..."),
				CustomMinimumSize = new Vector2(0f, 48f),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				ClearButtonEnabled = true
			};
			ApplyDialogInputStyle(_createModNameInput);
			_createModNameInput.TextSubmitted += _ => _createModAuthorInput.GrabFocus();
			body.AddChild(_createModNameInput);

			_createModAuthorInput = new LineEdit
			{
				Name = "CreateModAuthorInput",
				PlaceholderText = T("REFORGE.MOD_MANAGER.CREATE_MOD_DIALOG_AUTHOR_PLACEHOLDER", "Input mod author..."),
				CustomMinimumSize = new Vector2(0f, 48f),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				ClearButtonEnabled = true
			};
			ApplyDialogInputStyle(_createModAuthorInput);
			_createModAuthorInput.TextSubmitted += _ => _createModVersionInput.GrabFocus();
			body.AddChild(_createModAuthorInput);

			_createModVersionInput = new LineEdit
			{
				Name = "CreateModVersionInput",
				PlaceholderText = T("REFORGE.MOD_MANAGER.CREATE_MOD_DIALOG_VERSION_PLACEHOLDER", "Input mod version..."),
				CustomMinimumSize = new Vector2(0f, 48f),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				ClearButtonEnabled = true
			};
			ApplyDialogInputStyle(_createModVersionInput);
			_createModVersionInput.TextSubmitted += _ => ConfirmCreateMod();
			body.AddChild(_createModVersionInput);

			_createModDialogMessage = BuildDynamicText(minHeight: 42f, minWidth: 0f, bbcodeEnabled: false);
			_createModDialogMessage.AddThemeColorOverride("default_color", StsColors.red);
			_createModDialogMessage.Visible = false;
			body.AddChild(_createModDialogMessage);

			HBoxContainer buttonRow = new()
			{
				Name = "DialogButtons",
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				Alignment = BoxContainer.AlignmentMode.End
			};
			buttonRow.AddThemeConstantOverride("separation", 10);
			body.AddChild(buttonRow);

			Godot.Button cancelButton = BuildButton(
				fallbackText: "Cancel",
				locKey: "REFORGE.MOD_MANAGER.CREATE_MOD_DIALOG_CANCEL",
				onPressed: CloseCreateModDialog,
				preset: UiButtonStylePreset.OfficialBack,
				minimumSize: new Vector2(150f, 42f));
			buttonRow.AddChild(cancelButton);

			Godot.Button confirmButton = BuildButton(
				fallbackText: "Create",
				locKey: "REFORGE.MOD_MANAGER.CREATE_MOD_DIALOG_CONFIRM",
				onPressed: ConfirmCreateMod,
				preset: UiButtonStylePreset.OfficialConfirm,
				minimumSize: new Vector2(170f, 42f));
			buttonRow.AddChild(confirmButton);
		}

		private void OpenCreateModDialog()
		{
			if (!GodotObject.IsInstanceValid(_createModDialogOverlay))
			{
				return;
			}

			_createModNameInput.PlaceholderText = T("REFORGE.MOD_MANAGER.CREATE_MOD_DIALOG_PLACEHOLDER", "Input mod name...");
			_createModNameInput.Text = string.Empty;
			_createModAuthorInput.PlaceholderText = T("REFORGE.MOD_MANAGER.CREATE_MOD_DIALOG_AUTHOR_PLACEHOLDER", "Input mod author...");
			_createModAuthorInput.Text = string.Empty;
			_createModVersionInput.PlaceholderText = T("REFORGE.MOD_MANAGER.CREATE_MOD_DIALOG_VERSION_PLACEHOLDER", "Input mod version...");
			_createModVersionInput.Text = string.Empty;
			SetCreateModDialogMessage(string.Empty, isError: false);
			_createModDialogLayer.Visible = true;
			_createModNameInput.GrabFocus();
		}

		private void CloseCreateModDialog()
		{
			if (!GodotObject.IsInstanceValid(_createModDialogLayer))
			{
				return;
			}

			_createModDialogLayer.Visible = false;
		}

		private void ConfirmCreateMod()
		{
			string modName = _createModNameInput.Text?.Trim() ?? string.Empty;
			string modAuthor = _createModAuthorInput.Text?.Trim() ?? string.Empty;
			string modVersion = _createModVersionInput.Text?.Trim() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(modName))
			{
				SetCreateModDialogMessage(
					T("REFORGE.MOD_MANAGER.CREATE_MOD_DIALOG_EMPTY_NAME", "Please input a valid mod name."),
					isError: true);
				return;
			}

			if (string.IsNullOrWhiteSpace(modAuthor))
			{
				SetCreateModDialogMessage(
					T("REFORGE.MOD_MANAGER.CREATE_MOD_DIALOG_EMPTY_AUTHOR", "Please input a valid mod author."),
					isError: true);
				return;
			}

			if (string.IsNullOrWhiteSpace(modVersion))
			{
				SetCreateModDialogMessage(
					T("REFORGE.MOD_MANAGER.CREATE_MOD_DIALOG_EMPTY_VERSION", "Please input a valid mod version."),
					isError: true);
				return;
			}

			if (!ReForgeModManager.TryCreateDevModProject(modName, modAuthor, modVersion, out string createdPath, out string errorMessage))
			{
				SetCreateModDialogMessage(SanitizeText(errorMessage), isError: true);
				return;
			}

			string successText = string.Format(
				T("REFORGE.MOD_MANAGER.CREATE_MOD_DIALOG_SUCCESS", "Project created at: {0}"),
				SanitizeText(createdPath));
			GD.Print($"[ReForge.ModLoader] {successText}");
			GD.Print($"[ReForge.ModLoader] Created dev mod project: {createdPath}");
			CloseCreateModDialog();
			Refresh(preserveSelection: true);
		}

		private void SetCreateModDialogMessage(string message, bool isError)
		{
			if (!GodotObject.IsInstanceValid(_createModDialogMessage))
			{
				return;
			}

			bool hasMessage = !string.IsNullOrWhiteSpace(message);
			_createModDialogMessage.Visible = hasMessage;
			_createModDialogMessage.Text = hasMessage ? message : string.Empty;
			_createModDialogMessage.AddThemeColorOverride(
				"default_color",
				isError ? StsColors.red : StsColors.cream);
		}

		private static StyleBoxFlat CreateDialogPanelStyle()
		{
			return new StyleBoxFlat
			{
				DrawCenter = true,
				BgColor = new Color(0.07451f, 0.117647f, 0.172549f, 0.98f),
				BorderColor = new Color(0.941176f, 0.705882f, 0.282353f, 1f),
				BorderWidthLeft = 2,
				BorderWidthTop = 2,
				BorderWidthRight = 2,
				BorderWidthBottom = 2,
				CornerRadiusBottomLeft = 8,
				CornerRadiusBottomRight = 8,
				CornerRadiusTopLeft = 8,
				CornerRadiusTopRight = 8
			};
		}

		private static void ApplyDialogInputStyle(LineEdit input)
		{
			input.AddThemeStyleboxOverride("normal", CreateDialogInputStyle(
				bgColor: new Color(0.082353f, 0.101961f, 0.133333f, 0.94f),
				borderColor: new Color(0.309804f, 0.52549f, 0.709804f, 1f)));
			input.AddThemeStyleboxOverride("focus", CreateDialogInputStyle(
				bgColor: new Color(0.109804f, 0.14902f, 0.215686f, 0.96f),
				borderColor: new Color(0.941176f, 0.705882f, 0.282353f, 1f)));
			input.AddThemeColorOverride("font_color", StsColors.cream);
			input.AddThemeColorOverride("font_placeholder_color", new Color(0.784314f, 0.760784f, 0.678431f, 0.58f));
			input.AddThemeColorOverride("font_selected_color", Colors.White);
			input.AddThemeColorOverride("selection_color", new Color(0.427451f, 0.584314f, 0.780392f, 0.45f));
		}

		private static StyleBoxFlat CreateDialogInputStyle(Color bgColor, Color borderColor)
		{
			return new StyleBoxFlat
			{
				DrawCenter = true,
				BgColor = bgColor,
				BorderColor = borderColor,
				BorderWidthLeft = 2,
				BorderWidthTop = 2,
				BorderWidthRight = 2,
				BorderWidthBottom = 2,
				CornerRadiusBottomLeft = 5,
				CornerRadiusBottomRight = 5,
				CornerRadiusTopLeft = 5,
				CornerRadiusTopRight = 5,
				ContentMarginLeft = 12f,
				ContentMarginRight = 12f,
				ContentMarginTop = 9f,
				ContentMarginBottom = 9f
			};
		}

		private static VBoxContainer GetSectionBody(PanelContainer section)
		{
			if (section.GetNodeOrNull<VBoxContainer>("SectionMargin/Body") is { } nestedBody)
			{
				return nestedBody;
			}

			if (section.GetNodeOrNull<VBoxContainer>("Body") is { } directBody)
			{
				return directBody;
			}

			// 防御性兜底：当节点结构被运行时调整时，动态补一个 Body，避免后续空引用连锁。
			MarginContainer margin = section.GetNodeOrNull<MarginContainer>("SectionMargin") ?? new MarginContainer
			{
				Name = "SectionMargin",
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill
			};

			if (margin.GetParent() == null)
			{
				section.AddChild(margin);
			}

			VBoxContainer body = new()
			{
				Name = "Body",
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill
			};
			body.AddThemeConstantOverride("separation", 12);
			margin.AddChild(body);
			return body;
		}

		private PanelContainer BuildSectionPanel(string fallbackTitle, string locKey)
		{
			PanelContainer section = new()
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill,
			};
			section.AddThemeStyleboxOverride("panel", CreateBorderPanelStyle());

			MarginContainer margin = new()
			{
				Name = "SectionMargin",
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill
			};
			margin.AddThemeConstantOverride("margin_left", 10);
			margin.AddThemeConstantOverride("margin_top", 8);
			margin.AddThemeConstantOverride("margin_right", 10);
			margin.AddThemeConstantOverride("margin_bottom", 10);
			section.AddChild(margin);

			VBoxContainer body = new()
			{
				Name = "Body",
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill
			};
			body.AddThemeConstantOverride("separation", 12);
			margin.AddChild(body);

			RichTextLabel title = BuildStaticText($"[gold]{EscapeBbCode(fallbackTitle)}[/gold]", locKey, 52f);
			body.AddChild(title);

			return section;
		}

		private static StyleBoxFlat CreateBorderPanelStyle()
		{
			return new StyleBoxFlat
			{
				DrawCenter = true,
				BgColor = new Color(0.039216f, 0.070588f, 0.109804f, 0.52f),
				BorderColor = new Color(0.403922f, 0.682353f, 0.921569f, 1f),
				BorderWidthLeft = 2,
				BorderWidthTop = 2,
				BorderWidthRight = 2,
				BorderWidthBottom = 2,
				CornerRadiusBottomLeft = 4,
				CornerRadiusBottomRight = 4,
				CornerRadiusTopLeft = 4,
				CornerRadiusTopRight = 4
			};
		}

		private static RichTextLabel BuildStaticText(string fallbackText, string locKey, float minHeight)
		{
			Control built = new UiRichText(
				fallbackText,
				bbcodeEnabled: true,
				locTable: "gameplay_ui",
				locEntryKey: locKey)
				.Build();

			RichTextLabel label = built as RichTextLabel ?? new RichTextLabel();
			label.CustomMinimumSize = new Vector2(0f, minHeight);
			label.FitContent = true;
			label.ScrollActive = false;
			label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			label.MouseFilter = MouseFilterEnum.Ignore;
			return label;
		}

		private static RichTextLabel BuildDynamicText(float minHeight, float minWidth = 0f, bool bbcodeEnabled = true)
		{
			Control built = new UiRichText("", bbcodeEnabled: bbcodeEnabled).Build();
			RichTextLabel label = built as RichTextLabel ?? new RichTextLabel();
			label.CustomMinimumSize = new Vector2(minWidth, minHeight);
			label.FitContent = false;
			label.ScrollActive = false;
			label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			label.MouseFilter = MouseFilterEnum.Ignore;
			label.BbcodeEnabled = bbcodeEnabled;
			label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			return label;
		}

		private static Godot.Button BuildButton(
			string fallbackText,
			string locKey,
			Action onPressed,
			UiButtonStylePreset preset,
			Vector2 minimumSize)
		{
			Control built = new UiButton(
				fallbackText,
				onPressed,
				locTable: "gameplay_ui",
				locEntryKey: locKey,
				stylePreset: preset,
				customStyler: nativeButton => nativeButton.CustomMinimumSize = minimumSize)
				.Build();

			Godot.Button button = built as Godot.Button ?? new Godot.Button();
			button.FocusMode = FocusModeEnum.All;
			button.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
			return button;
		}

		private void OnAgreementToggled(bool agreed)
		{
			ReForgeModManager.SetPlayerAgreementForNextLaunch(agreed);
			Refresh(preserveSelection: true);
		}

		private void Refresh(bool preserveSelection)
		{
			if (_devMode)
			{
				RefreshDevMods();
				return;
			}

			List<ReForgeModContext> mods = ReForgeModManager
				.GetAllMods()
				.OrderBy(static mod => mod.Manifest.Name ?? mod.ModId, StringComparer.OrdinalIgnoreCase)
				.ThenBy(static mod => mod.ModId, StringComparer.OrdinalIgnoreCase)
				.ToList();

			ReForgeModSettings settings = ReForgeModManager.GetPersistedSettings();
			ReForgeModDiagnosticsSnapshot diagnostics = ReForgeModManager.GetDiagnosticsSnapshot();

			if (!preserveSelection)
			{
				_selectedModId = null;
			}

			RebuildRows(mods, settings, diagnostics);
			UpdateSummary(mods, settings);
			UpdatePendingWarning();
		}

		private void RebuildRows(
			IReadOnlyList<ReForgeModContext> mods,
			ReForgeModSettings settings,
			ReForgeModDiagnosticsSnapshot diagnostics)
		{
			foreach (Node child in _rowsContainer.GetChildren())
			{
				child.QueueFree();
			}

			_rowsById.Clear();
			foreach (ReForgeModContext mod in mods)
			{
				ModRowView row = BuildRow(mod, settings);
				_rowsById[mod.ModId] = row;
				_rowsContainer.AddChild(row.Root);
			}

			string targetModId = _selectedModId ?? mods.FirstOrDefault()?.ModId ?? string.Empty;
			if (string.IsNullOrWhiteSpace(targetModId) || !_rowsById.ContainsKey(targetModId))
			{
				_detailsLabel.Text = BuildEmptyDetailsText();
				_selectedModId = null;
				return;
			}

			SelectMod(targetModId, diagnostics);
		}

		private void RefreshDevMods()
		{
			if (!GodotObject.IsInstanceValid(_devModsRowsContainer)
				|| !GodotObject.IsInstanceValid(_devModsSummaryLabel)
				|| !GodotObject.IsInstanceValid(_devBuildStatusLabel))
			{
				return;
			}

			foreach (Node child in _devModsRowsContainer.GetChildren())
			{
				child.QueueFree();
			}

			IReadOnlyList<ReForgeDevModProject> projects = ReForgeModManager.GetDevModProjects();
			string devRoot = ReForgeModManager.GetDevModsRootPath();
			if (string.IsNullOrWhiteSpace(devRoot))
			{
				devRoot = "<unknown>";
			}

			_devModsSummaryLabel.Text = string.Format(
				T("REFORGE.MOD_MANAGER.DEV_MODS_SUMMARY", "Dev root: {0} | Projects: {1}"),
				SanitizeText(devRoot),
				projects.Count);

			if (projects.Count == 0)
			{
				RichTextLabel emptyLabel = BuildDynamicText(minHeight: 52f, minWidth: 0f, bbcodeEnabled: false);
				emptyLabel.AddThemeColorOverride("default_color", StsColors.gray);
				emptyLabel.Text = T(
					"REFORGE.MOD_MANAGER.DEV_MODS_EMPTY",
					"No development mods were found under the dev directory.");
				_devModsRowsContainer.AddChild(emptyLabel);
				return;
			}

			foreach (ReForgeDevModProject project in projects)
			{
				Control row = BuildDevProjectRow(project);
				_devModsRowsContainer.AddChild(row);
			}
		}

		private Control BuildDevProjectRow(ReForgeDevModProject project)
		{
			PanelContainer rowRoot = new()
			{
				Name = "DevMod_" + project.ModName,
				CustomMinimumSize = new Vector2(0f, 126f),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Pass
			};

			StyleBoxFlat rowStyle = new()
			{
				BgColor = new Color(0.043137f, 0.07451f, 0.113725f, 0.44f),
				BorderColor = new Color(0.168627f, 0.25098f, 0.309804f, 1f),
				BorderWidthLeft = 1,
				BorderWidthTop = 1,
				BorderWidthRight = 1,
				BorderWidthBottom = 1,
				CornerRadiusBottomLeft = 2,
				CornerRadiusBottomRight = 2,
				CornerRadiusTopLeft = 2,
				CornerRadiusTopRight = 2
			};
			rowRoot.AddThemeStyleboxOverride("panel", rowStyle);

			MarginContainer contentMargin = new()
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill
			};
			contentMargin.AddThemeConstantOverride("margin_left", 12);
			contentMargin.AddThemeConstantOverride("margin_right", 12);
			contentMargin.AddThemeConstantOverride("margin_top", 6);
			contentMargin.AddThemeConstantOverride("margin_bottom", 6);
			rowRoot.AddChild(contentMargin);

			HBoxContainer rowContent = new()
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill,
				Alignment = BoxContainer.AlignmentMode.Begin
			};
			rowContent.AddThemeConstantOverride("separation", 10);
			contentMargin.AddChild(rowContent);

			RichTextLabel infoLabel = BuildDynamicText(minHeight: 72f, minWidth: 0f, bbcodeEnabled: true);
			infoLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			infoLabel.AddThemeColorOverride("default_color", StsColors.cream);
			infoLabel.Text = BuildDevProjectRowText(project);
			rowContent.AddChild(infoLabel);

			VBoxContainer actionColumn = new()
			{
				SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
				SizeFlagsVertical = SizeFlags.ShrinkCenter
			};
			actionColumn.AddThemeConstantOverride("separation", 6);
			rowContent.AddChild(actionColumn);

			Godot.Button buildButton = BuildButton(
				fallbackText: "Build",
				locKey: "REFORGE.MOD_MANAGER.DEV_MODS_BUILD_BUTTON",
				onPressed: () => StartBuildDevProject(project),
				preset: UiButtonStylePreset.OfficialConfirm,
				minimumSize: new Vector2(132f, 38f));
			buildButton.Disabled = _devBuildRunning || !project.HasProjectFile || !project.HasManifestFile;
			actionColumn.AddChild(buildButton);

			Godot.Button reloadButton = BuildButton(
				fallbackText: "Reload",
				locKey: "REFORGE.MOD_MANAGER.DEV_MODS_RELOAD_BUTTON",
				onPressed: () => ReloadDevProject(project),
				preset: UiButtonStylePreset.OfficialConfirm,
				minimumSize: new Vector2(132f, 34f));
			reloadButton.Disabled = _devBuildRunning || !project.HasManifestFile;
			actionColumn.AddChild(reloadButton);

			Godot.Button uninstallButton = BuildButton(
				fallbackText: "Uninstall",
				locKey: "REFORGE.MOD_MANAGER.DEV_MODS_UNINSTALL_BUTTON",
				onPressed: () => UninstallDevProject(project),
				preset: UiButtonStylePreset.OfficialBack,
				minimumSize: new Vector2(132f, 34f));
			uninstallButton.Disabled = _devBuildRunning;
			actionColumn.AddChild(uninstallButton);

			rowRoot.GuiInput += input =>
			{
				if (input is not InputEventMouseButton mouseButton
					|| mouseButton.ButtonIndex != MouseButton.Left
					|| !mouseButton.Pressed)
				{
					return;
				}

				_detailsLabel.Text = BuildDevProjectDetailsText(project);
			};

			return rowRoot;
		}

		private void StartBuildDevProject(ReForgeDevModProject project)
		{
			if (_devBuildRunning)
			{
				SetDevBuildStatus(
					T("REFORGE.MOD_MANAGER.DEV_MODS_BUILD_BUSY", "Another build is running, please wait."),
					isError: true);
				return;
			}

			if (!project.HasProjectFile || string.IsNullOrWhiteSpace(project.ProjectFilePath))
			{
				SetDevBuildStatus(
					T("REFORGE.MOD_MANAGER.DEV_MODS_PROJECT_MISSING", "This development mod has no .csproj file."),
					isError: true);
				return;
			}

			_devBuildRunning = true;
			SetDevBuildStatus(
				string.Format(
					T("REFORGE.MOD_MANAGER.DEV_MODS_BUILD_RUNNING", "Building '{0}' ..."),
					project.ModName),
				isError: false);

			Task.Run(() =>
			{
				ReForgeDevBuildResult result = ReForgeModManager.BuildDevModProject(project.ProjectFilePath!);
				Callable.From(() =>
				{
					if (!GodotObject.IsInstanceValid(this))
					{
						return;
					}

					ApplyDevBuildResult(project, result);
				}).CallDeferred();
			});
		}

		private void ApplyDevBuildResult(ReForgeDevModProject project, ReForgeDevBuildResult result)
		{
			_devBuildRunning = false;

			string statusText = result.Succeeded
				? string.Format(T("REFORGE.MOD_MANAGER.DEV_MODS_BUILD_SUCCESS", "Build succeeded: {0}"), project.ModName)
				: string.Format(T("REFORGE.MOD_MANAGER.DEV_MODS_BUILD_FAILED", "Build failed: {0}"), project.ModName);

			SetDevBuildStatus(statusText, isError: !result.Succeeded);

			StringBuilder details = new();
			details.AppendLine(statusText);
			details.AppendLine(new string('-', 44));
			details.AppendLine(result.Summary);
			details.AppendLine();
			details.AppendLine(result.Output);
			_detailsLabel.Text = details.ToString();

			RefreshDevMods();
		}

		private void ReloadDevProject(ReForgeDevModProject project)
		{
			ReForgeModRuntimeActionResult result = ReForgeModManager.ReloadDevModProject(project.ModDirectory);
			ApplyDevRuntimeActionResult(project, result, "REFORGE.MOD_MANAGER.DEV_MODS_RELOAD");
		}

		private void UninstallDevProject(ReForgeDevModProject project)
		{
			ReForgeModRuntimeActionResult result = ReForgeModManager.UninstallDevModProject(project.ModDirectory);
			ApplyDevRuntimeActionResult(project, result, "REFORGE.MOD_MANAGER.DEV_MODS_UNINSTALL");
		}

		private void ApplyDevRuntimeActionResult(ReForgeDevModProject project, ReForgeModRuntimeActionResult result, string actionKey)
		{
			string actionText = actionKey switch
			{
				"REFORGE.MOD_MANAGER.DEV_MODS_RELOAD" => T("REFORGE.MOD_MANAGER.DEV_MODS_RELOAD", "Reload"),
				"REFORGE.MOD_MANAGER.DEV_MODS_UNINSTALL" => T("REFORGE.MOD_MANAGER.DEV_MODS_UNINSTALL", "Uninstall"),
				_ => T("REFORGE.MOD_MANAGER.DEV_MODS_ACTION", "Action")
			};

			string statusText;
			if (result.Succeeded)
			{
				statusText = string.Format(
					T("REFORGE.MOD_MANAGER.DEV_MODS_ACTION_SUCCESS", "{0} succeeded: {1}"),
					actionText,
					project.ModName);
			}
			else
			{
				statusText = string.Format(
					T("REFORGE.MOD_MANAGER.DEV_MODS_ACTION_FAILED", "{0} failed: {1}"),
					actionText,
					project.ModName);
			}

			if (result.RequiresRestart)
			{
				statusText += " " + T(
					"REFORGE.MOD_MANAGER.DEV_MODS_RESTART_REQUIRED",
					"Restart required to fully apply changes.");
			}

			SetDevBuildStatus(statusText, isError: !result.Succeeded);

			StringBuilder details = new();
			details.AppendLine(statusText);
			details.AppendLine(new string('-', 44));
			details.AppendLine(result.Summary);
			if (!string.IsNullOrWhiteSpace(result.Details))
			{
				details.AppendLine();
				details.AppendLine(result.Details);
			}

			_detailsLabel.Text = details.ToString();
			RefreshDevMods();
		}

		private void SetDevBuildStatus(string text, bool isError)
		{
			if (!GodotObject.IsInstanceValid(_devBuildStatusLabel))
			{
				return;
			}

			_devBuildStatusLabel.Text = SanitizeText(text);
			_devBuildStatusLabel.AddThemeColorOverride(
				"default_color",
				isError ? StsColors.red : StsColors.cream);
		}

		private string BuildDevProjectRowText(ReForgeDevModProject project)
		{
			string entryStatus = project.HasModMainFile
				? T("REFORGE.MOD_MANAGER.DEV_MODS_ENTRY_READY", "ModMain.cs ready")
				: T("REFORGE.MOD_MANAGER.DEV_MODS_ENTRY_MISSING", "ModMain.cs missing");

			string projectStatus = project.HasProjectFile
				? Path.GetFileName(project.ProjectFilePath) ?? "*.csproj"
				: T("REFORGE.MOD_MANAGER.DEV_MODS_PROJECT_MISSING", "No .csproj found");

			string manifestStatus = project.HasManifestFile
				? Path.GetFileName(project.ManifestFilePath) ?? "*.json"
				: T("REFORGE.MOD_MANAGER.DEV_MODS_MANIFEST_MISSING", "No manifest json found");

			string resourceStatus = project.HasResourceDirectory
				? T("REFORGE.MOD_MANAGER.DEV_MODS_RESOURCE_READY", "Resource directory ready")
				: T("REFORGE.MOD_MANAGER.DEV_MODS_RESOURCE_MISSING", "Resource directory missing");

			return
				$"[b]{EscapeBbCode(project.ModName)}[/b]\n" +
				$"[color=#D0CAB8]{EscapeBbCode(entryStatus)}  |  {EscapeBbCode(resourceStatus)}[/color]\n" +
				$"[color=#A8C8E8]{EscapeBbCode(projectStatus)}  |  {EscapeBbCode(manifestStatus)}[/color]";
		}

		private string BuildDevProjectDetailsText(ReForgeDevModProject project)
		{
			StringBuilder builder = new();
			builder.AppendLine(project.ModName);
			builder.AppendLine(new string('-', 44));
			builder.Append(SanitizeText(T("REFORGE.MOD_MANAGER.DETAIL_PATH", "Path"))).Append(": ")
				.AppendLine(SanitizeText(project.ModDirectory));
			builder.Append(SanitizeText(T("REFORGE.MOD_MANAGER.DEV_MODS_PROJECT_FILE", "Project"))).Append(": ")
				.AppendLine(SanitizeText(project.ProjectFilePath ?? "<missing>"));
			builder.Append(SanitizeText(T("REFORGE.MOD_MANAGER.DEV_MODS_MANIFEST_FILE", "Manifest"))).Append(": ")
				.AppendLine(SanitizeText(project.ManifestFilePath ?? "<missing>"));
			builder.Append(SanitizeText(T("REFORGE.MOD_MANAGER.DEV_MODS_ENTRY_STATUS", "Entry"))).Append(": ")
				.AppendLine(project.HasModMainFile ? "ModMain.cs" : "<missing>");
			builder.Append(SanitizeText(T("REFORGE.MOD_MANAGER.DEV_MODS_RESOURCE_STATUS", "Resource"))).Append(": ")
				.AppendLine(project.HasResourceDirectory ? "OK" : "<missing>");
			builder.Append(SanitizeText(T("REFORGE.MOD_MANAGER.DEV_MODS_LAST_MODIFIED", "Last Modified"))).Append(": ")
				.AppendLine(project.LastModifiedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
			return builder.ToString();
		}

		private ModRowView BuildRow(ReForgeModContext mod, ReForgeModSettings settings)
		{
			PanelContainer rowRoot = new()
			{
				Name = $"ModRow_{mod.ModId}",
				CustomMinimumSize = new Vector2(0f, 106f),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				FocusMode = FocusModeEnum.All,
				MouseFilter = MouseFilterEnum.Pass
			};

			StyleBoxFlat rowStyle = new()
			{
				BgColor = new Color(0.043137f, 0.07451f, 0.113725f, 0.44f),
				BorderColor = new Color(0.168627f, 0.25098f, 0.309804f, 1f),
				BorderWidthLeft = 1,
				BorderWidthTop = 1,
				BorderWidthRight = 1,
				BorderWidthBottom = 1,
				CornerRadiusBottomLeft = 2,
				CornerRadiusBottomRight = 2,
				CornerRadiusTopLeft = 2,
				CornerRadiusTopRight = 2
			};
			rowRoot.AddThemeStyleboxOverride("panel", rowStyle);

			MarginContainer contentMargin = new()
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill
			};
			contentMargin.AddThemeConstantOverride("margin_left", 12);
			contentMargin.AddThemeConstantOverride("margin_right", 12);
			contentMargin.AddThemeConstantOverride("margin_top", 4);
			contentMargin.AddThemeConstantOverride("margin_bottom", 4);
			rowRoot.AddChild(contentMargin);

			HBoxContainer rowContent = new()
			{
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill,
				Alignment = BoxContainer.AlignmentMode.Begin
			};
			rowContent.AddThemeConstantOverride("separation", 10);
			contentMargin.AddChild(rowContent);

			RichTextLabel titleLabel = BuildDynamicText(minHeight: 86f, minWidth: 0f, bbcodeEnabled: true);
			titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			titleLabel.AddThemeFontSizeOverride("normal_font_size", 20);
			titleLabel.AddThemeFontSizeOverride("bold_font_size", 20);
			titleLabel.Text = BuildRowText(mod);
			titleLabel.AddThemeColorOverride("default_color", GetStateColor(mod.State));
			rowContent.AddChild(titleLabel);

			RichTextLabel sourceLabel = BuildDynamicText(minHeight: 86f, minWidth: 96f, bbcodeEnabled: true);
			sourceLabel.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
			sourceLabel.HorizontalAlignment = HorizontalAlignment.Right;
			sourceLabel.AddThemeFontSizeOverride("normal_font_size", 18);
			sourceLabel.AddThemeFontSizeOverride("bold_font_size", 18);
			sourceLabel.Text = BuildSourceText(mod.SourceKind);
			sourceLabel.AddThemeColorOverride("default_color", StsColors.halfTransparentCream);
			rowContent.AddChild(sourceLabel);

			bool isEnabledForNextLaunch = settings.PlayerAgreedToModLoading && !settings.DisabledModIds.Contains(mod.ModId);
			Control toggle = new TickBox(isEnabledForNextLaunch, checkedOn =>
			{
				bool persisted = ReForgeModManager.SetModEnabledForNextLaunch(mod.ModId, checkedOn);
				if (!persisted)
				{
					Refresh(preserveSelection: true);
					return;
				}

				UpdatePendingWarning();
			})
				.Build();
			toggle.CustomMinimumSize = new Vector2(56f, 56f);
			toggle.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			rowContent.AddChild(toggle);

			rowRoot.GuiInput += input =>
			{
				if (input is InputEventMouseButton mouseButton
					&& mouseButton.ButtonIndex == MouseButton.Left
					&& mouseButton.Pressed)
				{
					SelectMod(mod.ModId, ReForgeModManager.GetDiagnosticsSnapshot());
				}
			};

			rowRoot.MouseEntered += () =>
			{
				if (_selectedModId == mod.ModId)
				{
					return;
				}

				Color hover = StsColors.darkBlue;
				hover.A = 0.25f;
				rowStyle.BgColor = hover;
			};

			rowRoot.MouseExited += () =>
			{
				if (_selectedModId == mod.ModId)
				{
					return;
				}

				rowStyle.BgColor = new Color(0.043137f, 0.07451f, 0.113725f, 0.44f);
			};

			return new ModRowView
			{
				Mod = mod,
				Root = rowRoot,
				Style = rowStyle
			};
		}

		private void SelectMod(string modId, ReForgeModDiagnosticsSnapshot diagnostics)
		{
			if (_detailsLabel == null)
			{
				return;
			}

			if (!_rowsById.TryGetValue(modId, out ModRowView? target))
			{
				_detailsLabel.Text = BuildEmptyDetailsText();
				_selectedModId = null;
				return;
			}

			_selectedModId = modId;
			foreach (KeyValuePair<string, ModRowView> pair in _rowsById)
			{
				if (pair.Value?.Style == null)
				{
					continue;
				}

				bool selected = pair.Key.Equals(modId, StringComparison.OrdinalIgnoreCase);
				if (selected)
				{
					Color picked = StsColors.blue;
					picked.A = 0.25f;
					pair.Value.Style.BgColor = picked;
				}
				else
				{
					pair.Value.Style.BgColor = new Color(0.043137f, 0.07451f, 0.113725f, 0.44f);
				}
			}

			_detailsLabel.Text = BuildDetailsText(target.Mod, diagnostics);
		}

		private void UpdateSummary(IReadOnlyList<ReForgeModContext> mods, ReForgeModSettings settings)
		{
			int loadedCount = mods.Count(static m => m.State == ReForgeModLoadState.Loaded);
			int failedCount = mods.Count(static m => m.State == ReForgeModLoadState.Failed);
			int disabledCount = mods.Count(static m => m.State == ReForgeModLoadState.Disabled);
			int enabledForNextLaunch = settings.PlayerAgreedToModLoading
				? mods.Count(mod => !settings.DisabledModIds.Contains(mod.ModId))
				: 0;

			string runningStateText = settings.PlayerAgreedToModLoading
				? T("REFORGE.MOD_MANAGER.STATUS_LOADING_ON", "Loading Enabled")
				: T("REFORGE.MOD_MANAGER.STATUS_LOADING_OFF", "Loading Disabled");

			_summaryLabel.Text =
				$"[gold]{EscapeBbCode(T("REFORGE.MOD_MANAGER.SUMMARY_TITLE", "Summary"))}[/gold]  " +
				$"{EscapeBbCode(T("REFORGE.MOD_MANAGER.SUMMARY_TOTAL", "Total"))}: [b]{mods.Count}[/b]  " +
				$"{EscapeBbCode(T("REFORGE.MOD_MANAGER.SUMMARY_LOADED", "Loaded"))}: [b]{loadedCount}[/b]  " +
				$"{EscapeBbCode(T("REFORGE.MOD_MANAGER.SUMMARY_DISABLED", "Disabled"))}: [b]{disabledCount}[/b]  " +
				$"{EscapeBbCode(T("REFORGE.MOD_MANAGER.SUMMARY_FAILED", "Failed"))}: [b]{failedCount}[/b]\n" +
				$"{EscapeBbCode(T("REFORGE.MOD_MANAGER.SUMMARY_NEXT_LAUNCH", "Next Launch Enabled"))}: [b]{enabledForNextLaunch}[/b]  " +
				$"{EscapeBbCode(T("REFORGE.MOD_MANAGER.SUMMARY_LOADING_STATE", "Mod Loading"))}: [b]{EscapeBbCode(runningStateText)}[/b]";
		}

		private void UpdatePendingWarning()
		{
			bool pending = ReForgeModManager.HasPendingRestartChanges();
			_pendingLabel.Visible = pending;
			if (pending)
			{
				_pendingLabel.Text = EscapeBbCode(T(
					"REFORGE.MOD_MANAGER.PENDING_CHANGES",
					"Disabling/Enabling mods will only take effect after a restart."));
			}
			else
			{
				_pendingLabel.Text = string.Empty;
			}
		}

		private string BuildRowText(ReForgeModContext mod)
		{
			string displayName = mod.Manifest.Name ?? mod.ModId;
			string version = mod.Manifest.Version ?? "unknown";
			string state = GetStateText(mod.State);
			return
				$"[b]{EscapeBbCode(displayName)}[/b]  [color=#E0B12A]({EscapeBbCode(mod.ModId)})[/color]\n" +
				$"[color=#D0CAB8]{EscapeBbCode(T("REFORGE.MOD_MANAGER.ROW_VERSION", "Version"))}: {EscapeBbCode(version)}[/color]\n" +
				$"[color=#A8C8E8]{EscapeBbCode(T("REFORGE.MOD_MANAGER.ROW_STATE", "State"))}: {EscapeBbCode(state)}[/color]";
		}

		private string BuildSourceText(ReForgeModSourceKind sourceKind)
		{
			return $"[right][color=#CFC9B8]{EscapeBbCode(T("REFORGE.MOD_MANAGER.ROW_SOURCE", "Source"))}:\n{EscapeBbCode(GetSourceText(sourceKind))}[/color][/right]";
		}

		private string BuildDetailsText(ReForgeModContext mod, ReForgeModDiagnosticsSnapshot diagnostics)
		{
			string title = mod.Manifest.Name ?? mod.ModId;
			string author = mod.Manifest.Author ?? "unknown";
			string version = mod.Manifest.Version ?? "unknown";
			string description = mod.Manifest.Description ?? T("REFORGE.MOD_MANAGER.NO_DESCRIPTION", "No description.");
			string state = GetStateText(mod.State);
			string source = GetSourceText(mod.SourceKind);

			StringBuilder builder = new();
			builder.AppendLine(SanitizeText(title));
			builder.AppendLine(new string('-', 44));
			builder.Append(SanitizeText(T("REFORGE.MOD_MANAGER.DETAIL_AUTHOR", "Author"))).Append(": ")
				.AppendLine(SanitizeText(author));
			builder.Append(SanitizeText(T("REFORGE.MOD_MANAGER.DETAIL_VERSION", "Version"))).Append(": ")
				.AppendLine(SanitizeText(version));
			builder.Append(SanitizeText(T("REFORGE.MOD_MANAGER.DETAIL_STATE", "State"))).Append(": ")
				.AppendLine(SanitizeText(state));
			builder.Append(SanitizeText(T("REFORGE.MOD_MANAGER.DETAIL_SOURCE", "Source"))).Append(": ")
				.AppendLine(SanitizeText(source));
			builder.Append(SanitizeText(T("REFORGE.MOD_MANAGER.DETAIL_PATH", "Path"))).Append(": ")
				.AppendLine(SanitizeText(mod.ModPath));
			builder.AppendLine();
			builder.AppendLine(SanitizeText(description));

			if (mod.Manifest.Dependencies is { Count: > 0 })
			{
				builder.AppendLine();
				builder.Append(SanitizeText(T("REFORGE.MOD_MANAGER.DETAIL_DEPENDENCIES", "Dependencies"))).AppendLine(":");
				foreach (string dependency in mod.Manifest.Dependencies)
				{
					builder.Append("- ").AppendLine(SanitizeText(dependency));
				}
			}

			if (mod.Errors.Count > 0)
			{
				builder.AppendLine();
				builder.Append(SanitizeText(T("REFORGE.MOD_MANAGER.DETAIL_ERRORS", "Errors"))).AppendLine(":");
				foreach (string error in mod.Errors)
				{
					builder.Append("- ").AppendLine(SanitizeText(error));
				}
			}

			IEnumerable<ReForgeModDiagnosticEvent> recentEvents = diagnostics.Events
				.Where(e => e.ModId.Equals(mod.ModId, StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(static e => e.Timestamp)
				.Take(8);

			if (recentEvents.Any())
			{
				builder.AppendLine();
				builder.Append(SanitizeText(T("REFORGE.MOD_MANAGER.DETAIL_RECENT_EVENTS", "Recent Events"))).AppendLine(":");

				foreach (ReForgeModDiagnosticEvent evt in recentEvents)
				{
					string time = evt.Timestamp.ToLocalTime().ToString("HH:mm:ss");
					builder
						.Append("- [")
						.Append(SanitizeText(time))
						.Append("] ")
						.Append(SanitizeText(evt.Phase.ToString()))
						.Append(" / ")
						.Append(SanitizeText(evt.State.ToString()))
						.Append(": ")
						.AppendLine(SanitizeText(evt.Message));
				}
			}

			return builder.ToString();
		}

		private string BuildEmptyDetailsText()
		{
			return SanitizeText(T("REFORGE.MOD_MANAGER.EMPTY_DETAILS", "Select a mod from the left list to view details."));
		}

		private static Color GetStateColor(ReForgeModLoadState state)
		{
			return state switch
			{
				ReForgeModLoadState.Loaded => Colors.White,
				ReForgeModLoadState.Failed => StsColors.red,
				ReForgeModLoadState.Disabled => StsColors.gray,
				ReForgeModLoadState.AddedAtRuntime => StsColors.gray,
				ReForgeModLoadState.None => StsColors.purple,
				_ => Colors.White
			};
		}

		private string GetStateText(ReForgeModLoadState state)
		{
			return state switch
			{
				ReForgeModLoadState.Loaded => T("REFORGE.MOD_MANAGER.STATE_LOADED", "Loaded"),
				ReForgeModLoadState.Disabled => T("REFORGE.MOD_MANAGER.STATE_DISABLED", "Disabled"),
				ReForgeModLoadState.Failed => T("REFORGE.MOD_MANAGER.STATE_FAILED", "Failed"),
				ReForgeModLoadState.AddedAtRuntime => T("REFORGE.MOD_MANAGER.STATE_ADDED_AT_RUNTIME", "AddedAtRuntime"),
				_ => T("REFORGE.MOD_MANAGER.STATE_NONE", "None")
			};
		}

		private string GetSourceText(ReForgeModSourceKind sourceKind)
		{
			return sourceKind switch
			{
				ReForgeModSourceKind.Pck => T("REFORGE.MOD_MANAGER.SOURCE_PCK", "PCK"),
				ReForgeModSourceKind.Embedded => T("REFORGE.MOD_MANAGER.SOURCE_EMBEDDED", "Embedded"),
				_ => T("REFORGE.MOD_MANAGER.SOURCE_UNKNOWN", "Unknown")
			};
		}

		private static string EscapeBbCode(string text)
		{
			return (text ?? string.Empty)
				.Replace("[", "\\[", StringComparison.Ordinal)
				.Replace("]", "\\]", StringComparison.Ordinal);
		}

		private static string SanitizeText(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return string.Empty;
			}

			return text.Replace("\r\n", "\n", StringComparison.Ordinal);
		}

		private static string T(string key, string fallback)
		{
			return UiLocalization.GetText(null, fallback, "gameplay_ui", key);
		}

		private sealed class ModRowView
		{
			public required ReForgeModContext Mod { get; init; }

			public required PanelContainer Root { get; init; }

			public required StyleBoxFlat Style { get; init; }
		}
	}
}