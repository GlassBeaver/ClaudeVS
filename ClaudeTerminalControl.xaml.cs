namespace ClaudeVS
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Reflection;
	using System.Runtime.InteropServices;
	using System.Windows.Threading;
	using System.Windows;
	using System.Windows.Controls;
	using System.Windows.Input;
	using System.Windows.Interop;
	using System.Windows.Media;
	using System.Windows.Controls.Primitives;
	using EnvDTE;
	using EnvDTE80;
	using Microsoft.Terminal.Wpf;
	using Microsoft.VisualStudio.Shell;
	using Microsoft.VisualStudio.Shell.Interop;

	/// <summary>
	/// Interaction logic for ClaudeTerminalControl.xaml
	/// </summary>
	public partial class ClaudeTerminalControl : UserControl
	{
		[DllImport("user32.dll")]
		private static extern IntPtr SetFocus(IntPtr hWnd);

		[DllImport("Microsoft.Terminal.Control.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall, PreserveSig = true)]
		[return: MarshalAs(UnmanagedType.I1)]
		private static extern bool TerminalIsSelectionActive(IntPtr terminal);

		[DllImport("Microsoft.Terminal.Control.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall, PreserveSig = true)]
		private static extern void TerminalUserScroll(IntPtr terminal, int viewTop);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetCursorPos(out POINT lpPoint);

		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll")]
		private static extern short GetAsyncKeyState(int vKey);

		private const int VK_LBUTTON = 0x01;
		private const uint WM_MOUSEWHEEL = 0x020A;
		private const int WHEEL_DELTA = 120;
		private const int MK_LBUTTON = 0x0001;
	private const int WM_LBUTTONDOWN = 0x0201;

		[StructLayout(LayoutKind.Sequential)]
		private struct POINT
		{
			public int X;
			public int Y;
		}

		private enum QuickSwitchAgentType
		{
			None,
			Claude,
			Codex,
		}

		private class AgentTab
		{
			public string Title;
			public TabItem TabItem;
			public TerminalControl TerminalControl;
			public Border TerminalBorder;
			public ConPtyTerminal Terminal;
			public ConPtyTerminalConnection Connection;
			public IntPtr TerminalHandle = IntPtr.Zero;
			public IntPtr TerminalHwnd = IntPtr.Zero;
			public DispatcherTimer SelectionScrollTimer;
			public object TermContainerInstance;
			public ScrollBar TerminalScrollbar;
			public MethodInfo UserScrollMethod;
			public bool IsInitialized;
			public bool NeedsResizeAfterOutput;
			public string CurrentSolutionPath;
			public DispatcherTimer RefreshTimer;
			public DateTime LastOutputTime = DateTime.MinValue;
			public string Command;
			public bool MouseHookInstalled;
		}

		private ClaudeTerminal claudeTerminal;
		private DTE2 dte;
		private SolutionEvents solutionEvents;
		private string currentCommand = "claude";
		private short currentFontSize = 10;
		private string currentTheme = "System";
		private int nextTabIndex = 1;
		private List<AgentTab> agentTabs = new List<AgentTab>();
		private AgentTab activeTab;
		private AgentTab lastUserSelectedTab;
		private Popup quickSwitchPopup;
		private bool quickSwitchInProgress;
		private readonly string[] quickSwitchClaudeModels = { "Opus 4.6", "Sonnet 4.6", "Haiku 4.5" };
		private readonly string[] quickSwitchClaudeModelIds = { "claude-opus-4-6", "claude-sonnet-4-6", "claude-haiku-4-5" };
		private readonly string[] quickSwitchClaudeEfforts = { "Low", "Medium", "High", "Max", "Auto" };
		private readonly string[] quickSwitchClaudeEffortIds = { "low", "medium", "high", "max", "auto" };
		private readonly string[] quickSwitchCodexModels = { "gpt-5.4", "gpt-5.2-codex", "gpt-5.1-codex-max", "gpt-5.4-mini", "gpt-5.3-codex", "gpt-5.2", "gpt-5.1-codex-mini" };
		private readonly string[] quickSwitchCodexEfforts = { "low", "medium", "high", "xhigh" };
		private int[] quickSwitchClaudePresetModel = { 0, 1, 2, 2 };
		private bool[] quickSwitchClaudePresetThinking = { false, false, false, false };
		private int[] quickSwitchClaudePresetEffort = { 0, 0, 0, 0 };
		private int[] quickSwitchCodexPresetModel = { 0, 1, 2, 3 };
		private bool[] quickSwitchCodexPresetThinking = { false, false, false, false };
		private int[] quickSwitchCodexPresetEffort = { 1, 1, 1, 1 };
		private bool eventsInitialized;

		/// <summary>
		/// Initializes a new instance of the <see cref="ClaudeTerminalControl"/> class.
		/// </summary>
		public ClaudeTerminalControl(ToolWindowPane toolWindowPane = null)
		{
			this.claudeTerminal = toolWindowPane as ClaudeTerminal;
			this.InitializeComponent();
			this.Loaded += ClaudeTerminalControl_Loaded;
			this.Unloaded += ClaudeTerminalControl_Unloaded;
			this.SizeChanged += ClaudeTerminalControl_SizeChanged;
			this.IsVisibleChanged += ClaudeTerminalControl_IsVisibleChanged;
			AgentTabs.GotKeyboardFocus += AgentTabs_GotKeyboardFocus;
		}

		private void ClaudeTerminalControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
		{
			try
			{
				currentCommand = SettingsManager.GetLastCommand();
				currentFontSize = SettingsManager.GetFontSize();
				currentTheme = SettingsManager.GetTheme();
				SettingsManager.LoadQuickSwitchPresets("Claude", quickSwitchClaudePresetModel, quickSwitchClaudePresetThinking, quickSwitchClaudePresetEffort, quickSwitchClaudeModels.Length, quickSwitchClaudeEfforts.Length);
				SettingsManager.LoadQuickSwitchPresets("Codex", quickSwitchCodexPresetModel, quickSwitchCodexPresetThinking, quickSwitchCodexPresetEffort, quickSwitchCodexModels.Length, quickSwitchCodexEfforts.Length);

				if (agentTabs.Count == 0)
				{
					CreateNewAgentTab(false);
				}

				UpdateTabVisibility();

				if (activeTab == null)
				{
					SetActiveTab(agentTabs[0]);
				}
				else if (lastUserSelectedTab == null)
				{
					lastUserSelectedTab = activeTab;
				}

				if (!eventsInitialized)
				{
					dte = GetDTE();
					if (dte != null && dte.Events != null)
					{
						solutionEvents = dte.Events.SolutionEvents;
						if (solutionEvents != null)
						{
							solutionEvents.Opened += SolutionEvents_Opened;
							solutionEvents.AfterClosing += SolutionEvents_AfterClosing;
						}
					}
					eventsInitialized = true;
				}

				EnsureTabInitialized(activeTab);
				ApplyThemeToAll();
				ApplyFontSizeToAll();
				FocusTerminal();

				Dispatcher.BeginInvoke(new Action(() =>
				{
					try
					{
						if (activeTab != null)
						{
							ApplyFontSize(activeTab);
						}
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"Exception in deferred layout: {ex}");
					}
				}), DispatcherPriority.ContextIdle);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in ClaudeTerminalControl_Loaded: {ex}");
			}
		}

		private void InitializeConPtyTerminal(AgentTab tab)
		{
			try
			{
				var conPtyTerminal = new ConPtyTerminal(rows: 30, columns: 120);
				conPtyTerminal.Command = tab.Command ?? currentCommand;

				string workingDir = GetActiveProjectDirectory();
				if (string.IsNullOrEmpty(workingDir))
				{
					workingDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
				}

				bool initialized = conPtyTerminal.Initialize(workingDir);

				if (!initialized)
				{
					conPtyTerminal?.Dispose();
					return;
				}

				var terminalConnection = new ConPtyTerminalConnection(conPtyTerminal);

				conPtyTerminal.OutputReceived += (sender, output) => ConPtyTerminal_OutputReceived(tab, output);

				terminalConnection.WaitForConnectionReady();

				tab.Terminal = conPtyTerminal;
				tab.Connection = terminalConnection;
				tab.TerminalControl.Connection = terminalConnection;

				ExtractTerminalHandle(tab);

				var theme = GetTerminalTheme();
				var bgColor = GetThemeBackgroundColor();
				tab.TerminalControl.SetTheme(theme, "Consolas", currentFontSize, bgColor);
				tab.TerminalControl.Background = new SolidColorBrush(bgColor);
				this.Background = new SolidColorBrush(bgColor);
				AgentTabs.Background = new SolidColorBrush(bgColor);
				UpdateToolbarColors();

				UpdateTerminalMaxWidth(tab);

				terminalConnection.Start();

				tab.TerminalControl.SetTheme(theme, "Consolas", currentFontSize, bgColor);

				tab.NeedsResizeAfterOutput = true;
				tab.IsInitialized = true;
				tab.CurrentSolutionPath = workingDir;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in InitializeConPtyTerminal: {ex}");
			}
		}

		private void ReconnectTerminal()
		{
			try
			{
				if (activeTab?.Connection != null)
				{
					activeTab.TerminalControl.Connection = activeTab.Connection;
					activeTab.TerminalControl.InvalidateVisual();
					activeTab.TerminalControl.UpdateLayout();
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in ReconnectTerminal: {ex}");
			}
		}

		private void ClaudeTerminalControl_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
		{
			try
			{
				if (activeTab == null)
				{
					return;
				}
				var terminalConnection = activeTab.Connection;
				if (terminalConnection != null && activeTab.TerminalControl.ActualHeight > 0 && activeTab.TerminalControl.ActualWidth > 0)
				{
					double charHeight = currentFontSize * 1.2;

					uint columns = 120;
					uint rows = (uint)Math.Max(1, activeTab.TerminalControl.ActualHeight / charHeight);

					terminalConnection.Resize(rows, columns);

					var size = new Size(activeTab.TerminalControl.ActualWidth, activeTab.TerminalControl.ActualHeight);
					activeTab.TerminalControl.TriggerResize(size);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in ClaudeTerminalControl_SizeChanged: {ex}");
			}
		}

		private void ClaudeTerminalControl_Unloaded(object sender, System.Windows.RoutedEventArgs e)
		{
			try
			{
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in ClaudeTerminalControl_Unloaded: {ex}");
			}
		}

		private void SolutionEvents_Opened()
		{
			try
			{
				string projectDir = GetActiveProjectDirectory();
				if (!string.IsNullOrEmpty(projectDir))
				{
					foreach (var tab in agentTabs)
					{
						if (tab.CurrentSolutionPath == projectDir)
						{
							continue;
						}
						tab.CurrentSolutionPath = projectDir;
						RestartClaudeWithWorkingDirectory(tab, projectDir);
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in SolutionEvents_Opened: {ex}");
			}
		}

		private void SolutionEvents_AfterClosing()
		{
			try
			{
				foreach (var tab in agentTabs)
				{
					tab.CurrentSolutionPath = null;
				}
				StopClaude();
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in SolutionEvents_AfterClosing: {ex}");
			}
		}

		private void RestartClaudeWithWorkingDirectory(AgentTab tab, string workingDirectory)
		{
			try
			{
				StopClaude(tab);
				InitializeConPtyTerminal(tab);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in RestartClaudeWithWorkingDirectory: {ex}");
			}
		}

		private void StopClaude()
		{
			try
			{
				foreach (var tab in agentTabs)
				{
					StopClaude(tab);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in StopClaude: {ex}");
			}
		}

		private void StopClaude(AgentTab tab)
		{
			try
			{
				tab.RefreshTimer?.Stop();
				tab.SelectionScrollTimer?.Stop();
				tab.TerminalControl.Connection = null;
				tab.Terminal?.Dispose();
				tab.Connection = null;
				tab.Terminal = null;
				tab.TerminalHandle = IntPtr.Zero;
				tab.TerminalHwnd = IntPtr.Zero;
				tab.IsInitialized = false;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in StopClaude(AgentTab): {ex}");
			}
		}

		private DTE2 GetDTE()
		{
			try
			{
				if (claudeTerminal != null)
				{
					DTE2 result = claudeTerminal.GetService<EnvDTE.DTE, EnvDTE.DTE>() as DTE2;
					if (result != null)
						return result;
				}

				return (DTE2)System.Runtime.InteropServices.Marshal.GetActiveObject("VisualStudio.DTE.18.0");
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in GetDTE: {ex}");
				return null;
			}
		}

		private string GetActiveProjectDirectory()
		{
			try
			{
				DTE2 localDte = dte ?? GetDTE();

				if (localDte == null || localDte.Solution == null)
				{
					return null;
				}

				if (localDte.Solution.IsOpen && !string.IsNullOrEmpty(localDte.Solution.FullName))
				{
					string solutionDir = Path.GetDirectoryName(localDte.Solution.FullName);
					return solutionDir;
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in GetActiveProjectDirectory: {ex}");
			}

			return null;
		}

		public void SendToClaude(string message, bool bEnter)
		{
			try
			{
				var terminal = activeTab?.Terminal;
				if (terminal != null)
					terminal.SendToClaude(message, bEnter);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in SendToClaude: {ex}");
			}
		}

		private DispatcherTimer focusTimer;

		public void FocusTerminal()
		{
			try
			{
				if (focusTimer != null)
				{
					focusTimer.Stop();
				}

				var tabToFocus = lastUserSelectedTab ?? activeTab;

				focusTimer = new DispatcherTimer
				{
					Interval = TimeSpan.FromMilliseconds(100)
				};
					focusTimer.Tick += (s, e) =>
					{
						focusTimer.Stop();
						try
						{
							if (tabToFocus != null && AgentTabs.SelectedItem != tabToFocus.TabItem)
							{
								AgentTabs.SelectedItem = tabToFocus.TabItem;
								activeTab = tabToFocus;
							}

							if (activeTab?.TerminalControl == null)
							{
								return;
							}
							var hwndHost = FindVisualChild<HwndHost>(activeTab.TerminalControl);
							if (hwndHost != null && hwndHost.Handle != IntPtr.Zero)
							{
								SetFocus(hwndHost.Handle);
							}
							activeTab.TerminalControl.Focus();
							Keyboard.Focus(activeTab.TerminalControl);
						}
						catch (Exception ex)
						{
							Debug.WriteLine($"Exception in FocusTerminal timer: {ex}");
					}
				};
				focusTimer.Start();
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in FocusTerminal: {ex}");
			}
		}

		private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
		{
			for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
			{
				var child = VisualTreeHelper.GetChild(parent, i);
				if (child is T result)
					return result;
				var descendant = FindVisualChild<T>(child);
				if (descendant != null)
					return descendant;
			}
			return null;
		}

		private void ClaudeTerminalControl_GotFocus(object sender, RoutedEventArgs e)
		{
			if (e.OriginalSource == this)
			{
				FocusTerminal();
				e.Handled = true;
			}
		}

		private void ClaudeTerminalControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
		{
		}

		public ConPtyTerminal ActiveTerminal => activeTab?.Terminal;
		public ConPtyTerminalConnection ActiveConnection => activeTab?.Connection;

		public IntPtr TerminalHwnd
		{
			get
			{
				if (activeTab == null) return IntPtr.Zero;
				if (activeTab.TerminalHwnd != IntPtr.Zero) return activeTab.TerminalHwnd;
				if (activeTab.TerminalControl != null)
				{
					var hwndHost = FindVisualChild<HwndHost>(activeTab.TerminalControl);
					if (hwndHost != null && hwndHost.Handle != IntPtr.Zero)
					{
						activeTab.TerminalHwnd = hwndHost.Handle;
					}
				}
				return activeTab?.TerminalHwnd ?? IntPtr.Zero;
			}
		}

		public IntPtr TerminalHandle
		{
			get
			{
				if (activeTab == null) return IntPtr.Zero;
				if (activeTab.TerminalHandle != IntPtr.Zero) return activeTab.TerminalHandle;
				ExtractTerminalHandle(activeTab);
				return activeTab?.TerminalHandle ?? IntPtr.Zero;
			}
		}

		public void DisposeAllTerminals()
		{
			StopClaude();
		}

		private AgentTab CreateNewAgentTab(bool initialize)
		{
			var tab = new AgentTab();
			tab.Title = $"Agent {nextTabIndex++}";
			tab.Command = currentCommand;

			var terminalControl = new TerminalControl
			{
				Background = new SolidColorBrush(GetThemeBackgroundColor()),
				Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)),
				IsHitTestVisible = true,
				Focusable = true
			};

			var border = new Border
			{
				HorizontalAlignment = HorizontalAlignment.Left,
				Child = terminalControl
			};

			tab.TerminalControl = terminalControl;
			tab.TerminalBorder = border;

			string effectiveTheme = currentTheme;
			if (effectiveTheme == "System")
			{
				effectiveTheme = IsSystemDarkMode() ? "Dark" : "Light";
			}
			var tabItemStyle = (Style)FindResource(effectiveTheme == "Light" ? "LightTabItemStyle" : "DarkTabItemStyle");
			var closeButtonStyle = (Style)FindResource(effectiveTheme == "Light" ? "LightTabCloseButtonStyle" : "DarkTabCloseButtonStyle");

			var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
			var headerText = new TextBlock
			{
				Text = tab.Title,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 6, 0)
			};
			var closeButton = new Button
			{
				Style = closeButtonStyle,
				VerticalAlignment = VerticalAlignment.Center,
				Focusable = false,
				IsTabStop = false
			};
			closeButton.Click += CloseTabButton_Click;
			headerPanel.Children.Add(headerText);
			headerPanel.Children.Add(closeButton);

			tab.TabItem = new TabItem
			{
				Header = headerPanel,
				Content = border,
				Style = tabItemStyle
			};

			agentTabs.Add(tab);
			AgentTabs.Items.Add(tab.TabItem);

			UpdateTabVisibility();

			UpdateTerminalMaxWidth(tab);
			ApplyTheme(tab);
			ApplyFontSize(tab);

			if (initialize)
			{
				EnsureTabInitialized(tab);
			}

			return tab;
		}

		private void EnsureTabInitialized(AgentTab tab)
		{
			if (tab == null || tab.IsInitialized)
			{
				return;
			}

			InitializeConPtyTerminal(tab);
		}

		private QuickSwitchAgentType GetQuickSwitchAgentType()
		{
			string cmd = activeTab?.Command ?? currentCommand;
			if (cmd == null)
			{
				return QuickSwitchAgentType.None;
			}

			if (cmd.IndexOf("claude", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return QuickSwitchAgentType.Claude;
			}

			if (cmd.IndexOf("codex", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return QuickSwitchAgentType.Codex;
			}

			return QuickSwitchAgentType.None;
		}

		private void GetQuickSwitchPresetState(QuickSwitchAgentType agentType, out int[] models, out bool[] thinking, out int[] efforts)
		{
			if (agentType == QuickSwitchAgentType.Codex)
			{
				models = quickSwitchCodexPresetModel;
				thinking = quickSwitchCodexPresetThinking;
				efforts = quickSwitchCodexPresetEffort;
				return;
			}

			models = quickSwitchClaudePresetModel;
			thinking = quickSwitchClaudePresetThinking;
			efforts = quickSwitchClaudePresetEffort;
		}

		private string[] GetQuickSwitchModels(QuickSwitchAgentType agentType)
		{
			return agentType == QuickSwitchAgentType.Codex ? quickSwitchCodexModels : quickSwitchClaudeModels;
		}

		private string[] GetQuickSwitchEfforts(QuickSwitchAgentType agentType)
		{
			return agentType == QuickSwitchAgentType.Codex ? quickSwitchCodexEfforts : quickSwitchClaudeEfforts;
		}

		private string GetQuickSwitchAgentKey(QuickSwitchAgentType agentType)
		{
			return agentType == QuickSwitchAgentType.Codex ? "Codex" : "Claude";
		}

		private bool QuickSwitchSupportsThinking(QuickSwitchAgentType agentType)
		{
			return agentType == QuickSwitchAgentType.Claude;
		}

		private bool QuickSwitchShowsEffort(QuickSwitchAgentType agentType, int modelIndex)
		{
			if (agentType == QuickSwitchAgentType.Claude)
			{
				return modelIndex == 0;
			}

			return agentType == QuickSwitchAgentType.Codex;
		}

		private void UpdateQuickSwitchVisibility()
		{
			QuickSwitchButton.Visibility = GetQuickSwitchAgentType() == QuickSwitchAgentType.None ? Visibility.Collapsed : Visibility.Visible;
		}

		private void SetActiveTab(AgentTab tab)
		{
			if (tab == null)
			{
				return;
			}

			activeTab = tab;
			lastUserSelectedTab = tab;
			AgentTabs.SelectedItem = tab.TabItem;
			EnsureTabInitialized(tab);
			UpdateQuickSwitchVisibility();
			FocusTerminal();
		}

		private AgentTab GetTabByItem(object item)
		{
			foreach (var tab in agentTabs)
			{
				if (tab.TabItem == item)
				{
					return tab;
				}
			}
			return null;
		}

		private void AgentTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			try
			{
				UpdateTabVisibility();
				var selectedTab = GetTabByItem(AgentTabs.SelectedItem);
				if (selectedTab != null)
				{
					activeTab = selectedTab;

					if (Mouse.LeftButton == MouseButtonState.Pressed && AgentTabs.IsMouseOver)
					{
						lastUserSelectedTab = selectedTab;
					}

					EnsureTabInitialized(selectedTab);
					UpdateQuickSwitchVisibility();
					FocusTerminal();
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in AgentTabs_SelectionChanged: {ex}");
			}
		}

		private void AgentTabs_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
		{
			var tabToRestore = lastUserSelectedTab;
			if (tabToRestore == null)
			{
				return;
			}

			if (AgentTabs.SelectedItem != tabToRestore.TabItem)
			{
				AgentTabs.SelectedItem = tabToRestore.TabItem;
				activeTab = tabToRestore;
			}

			UpdateQuickSwitchVisibility();

			if (activeTab?.TerminalControl != null)
			{
				var hwndHost = FindVisualChild<HwndHost>(activeTab.TerminalControl);
				if (hwndHost != null && hwndHost.Handle != IntPtr.Zero)
				{
					SetFocus(hwndHost.Handle);
				}
			}
		}

		private void NewAgentButton_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				var tab = CreateNewAgentTab(true);
				SetActiveTab(tab);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in NewAgentButton_Click: {ex}");
			}
		}

		private void CloseTabButton_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				if (agentTabs.Count <= 1)
				{
					return;
				}

				var button = sender as Button;
				if (button == null)
				{
					return;
				}

				var headerPanel = button.Parent as StackPanel;
				if (headerPanel == null)
				{
					return;
				}

				AgentTab tabToClose = null;
				foreach (var tab in agentTabs)
				{
					if (tab.TabItem.Header == headerPanel)
					{
						tabToClose = tab;
						break;
					}
				}

				if (tabToClose == null)
				{
					return;
				}

				int tabIndex = agentTabs.IndexOf(tabToClose);
				bool wasActive = (tabToClose == activeTab);

				StopClaude(tabToClose);
				agentTabs.Remove(tabToClose);
				AgentTabs.Items.Remove(tabToClose.TabItem);

				UpdateTabVisibility();

				if (wasActive && agentTabs.Count > 0)
				{
					int newIndex = Math.Min(tabIndex, agentTabs.Count - 1);
					SetActiveTab(agentTabs[newIndex]);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in CloseTabButton_Click: {ex}");
			}
		}

		private void UpdateTabVisibility()
		{
			AgentTabs.Tag = agentTabs.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
		}

		private void ConPtyTerminal_OutputReceived(AgentTab tab, string e)
		{
			tab.LastOutputTime = DateTime.UtcNow;

			var connection = tab.Connection;
			if (connection != null)
			{
				connection.IsPaused = IsTerminalSelectionActive(tab);
			}

			if (tab.NeedsResizeAfterOutput)
			{
				tab.NeedsResizeAfterOutput = false;
				Dispatcher.BeginInvoke(new Action(() =>
				{
					try
					{
						ApplyFontSize(tab);
						StartRefreshTimer(tab);
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"Exception in ConPtyTerminal_OutputReceived resize handler: {ex}");
					}
				}), System.Windows.Threading.DispatcherPriority.Render);
			}
		}

		private void ExtractTerminalHandle(AgentTab tab)
		{
			try
			{
				var termContainerField = tab.TerminalControl.GetType().GetField("termContainer", BindingFlags.NonPublic | BindingFlags.Instance);
				if (termContainerField != null)
				{
					var termContainer = termContainerField.GetValue(tab.TerminalControl);
					if (termContainer != null)
					{
						tab.TermContainerInstance = termContainer;

						var terminalField = termContainer.GetType().GetField("terminal", BindingFlags.NonPublic | BindingFlags.Instance);
						if (terminalField != null)
						{
							tab.TerminalHandle = (IntPtr)terminalField.GetValue(termContainer);
						}

						var hwndField = termContainer.GetType().GetField("hwnd", BindingFlags.NonPublic | BindingFlags.Instance);
						if (hwndField != null)
						{
							tab.TerminalHwnd = (IntPtr)hwndField.GetValue(termContainer);
						}

						tab.UserScrollMethod = termContainer.GetType().GetMethod("UserScroll", BindingFlags.NonPublic | BindingFlags.Instance);

						if (!tab.MouseHookInstalled && termContainer is HwndHost hwndHost)
						{
							hwndHost.MessageHook += delegate(IntPtr h, int m, IntPtr w, IntPtr l, ref bool hd)
							{
								if (m == WM_LBUTTONDOWN)
								{
									(claudeTerminal?.Frame as IVsWindowFrame)?.Show();
									SetFocus(h);
								}
								return IntPtr.Zero;
							};
							tab.MouseHookInstalled = true;
						}
					}
				}

				var scrollbarField = tab.TerminalControl.GetType().GetField("scrollbar", BindingFlags.NonPublic | BindingFlags.Instance);
				if (scrollbarField != null)
				{
					tab.TerminalScrollbar = scrollbarField.GetValue(tab.TerminalControl) as ScrollBar;
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in ExtractTerminalHandle: {ex}");
			}
		}

		private bool IsTerminalSelectionActive(AgentTab tab)
		{
			try
			{
				if (tab.TerminalHandle != IntPtr.Zero)
				{
					return TerminalIsSelectionActive(tab.TerminalHandle);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in IsTerminalSelectionActive: {ex}");
			}
			return false;
		}

		private void StartRefreshTimer(AgentTab tab)
		{
			if (tab.RefreshTimer == null)
			{
				tab.RefreshTimer = new DispatcherTimer
				{
					Interval = TimeSpan.FromMilliseconds(500)
				};
				tab.RefreshTimer.Tick += (s, e) => RefreshTimer_Tick(tab);
			}
			tab.RefreshTimer.Start();

			StartSelectionScrollTimer(tab);
		}

		private void StartSelectionScrollTimer(AgentTab tab)
		{
			if (tab.SelectionScrollTimer == null)
			{
				tab.SelectionScrollTimer = new DispatcherTimer(DispatcherPriority.Input)
				{
					Interval = TimeSpan.FromMilliseconds(16)
				};
				tab.SelectionScrollTimer.Tick += (s, e) => SelectionScrollTimer_Tick(tab);
			}
			tab.SelectionScrollTimer.Start();
		}

		private bool IsLeftMouseButtonDown()
		{
			return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
		}

		private bool wasMouseButtonDown = false;
		private bool mouseDownStartedInTerminal = false;

		private void SelectionScrollTimer_Tick(AgentTab tab)
		{
			try
			{
				if (tab.TerminalHwnd == IntPtr.Zero)
				{
					ExtractTerminalHandle(tab);
					if (tab.TerminalHwnd == IntPtr.Zero)
					{
						return;
					}
				}

				bool isMouseDown = IsLeftMouseButtonDown();

				if (!isMouseDown)
				{
					wasMouseButtonDown = false;
					mouseDownStartedInTerminal = false;
					return;
				}

				if (!GetCursorPos(out POINT cursorPos))
				{
					return;
				}

				var terminalPoint = tab.TerminalControl.PointFromScreen(new Point(cursorPos.X, cursorPos.Y));

				if (!wasMouseButtonDown)
				{
					wasMouseButtonDown = true;
					mouseDownStartedInTerminal = terminalPoint.X >= 0 && terminalPoint.X <= tab.TerminalControl.ActualWidth &&
					                             terminalPoint.Y >= 0 && terminalPoint.Y <= tab.TerminalControl.ActualHeight;
				}

				if (!mouseDownStartedInTerminal)
				{
					return;
				}

				double edgeMargin = 20;
				double terminalTop = edgeMargin;
				double terminalBottom = tab.TerminalControl.ActualHeight - edgeMargin;

				bool isOutsideBounds = terminalPoint.Y < terminalTop || terminalPoint.Y > terminalBottom;
				if (!isOutsideBounds)
				{
					return;
				}

				int wheelDelta = 0;

				if (terminalPoint.Y < terminalTop)
				{
					double distance = terminalTop - terminalPoint.Y;
					int scrollSpeed = Math.Max(1, Math.Min(3, (int)(distance / 50) + 1));
					wheelDelta = (WHEEL_DELTA / 2) * scrollSpeed;
				}
				else if (terminalPoint.Y > terminalBottom)
				{
					double distance = terminalPoint.Y - terminalBottom;
					int scrollSpeed = Math.Max(1, Math.Min(3, (int)(distance / 50) + 1));
					wheelDelta = -(WHEEL_DELTA / 2) * scrollSpeed;
				}

				if (wheelDelta != 0)
				{
					int wParam = (wheelDelta << 16) | MK_LBUTTON;
					int lParam = (cursorPos.Y << 16) | (cursorPos.X & 0xFFFF);
					SendMessage(tab.TerminalHwnd, WM_MOUSEWHEEL, (IntPtr)wParam, (IntPtr)lParam);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in SelectionScrollTimer_Tick: {ex}");
			}
		}

		private void RefreshTimer_Tick(AgentTab tab)
		{
			try
			{
				bool isSelecting = IsTerminalSelectionActive(tab);

				var connection = tab.Connection;
				if (connection != null)
				{
					connection.IsPaused = isSelecting;
				}

				if (!isSelecting &&
					(DateTime.UtcNow - tab.LastOutputTime).TotalMilliseconds < 1000 &&
					tab.TerminalControl.ActualHeight > 0 && tab.TerminalControl.ActualWidth > 0)
				{
					var theme = GetTerminalTheme();
					var bgColor = GetThemeBackgroundColor();
					tab.TerminalControl.SetTheme(theme, "Consolas", currentFontSize, bgColor);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in RefreshTimer_Tick: {ex}");
			}
		}

		private void ChangeCommandButton_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				var dialog = new CommandInputDialog(activeTab?.Command ?? currentCommand);
				dialog.Owner = Application.Current?.MainWindow;
				if (dialog.ShowDialog() == true)
				{
					string newCommand = dialog.CommandName?.Trim();
					if (!string.IsNullOrWhiteSpace(newCommand) && !string.Equals(newCommand, activeTab?.Command ?? currentCommand, StringComparison.OrdinalIgnoreCase))
					{
						if (activeTab == null)
						{
							return;
						}
						activeTab.Command = newCommand;
						currentCommand = newCommand;
						SettingsManager.SaveLastCommand(currentCommand);
						UpdateQuickSwitchVisibility();
						activeTab.NeedsResizeAfterOutput = true;
						string projectDir = GetActiveProjectDirectory();
						if (!string.IsNullOrEmpty(projectDir))
						{
							activeTab.CurrentSolutionPath = projectDir;
							RestartClaudeWithWorkingDirectory(activeTab, projectDir);
						}
						else
						{
							activeTab.CurrentSolutionPath = null;
							StopClaude(activeTab);
							InitializeConPtyTerminal(activeTab);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in ChangeCommandButton_Click: {ex}");
			}
		}

		private void RestartAgentButton_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				if (activeTab == null)
				{
					return;
				}
				activeTab.NeedsResizeAfterOutput = true;
				string projectDir = GetActiveProjectDirectory();
				if (!string.IsNullOrEmpty(projectDir))
				{
					activeTab.CurrentSolutionPath = projectDir;
					RestartClaudeWithWorkingDirectory(activeTab, projectDir);
				}
				else
				{
					activeTab.CurrentSolutionPath = null;
					StopClaude(activeTab);
					InitializeConPtyTerminal(activeTab);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in RestartAgentButton_Click: {ex}");
			}
		}

		private void FontSizeButton_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				var dialog = new FontSizeDialog(currentFontSize);
				dialog.Owner = Application.Current?.MainWindow;
				if (dialog.ShowDialog() == true)
				{
					short newFontSize = dialog.SelectedFontSize;
					if (newFontSize != currentFontSize)
					{
						currentFontSize = newFontSize;
						SettingsManager.SaveFontSize(currentFontSize);
						ApplyFontSizeToAll();
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in FontSizeButton_Click: {ex}");
			}
		}

		private void QuickSwitchButton_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				if (activeTab?.Terminal != null && activeTab.Terminal.IsRunning)
				{
					var agentType = GetQuickSwitchAgentType();
					if (agentType == QuickSwitchAgentType.None)
					{
						return;
					}

					if (quickSwitchPopup != null && quickSwitchPopup.IsOpen)
					{
						quickSwitchPopup.IsOpen = false;
						return;
					}

					GetQuickSwitchPresetState(agentType, out var presetModels, out var presetThinking, out var presetEfforts);
					string[] models = GetQuickSwitchModels(agentType);
					string[] efforts = GetQuickSwitchEfforts(agentType);
					string agentKey = GetQuickSwitchAgentKey(agentType);
					bool supportsThinking = QuickSwitchSupportsThinking(agentType);

					string effectiveTheme = currentTheme;
					if (effectiveTheme == "System")
						effectiveTheme = IsSystemDarkMode() ? "Dark" : "Light";
					bool isDark = effectiveTheme != "Light";

					var bgBrush = isDark
						? new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30))
						: new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));
					var borderBrush = isDark
						? new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46))
						: new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
					var fgBrush = isDark
						? new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4))
						: new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

					var popup = new Popup
					{
						StaysOpen = false,
						AllowsTransparency = true,
						PlacementTarget = QuickSwitchButton,
						Placement = PlacementMode.Bottom,
					};

					var outerBorder = new Border
					{
						Background = bgBrush,
						BorderBrush = borderBrush,
						BorderThickness = new Thickness(1),
						Padding = new Thickness(8),
					};

					var grid = new Grid();
					grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
					grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
					grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
					grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
					grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

					for (int i = 0; i < 5; i++)
						grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

					var headerMargin = new Thickness(4, 2, 4, 6);

					var hModel = new TextBlock { Text = "Model", Foreground = fgBrush, Margin = headerMargin, FontWeight = FontWeights.Bold };
					Grid.SetRow(hModel, 0); Grid.SetColumn(hModel, 1);
					grid.Children.Add(hModel);

					var hThinking = new TextBlock { Text = "Thinking", Foreground = fgBrush, Margin = headerMargin, FontWeight = FontWeights.Bold };
					Grid.SetRow(hThinking, 0); Grid.SetColumn(hThinking, 2);
					hThinking.Visibility = supportsThinking ? Visibility.Visible : Visibility.Collapsed;
					grid.Children.Add(hThinking);

					var hEffort = new TextBlock { Text = "Effort", Foreground = fgBrush, Margin = headerMargin, FontWeight = FontWeights.Bold };
					Grid.SetRow(hEffort, 0); Grid.SetColumn(hEffort, 3);
					grid.Children.Add(hEffort);

					string[] quickSwitchCommandNames = {
						"ClaudeVS.QuickSwitch1",
						"ClaudeVS.QuickSwitch2",
						"ClaudeVS.QuickSwitch3",
						"ClaudeVS.QuickSwitch4"
					};
					string[] quickSwitchHotkeys = new string[4];
					try
					{
						DTE2 localDte = dte ?? GetDTE();
						if (localDte != null)
						{
							for (int i = 0; i < 4; i++)
							{
								try
								{
									var cmd = localDte.Commands.Item(quickSwitchCommandNames[i]);
									var bindings = cmd.Bindings as object[];
									if (bindings != null && bindings.Length > 0)
									{
										string b = bindings[0].ToString();
										int idx = b.IndexOf("::");
										quickSwitchHotkeys[i] = idx >= 0 ? b.Substring(idx + 2) : b;
									}
								}
								catch { }
							}
						}
					}
					catch { }

					for (int row = 0; row < 4; row++)
					{
						int capturedRow = row;
						int rowIndex = row + 1;
						var cellMargin = new Thickness(4, 2, 4, 2);

						var modelCombo = new ComboBox { Margin = cellMargin, MinWidth = 100 };
						foreach (var m in models)
							modelCombo.Items.Add(m);
						modelCombo.SelectedIndex = presetModels[row];

						var thinkingCheck = new CheckBox
						{
							Margin = cellMargin,
							VerticalAlignment = VerticalAlignment.Center,
							HorizontalAlignment = HorizontalAlignment.Center,
							IsChecked = presetThinking[row],
							Visibility = supportsThinking ? Visibility.Visible : Visibility.Collapsed
						};

						var effortCombo = new ComboBox { Margin = cellMargin, MinWidth = 80 };
						foreach (var ef in efforts)
							effortCombo.Items.Add(ef);
						effortCombo.SelectedIndex = presetEfforts[row];
						effortCombo.Visibility = QuickSwitchShowsEffort(agentType, modelCombo.SelectedIndex) ? Visibility.Visible : Visibility.Hidden;

						var capturedEffortCombo = effortCombo;
						var capturedModelCombo = modelCombo;
						modelCombo.SelectionChanged += (s, ev) =>
						{
							capturedEffortCombo.Visibility = QuickSwitchShowsEffort(agentType, capturedModelCombo.SelectedIndex) ? Visibility.Visible : Visibility.Hidden;
							presetModels[capturedRow] = capturedModelCombo.SelectedIndex;
							SettingsManager.SaveQuickSwitchPresets(agentKey, presetModels, presetThinking, presetEfforts);
						};

						var capturedThinkingCheck = thinkingCheck;
						thinkingCheck.Checked += (s, ev) =>
						{
							presetThinking[capturedRow] = true;
							SettingsManager.SaveQuickSwitchPresets(agentKey, presetModels, presetThinking, presetEfforts);
						};
						thinkingCheck.Unchecked += (s, ev) =>
						{
							presetThinking[capturedRow] = false;
							SettingsManager.SaveQuickSwitchPresets(agentKey, presetModels, presetThinking, presetEfforts);
						};

						effortCombo.SelectionChanged += (s, ev) =>
						{
							presetEfforts[capturedRow] = capturedEffortCombo.SelectedIndex;
							SettingsManager.SaveQuickSwitchPresets(agentKey, presetModels, presetThinking, presetEfforts);
						};
						var selectButton = new Button
						{
							Content = "▶",
							Margin = cellMargin,
							Padding = new Thickness(6, 2, 6, 2),
							Cursor = Cursors.Hand,
						};
						selectButton.Click += (s, ev) =>
						{
							presetModels[capturedRow] = capturedModelCombo.SelectedIndex;
							presetThinking[capturedRow] = capturedThinkingCheck.IsChecked == true;
							presetEfforts[capturedRow] = capturedEffortCombo.SelectedIndex;
							SettingsManager.SaveQuickSwitchPresets(agentKey, presetModels, presetThinking, presetEfforts);
							popup.IsOpen = false;
							ExecuteQuickSwitch(capturedRow);
						};

						Grid.SetRow(selectButton, rowIndex); Grid.SetColumn(selectButton, 0);
						Grid.SetRow(modelCombo, rowIndex); Grid.SetColumn(modelCombo, 1);
						Grid.SetRow(thinkingCheck, rowIndex); Grid.SetColumn(thinkingCheck, 2);
						Grid.SetRow(effortCombo, rowIndex); Grid.SetColumn(effortCombo, 3);

						if (!string.IsNullOrEmpty(quickSwitchHotkeys[capturedRow]))
						{
							var hotkeyLabel = new TextBlock
							{
								Text = quickSwitchHotkeys[capturedRow],
								Foreground = fgBrush,
								Margin = cellMargin,
								VerticalAlignment = VerticalAlignment.Center,
								Opacity = 0.6,
							};
							Grid.SetRow(hotkeyLabel, rowIndex); Grid.SetColumn(hotkeyLabel, 4);
							grid.Children.Add(hotkeyLabel);
						}

						grid.Children.Add(selectButton);
						grid.Children.Add(modelCombo);
						grid.Children.Add(thinkingCheck);
						grid.Children.Add(effortCombo);
					}

					outerBorder.Child = grid;
					popup.Child = outerBorder;
					quickSwitchPopup = popup;
					popup.IsOpen = true;
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in QuickSwitchButton_Click: {ex}");
			}
		}

		public void ExecuteQuickSwitch(int presetIndex)
		{
			var agentType = GetQuickSwitchAgentType();
			if (agentType == QuickSwitchAgentType.None) return;
			if (quickSwitchInProgress) return;
			if (presetIndex < 0 || presetIndex > 3) return;
			if (activeTab?.Terminal == null || !activeTab.Terminal.IsRunning) return;
			quickSwitchInProgress = true;

			GetQuickSwitchPresetState(agentType, out var presetModels, out var presetThinking, out var presetEfforts);
			int targetModel = presetModels[presetIndex];
			bool thinking = presetThinking[presetIndex];
			int targetEffort = presetEfforts[presetIndex];

			SettingsManager.SaveQuickSwitchPresets(GetQuickSwitchAgentKey(agentType), presetModels, presetThinking, presetEfforts);

			var terminal = activeTab.Terminal;
			System.Threading.Tasks.Task.Run(async () =>
			{
				try
				{
					if (agentType == QuickSwitchAgentType.Codex)
					{
						terminal.WriteInput("/model " + quickSwitchCodexModels[targetModel] + " " + quickSwitchCodexEfforts[targetEffort]);
						await System.Threading.Tasks.Task.Delay(300);
						terminal.WriteInput("\r");
						return;
					}

					terminal.WriteInput("\x1bt");
					await System.Threading.Tasks.Task.Delay(200);
					terminal.WriteInput(thinking ? "1" : "2");
					await System.Threading.Tasks.Task.Delay(200);

					string thinkingBuffer = null;
					await Dispatcher.InvokeAsync(() =>
					{
						thinkingBuffer = activeTab?.TerminalControl?.ReadEntireBuffer();
					});
					if (thinkingBuffer != null && thinkingBuffer.Contains("Do you want to proceed?"))
					{
						terminal.WriteInput("\r");
						await System.Threading.Tasks.Task.Delay(200);
					}

					terminal.WriteInput("/model " + quickSwitchClaudeModelIds[targetModel]);
					await System.Threading.Tasks.Task.Delay(200);
					terminal.WriteInput("\r");

					if (targetModel == 0)
					{
						for (int wait = 0; wait < 50; wait++)
						{
							await System.Threading.Tasks.Task.Delay(200);
							string buf = null;
							await Dispatcher.InvokeAsync(() =>
							{
								buf = activeTab?.TerminalControl?.ReadEntireBuffer();
							});
							if (buf != null && buf.Contains("─────\r\n> \r\n─────") && buf.Contains("⎿  Set model to "))
								break;
						}
						terminal.WriteInput("/effort " + quickSwitchClaudeEffortIds[targetEffort]);
						await System.Threading.Tasks.Task.Delay(200);
						terminal.WriteInput("\r");
					}
				}
				finally
				{
					quickSwitchInProgress = false;
				}
			});
		}

		private void ApplyFontSize(AgentTab tab)
		{
			try
			{
				var theme = GetTerminalTheme();
				var bgColor = GetThemeBackgroundColor();
				tab.TerminalControl.SetTheme(theme, "Consolas", currentFontSize, bgColor);
				tab.TerminalControl.Background = new SolidColorBrush(bgColor);
				this.Background = new SolidColorBrush(bgColor);
				AgentTabs.Background = new SolidColorBrush(bgColor);
				UpdateToolbarColors();

				UpdateTerminalMaxWidth(tab);

				if (tab.TerminalControl.ActualHeight > 0 && tab.TerminalControl.ActualWidth > 0)
				{
					var size = new Size(tab.TerminalControl.ActualWidth, tab.TerminalControl.ActualHeight);
					tab.TerminalControl.TriggerResize(size);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in ApplyFontSize: {ex}");
			}
		}

		private void ApplyFontSizeToAll()
		{
			foreach (var tab in agentTabs)
			{
				ApplyFontSize(tab);
			}
		}

		private void UpdateTerminalMaxWidth(AgentTab tab)
		{
			// typically, it's +80 Width per font size but there's a trap at 16 where it's +3 compared to 14 and so requires + (3 * 80) instead of + (2 * 80)
			if (currentFontSize == 8)
				tab.TerminalBorder.Width = 740.0;
			else if (currentFontSize == 9)
				tab.TerminalBorder.Width = 820.0;
			else if (currentFontSize == 10)
				tab.TerminalBorder.Width = 900.0;
			else if (currentFontSize == 11)
				tab.TerminalBorder.Width = 980.0;
			else if (currentFontSize == 12)
				tab.TerminalBorder.Width = 1060.0;
			else if (currentFontSize == 14)
				tab.TerminalBorder.Width = 1220.0;
			else if (currentFontSize == 16)
				tab.TerminalBorder.Width = 1460.0;
			else if (currentFontSize == 18)
				tab.TerminalBorder.Width = 1620.0;
			else if (currentFontSize == 20)
				tab.TerminalBorder.Width = 1780.0;
			else if (currentFontSize == 24)
				tab.TerminalBorder.Width = 2100.0;
		}

		private void ThemeButton_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				var dialog = new ThemeDialog(currentTheme);
				dialog.Owner = Application.Current?.MainWindow;
				if (dialog.ShowDialog() == true)
				{
					string newTheme = dialog.SelectedTheme;
					if (newTheme != currentTheme)
					{
						currentTheme = newTheme;
						SettingsManager.SaveTheme(currentTheme);
						foreach (var tab in agentTabs)
						{
							tab.NeedsResizeAfterOutput = true;
							string projectDir = GetActiveProjectDirectory();
							if (!string.IsNullOrEmpty(projectDir))
							{
								tab.CurrentSolutionPath = projectDir;
								RestartClaudeWithWorkingDirectory(tab, projectDir);
							}
							else
							{
								tab.CurrentSolutionPath = null;
								StopClaude(tab);
								InitializeConPtyTerminal(tab);
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in ThemeButton_Click: {ex}");
			}
		}

		private void ApplyTheme(AgentTab tab)
		{
			try
			{
				var theme = GetTerminalTheme();
				var bgColor = GetThemeBackgroundColor();
				tab.TerminalControl.SetTheme(theme, "Consolas", currentFontSize, bgColor);
				tab.TerminalControl.Background = new SolidColorBrush(bgColor);
				this.Background = new SolidColorBrush(bgColor);
				UpdateToolbarColors();

				if (tab.TerminalControl.ActualHeight > 0 && tab.TerminalControl.ActualWidth > 0)
				{
					var size = new Size(tab.TerminalControl.ActualWidth, tab.TerminalControl.ActualHeight);
					tab.TerminalControl.TriggerResize(size);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in ApplyTheme: {ex}");
			}
		}

		private void ApplyThemeToAll()
		{
			foreach (var tab in agentTabs)
			{
				ApplyTheme(tab);
			}
		}

		private Color GetThemeBackgroundColor()
		{
			string effectiveTheme = currentTheme;
			if (effectiveTheme == "System")
			{
				effectiveTheme = IsSystemDarkMode() ? "Dark" : "Light";
			}

			if (effectiveTheme == "Light")
			{
				return Color.FromRgb(0xFF, 0xFF, 0xFF);
			}
			else
			{
				return Color.FromRgb(0x1e, 0x1e, 0x1e);
			}
		}

		private void UpdateToolbarColors()
		{
			string effectiveTheme = currentTheme;
			if (effectiveTheme == "System")
			{
				effectiveTheme = IsSystemDarkMode() ? "Dark" : "Light";
			}

			SolidColorBrush toolbarBg, buttonBg, buttonFg, buttonBorder;

			if (effectiveTheme == "Light")
			{
				toolbarBg = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
				buttonBg = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));
				buttonFg = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
				buttonBorder = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));

				MicButton.Style = (Style)FindResource("LightToggleButtonStyle");
				ChangeCommandButton.Style = (Style)FindResource("LightButtonStyle");
				RestartAgentButton.Style = (Style)FindResource("LightButtonStyle");
				NewAgentButton.Style = (Style)FindResource("LightButtonStyle");
				ThemeButton.Style = (Style)FindResource("LightButtonStyle");
				FontSizeButton.Style = (Style)FindResource("LightButtonStyle");
				QuickSwitchButton.Style = (Style)FindResource("LightButtonStyle");
			}
			else
			{
				toolbarBg = new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x0C));
				buttonBg = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
				buttonFg = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4));
				buttonBorder = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

				MicButton.Style = (Style)FindResource("DarkToggleButtonStyle");
				ChangeCommandButton.Style = (Style)FindResource("DarkButtonStyle");
				RestartAgentButton.Style = (Style)FindResource("DarkButtonStyle");
				NewAgentButton.Style = (Style)FindResource("DarkButtonStyle");
				ThemeButton.Style = (Style)FindResource("DarkButtonStyle");
				FontSizeButton.Style = (Style)FindResource("DarkButtonStyle");
				QuickSwitchButton.Style = (Style)FindResource("DarkButtonStyle");
			}

			ToolbarBorder.Background = toolbarBg;

			MicButton.Background = buttonBg;
			MicButton.Foreground = buttonFg;
			MicButton.BorderBrush = buttonBorder;

			ChangeCommandButton.Background = buttonBg;
			ChangeCommandButton.Foreground = buttonFg;
			ChangeCommandButton.BorderBrush = buttonBorder;

			RestartAgentButton.Background = buttonBg;
			RestartAgentButton.Foreground = buttonFg;
			RestartAgentButton.BorderBrush = buttonBorder;

			NewAgentButton.Background = buttonBg;
			NewAgentButton.Foreground = buttonFg;
			NewAgentButton.BorderBrush = buttonBorder;

			ThemeButton.Background = buttonBg;
			ThemeButton.Foreground = buttonFg;
			ThemeButton.BorderBrush = buttonBorder;

			FontSizeButton.Background = buttonBg;
			FontSizeButton.Foreground = buttonFg;
			FontSizeButton.BorderBrush = buttonBorder;

			QuickSwitchButton.Background = buttonBg;
			QuickSwitchButton.Foreground = buttonFg;
			QuickSwitchButton.BorderBrush = buttonBorder;

			var tabItemStyle = (Style)FindResource(effectiveTheme == "Light" ? "LightTabItemStyle" : "DarkTabItemStyle");
			var closeButtonStyle = (Style)FindResource(effectiveTheme == "Light" ? "LightTabCloseButtonStyle" : "DarkTabCloseButtonStyle");
			foreach (var tab in agentTabs)
			{
				tab.TabItem.Style = tabItemStyle;
				var headerPanel = tab.TabItem.Header as StackPanel;
				if (headerPanel != null && headerPanel.Children.Count >= 2)
				{
					var closeButton = headerPanel.Children[1] as Button;
					if (closeButton != null)
					{
						closeButton.Style = closeButtonStyle;
					}
				}
			}
		}

		private TerminalTheme GetTerminalTheme()
		{
			string effectiveTheme = currentTheme;
			if (effectiveTheme == "System")
			{
				effectiveTheme = IsSystemDarkMode() ? "Dark" : "Light";
			}

			if (effectiveTheme == "Light")
			{
				return new TerminalTheme
				{
					DefaultBackground = 0xFFFFFFFF,
					DefaultForeground = 0xFF000000,
					DefaultSelectionBackground = 0xFF0078D7,
					CursorStyle = CursorStyle.BlinkingBar,
					ColorTable = new uint[]
					{
						0xFFFFFFFF, 0xFFC50F1F, 0xFF13A10E, 0xFFC19C00,
						0xFF0037DA, 0xFF881798, 0xFF3A96DD, 0xFF000000,
						0xFFFFFFFF, 0xFFE74856, 0xFF16C60C, 0xFFF9F1A5,
						0xFF3B78FF, 0xFFB4009E, 0xFF61D6D6, 0xFF000000
					}
				};
			}
			else
			{
				return new TerminalTheme
				{
					DefaultBackground = 0xFF1e1e1e,
					DefaultForeground = 0xFFd4d4d4,
					DefaultSelectionBackground = 0xFF264F78,
					CursorStyle = CursorStyle.BlinkingBar,
					ColorTable = new uint[]
					{
						0xFF1e1e1e, 0xFFC50F1F, 0xFF13A10E, 0xFFC19C00,
						0xFF0037DA, 0xFF881798, 0xFF3A96DD, 0xFFCCCCCC,
						0xFF1e1e1e, 0xFFE74856, 0xFF16C60C, 0xFFF9F1A5,
						0xFF3B78FF, 0xFFB4009E, 0xFF61D6D6, 0xFFF2F2F2
					}
				};
			}
		}

		private bool IsSystemDarkMode()
		{
			try
			{
				using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
				{
					if (key != null)
					{
						var value = key.GetValue("AppsUseLightTheme");
						if (value is int intValue)
						{
							return intValue == 0;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in IsSystemDarkMode: {ex}");
			}
			return true;
		}

		private void MicButton_Checked(object sender, RoutedEventArgs e)
		{
			try
			{
				var speechCommand = SpeechCommand.Instance;
				if (speechCommand != null)
				{
					speechCommand.ListeningStateChanged -= OnListeningStateChanged;
					speechCommand.ListeningStateChanged += OnListeningStateChanged;
					speechCommand.StartListening();
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in MicButton_Checked: {ex}");
				MicButton.IsChecked = false;
			}
		}

		private void MicButton_Unchecked(object sender, RoutedEventArgs e)
		{
			try
			{
				var speechCommand = SpeechCommand.Instance;
				if (speechCommand != null && speechCommand.IsListening)
				{
					_ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
					{
						await speechCommand.StopListeningAsync();
					});
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Exception in MicButton_Unchecked: {ex}");
			}
		}

		private void OnListeningStateChanged(bool isListening)
		{
			Dispatcher.BeginInvoke(new Action(() =>
			{
				MicButton.IsChecked = isListening;
			}));
		}

	}

}
