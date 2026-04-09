#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
	protected override Control CreateControl()
	{
		return new RuntimeDashboard();
	}

	private sealed partial class RuntimeDashboard : MarginContainer
	{
		private readonly Dictionary<string, ModRowView> _rowsById = new(StringComparer.OrdinalIgnoreCase);

		private VBoxContainer _rowsContainer = null!;
		private RichTextLabel _summaryLabel = null!;
		private RichTextLabel _detailsLabel = null!;
		private RichTextLabel _pendingLabel = null!;
		private string? _selectedModId;

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
			SizeFlagsVertical = SizeFlags.ExpandFill;
			CustomMinimumSize = new Vector2(0f, 680f);
			AddThemeConstantOverride("margin_left", 10);
			AddThemeConstantOverride("margin_top", 10);
			AddThemeConstantOverride("margin_right", 10);
			AddThemeConstantOverride("margin_bottom", 10);

			VBoxContainer root = new()
			{
				Name = "Root",
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill
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

			RichTextLabel titleLabel = BuildStaticText(
				fallbackText: "[gold]Mod Manager[/gold]",
				locKey: "REFORGE.MOD_MANAGER.TITLE",
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
			agreementRow.AddChild(agreementLabel);

			Control agreementTick = new TickBox(
				initialChecked: ReForgeModManager.GetPersistedSettings().PlayerAgreedToModLoading,
				onToggled: OnAgreementToggled)
				.Build();
			agreementTick.CustomMinimumSize = new Vector2(52f, 52f);
			agreementRow.AddChild(agreementTick);

			Godot.Button refreshButton = BuildButton(
				fallbackText: "Refresh",
				locKey: "REFORGE.MOD_MANAGER.REFRESH_BUTTON",
				onPressed: () => Refresh(preserveSelection: true),
				preset: UiButtonStylePreset.OfficialConfirm,
				minimumSize: new Vector2(156f, 42f));
			agreementRow.AddChild(refreshButton);

			_summaryLabel = BuildDynamicText(minHeight: 64f, minWidth: 0f, bbcodeEnabled: true);
			_summaryLabel.AddThemeColorOverride("default_color", StsColors.cream);
			root.AddChild(_summaryLabel);

			HSplitContainer split = new()
			{
				Name = "Split",
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsVertical = SizeFlags.ExpandFill
			};
			split.SplitOffset = 520;
			root.AddChild(split);

			PanelContainer listPanel = BuildSectionPanel("Installed Mods", "REFORGE.MOD_MANAGER.INSTALLED_MODS_TITLE");
			listPanel.CustomMinimumSize = new Vector2(520f, 420f);
			split.AddChild(listPanel);

			PanelContainer detailsPanel = BuildSectionPanel("Mod Details", "REFORGE.MOD_MANAGER.DETAILS_TITLE");
			detailsPanel.CustomMinimumSize = new Vector2(520f, 420f);
			split.AddChild(detailsPanel);

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
			GetSectionBody(listPanel).AddChild(listScroll);

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
			root.AddChild(_pendingLabel);
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