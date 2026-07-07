using NetFwTypeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

// Disambiguate types that clash with System.Windows.Forms equivalents
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace Ronin_Portier
{
    public partial class MainWindow : Window
    {
        private System.Collections.ObjectModel.ObservableCollection<GameServer> _serverList;
        private bool _isUpdatingSelection = false;
        private System.Windows.Forms.NotifyIcon _trayIcon;

        public MainWindow()
        {
            InitializeComponent();
            ConsoleRTB.Document.Blocks.Clear();

            SetupTrayIcon();

            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "servers.json");

            if (File.Exists(filePath))
            {
                try
                {
                    string jsonString = File.ReadAllText(filePath);
                    var savedServers = JsonSerializer.Deserialize<List<GameServer>>(jsonString);
                    _serverList = new System.Collections.ObjectModel.ObservableCollection<GameServer>(savedServers ?? new());
                    WriteLog("Loaded saved servers from disk.", "info");
                }
                catch (Exception ex)
                {
                    _serverList = new System.Collections.ObjectModel.ObservableCollection<GameServer>();
                    WriteLog($"Error loading servers: {ex.Message}", "warning");
                }
            }
            else
            {
                _serverList = new System.Collections.ObjectModel.ObservableCollection<GameServer>();
            }

            ServerCombo.ItemsSource = _serverList;
            ResetFields();
            RefreshAllStatuses();
            WriteLog("Ronin Portier initialized. Ready to manage firewall rules.", "info");
        }

        //      ____  _   _ _____ _____ ___  _   _ ____
        //     | __ )| | | |_   _|_   _/ _ \| \ | / ___|
        //     |  _ \| | | | | |   | || | | |  \| \___ \
        //     | |_) | |_| | | |   | || |_| | |\  |___) |
        //     |____/ \___/  |_|   |_| \___/|_| \_|____/

        private void btnApply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string currentName = ServerCombo.Text;
                string currentPorts = txtPorts.Text.Replace(" ", "");

                if (string.IsNullOrWhiteSpace(currentName) || string.IsNullOrWhiteSpace(currentPorts))
                {
                    MessageBox.Show("Please enter both a Rule Name and Ports.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (chkTCP.IsChecked != true && chkUDP.IsChecked != true)
                {
                    MessageBox.Show("Please select at least one protocol (TCP or UDP).", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Port conflict detection
                var conflicts = GetConflictingRules(currentPorts, currentName);
                if (conflicts.Count > 0)
                {
                    string conflictList = string.Join("\n  • ", conflicts);
                    var proceed = MessageBox.Show(
                        $"The following existing rules overlap with one or more of your ports:\n\n  • {conflictList}\n\nProceed anyway?",
                        "Port Conflict Detected", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (proceed == MessageBoxResult.No) return;
                }

                Type fwPolicy2Type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                INetFwPolicy2 fwPolicy2 = (INetFwPolicy2)Activator.CreateInstance(fwPolicy2Type);

                // Remove any existing rules for this profile
                var rulesToRemove = new List<string>();
                foreach (INetFwRule rule in fwPolicy2.Rules)
                {
                    if (rule.Name != null && rule.Name.StartsWith(currentName))
                    {
                        rulesToRemove.Add(rule.Name);
                        WriteLog($"Marked existing rule '{rule.Name}' for removal.", "info");
                    }
                }
                foreach (var name in rulesToRemove)
                {
                    fwPolicy2.Rules.Remove(name);
                    WriteLog($"Existing rule '{name}' removed.", "info");
                }

                bool useOutbound = chkOutbound.IsChecked == true;

                // Create inbound (and optionally outbound) rules
                if (chkTCP.IsChecked == true)
                {
                    try
                    {
                        CreateFirewallRule($"{currentName} - TCP", currentPorts, NET_FW_IP_PROTOCOL_.NET_FW_IP_PROTOCOL_TCP, NET_FW_RULE_DIRECTION_.NET_FW_RULE_DIR_IN);
                        WriteLog($"Inbound TCP rule applied for ports: {currentPorts}", "success");
                    }
                    catch (Exception ex) { WriteLog($"Error applying inbound TCP rule: {ex.Message}", "error"); }

                    if (useOutbound)
                    {
                        try
                        {
                            CreateFirewallRule($"{currentName} - TCP (Outbound)", currentPorts, NET_FW_IP_PROTOCOL_.NET_FW_IP_PROTOCOL_TCP, NET_FW_RULE_DIRECTION_.NET_FW_RULE_DIR_OUT);
                            WriteLog($"Outbound TCP rule applied for ports: {currentPorts}", "success");
                        }
                        catch (Exception ex) { WriteLog($"Error applying outbound TCP rule: {ex.Message}", "error"); }
                    }
                }

                if (chkUDP.IsChecked == true)
                {
                    try
                    {
                        CreateFirewallRule($"{currentName} - UDP", currentPorts, NET_FW_IP_PROTOCOL_.NET_FW_IP_PROTOCOL_UDP, NET_FW_RULE_DIRECTION_.NET_FW_RULE_DIR_IN);
                        WriteLog($"Inbound UDP rule applied for ports: {currentPorts}", "success");
                    }
                    catch (Exception ex) { WriteLog($"Error applying inbound UDP rule: {ex.Message}", "error"); }

                    if (useOutbound)
                    {
                        try
                        {
                            CreateFirewallRule($"{currentName} - UDP (Outbound)", currentPorts, NET_FW_IP_PROTOCOL_.NET_FW_IP_PROTOCOL_UDP, NET_FW_RULE_DIRECTION_.NET_FW_RULE_DIR_OUT);
                            WriteLog($"Outbound UDP rule applied for ports: {currentPorts}", "success");
                        }
                        catch (Exception ex) { WriteLog($"Error applying outbound UDP rule: {ex.Message}", "error"); }
                    }
                }

                // Save or update the profile
                var existing = _serverList.FirstOrDefault(s => s.Name == currentName);
                if (existing != null)
                {
                    existing.Ports = currentPorts;
                    existing.UseTCP = chkTCP.IsChecked ?? false;
                    existing.UseUDP = chkUDP.IsChecked ?? false;
                    existing.UseOutbound = useOutbound;
                    existing.IsActive = true;
                    SaveServers();
                    WriteLog($"Updated profile '{currentName}'.", "success");
                }
                else
                {
                    var newServer = new GameServer
                    {
                        Name = currentName,
                        Ports = currentPorts,
                        UseTCP = chkTCP.IsChecked ?? false,
                        UseUDP = chkUDP.IsChecked ?? false,
                        UseOutbound = useOutbound,
                        IsActive = true
                    };
                    _serverList.Add(newServer);
                    SaveServers();
                    WriteLog($"Added '{currentName}' to saved servers.", "success");
                }

                UpdateStatusBar();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying firewall rules: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            if (MessageBox.Show("Clear fields for next entry?", "Done", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                ResetFields();
        }

        public void btnRemove_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string ruleName = ServerCombo.Text;

                if (string.IsNullOrWhiteSpace(ruleName))
                {
                    MessageBox.Show("Please enter a Rule Name to remove.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Type fwPolicy2Type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                INetFwPolicy2 fwPolicy2 = (INetFwPolicy2)Activator.CreateInstance(fwPolicy2Type);

                var rulesToRemove = new List<string>();
                foreach (INetFwRule rule in fwPolicy2.Rules)
                {
                    try
                    {
                        if (rule.Name != null && rule.Name.StartsWith(ruleName))
                        {
                            rulesToRemove.Add(rule.Name);
                            WriteLog($"Marked rule '{rule.Name}' for removal.", "info");
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Error checking rule: {ex.Message}", "error");
                    }
                }

                foreach (var name in rulesToRemove)
                {
                    try
                    {
                        fwPolicy2.Rules.Remove(name);
                        WriteLog($"Rule '{name}' removed.", "success");
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Error removing rule '{name}': {ex.Message}", "error");
                    }
                }

                var serverInList = _serverList.FirstOrDefault(s => s.Name == ruleName);
                if (serverInList != null)
                {
                    _serverList.Remove(serverInList);
                    SaveServers();
                    WriteLog($"Removed '{ruleName}' from saved profiles.", "success");
                }

                UpdateStatusBar();
                MessageBox.Show("Firewall rules and saved configuration removed!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during removal: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            if (MessageBox.Show("Clear fields for next entry?", "Done", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                ResetFields();
        }

        private void btnDuplicate_Click(object sender, RoutedEventArgs e)
        {
            string currentName = ServerCombo.Text;
            var source = _serverList.FirstOrDefault(s => s.Name == currentName);

            if (source == null)
            {
                MessageBox.Show("Select a saved profile to duplicate.", "No Profile Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string newName = $"{source.Name} (Copy)";
            int count = 1;
            while (_serverList.Any(s => s.Name == newName))
                newName = $"{source.Name} (Copy {++count})";

            _serverList.Add(new GameServer
            {
                Name = newName,
                Ports = source.Ports,
                UseTCP = source.UseTCP,
                UseUDP = source.UseUDP,
                UseOutbound = source.UseOutbound,
                IsActive = false
            });

            SaveServers();
            UpdateStatusBar();
            WriteLog($"Duplicated '{source.Name}' as '{newName}'.", "success");
        }

        //      _   _ _____ _     ____  _____ ____  ____
        //     | | | | ____| |   |  _ \| ____|  _ \/ ___|
        //     | |_| |  _| | |   | |_) |  _| | |_) \___ \
        //     |  _  | |___| |___|  __/| |___|  _ < ___) |
        //     |_| |_|_____|_____|_|   |_____|_| \_\____/

        private void CreateFirewallRule(string name, string ports, NET_FW_IP_PROTOCOL_ protocol,
            NET_FW_RULE_DIRECTION_ direction = NET_FW_RULE_DIRECTION_.NET_FW_RULE_DIR_IN)
        {
            Type fwPolicy2Type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            INetFwPolicy2 fwPolicy2 = (INetFwPolicy2)Activator.CreateInstance(fwPolicy2Type);

            Type fwRuleType = Type.GetTypeFromProgID("HNetCfg.FWRule");
            INetFwRule rule = (INetFwRule)Activator.CreateInstance(fwRuleType);

            rule.Name = name;
            rule.Protocol = (int)protocol;
            rule.LocalPorts = ports;
            rule.Direction = direction;
            rule.Action = NET_FW_ACTION_.NET_FW_ACTION_ALLOW;
            rule.Enabled = true;
            rule.Profiles = 7;                              // 1 = Domain, 2 = Private, 4 = Public (7 = all)
            rule.Grouping = "Ronin Portier Rules";
            rule.Description = $"Created by Ronin Portier for ({name}) on ports: {ports}";

            fwPolicy2.Rules.Add(rule);
        }

        private void SaveServers()
        {
            try
            {
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "servers.json");
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(_serverList, options);
                File.WriteAllText(filePath, jsonString);
                WriteLog("Server list saved to disk.", "info");
            }
            catch (Exception ex)
            {
                WriteLog($"Failed to save servers: {ex.Message}", "error");
            }
        }

        // Check if a profile's rules currently exist in Windows Firewall (efficient single-pass)
        private void RefreshAllStatuses()
        {
            try
            {
                Type fwPolicy2Type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                INetFwPolicy2 fwPolicy2 = (INetFwPolicy2)Activator.CreateInstance(fwPolicy2Type);

                // Collect all active Ronin rule name prefixes in one pass
                var activeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (INetFwRule rule in fwPolicy2.Rules)
                {
                    if (rule.Name != null && rule.Grouping == "Ronin Portier Rules")
                        activeNames.Add(rule.Name);
                }

                foreach (var server in _serverList)
                    server.IsActive = activeNames.Any(n => n.StartsWith(server.Name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                WriteLog($"Could not check firewall status: {ex.Message}", "warning");
            }

            UpdateStatusBar();
        }

        private void UpdateStatusBar()
        {
            int active = _serverList.Count(s => s.IsActive);
            StatusBarText.Text = $"Profiles: {_serverList.Count}  |  Active: {active}";
        }

        // Check if any of the ports being applied conflict with existing (non-Ronin) rules
        private List<string> GetConflictingRules(string ports, string currentName)
        {
            var conflicts = new List<string>();
            try
            {
                var portSet = ExpandPorts(ports);
                if (portSet.Count == 0) return conflicts;

                Type fwPolicy2Type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                INetFwPolicy2 fwPolicy2 = (INetFwPolicy2)Activator.CreateInstance(fwPolicy2Type);

                foreach (INetFwRule rule in fwPolicy2.Rules)
                {
                    // Skip the current profile's own rules and wildcard-port rules
                    if (rule.Name == null || rule.Name.StartsWith(currentName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (rule.LocalPorts == null || rule.LocalPorts == "*") continue;

                    var existing = ExpandPorts(rule.LocalPorts);
                    if (portSet.Overlaps(existing))
                        conflicts.Add(rule.Name);
                }
            }
            catch { /* non-critical — skip silently */ }
            return conflicts;
        }

        // Expand a port string like "27015-27030,27036" into a flat set of ints
        private HashSet<int> ExpandPorts(string portString)
        {
            var result = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(portString)) return result;

            foreach (var part in portString.Split(','))
            {
                var trimmed = part.Trim();
                if (trimmed.Contains('-'))
                {
                    var sides = trimmed.Split('-');
                    if (int.TryParse(sides[0], out int start) && int.TryParse(sides[1], out int end))
                        for (int p = start; p <= Math.Min(end, start + 5000); p++) // cap at 5000 to avoid huge ranges
                            result.Add(p);
                }
                else if (int.TryParse(trimmed, out int port))
                    result.Add(port);
            }
            return result;
        }

        // Set up system tray icon — app minimizes to tray on close
        private void SetupTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon();

            try
            {
                var iconStream = System.Windows.Application.GetResourceStream(
                    new Uri("pack://application:,,,/assets/images/RoninLogo.ico"))?.Stream;
                if (iconStream != null)
                    _trayIcon.Icon = new System.Drawing.Icon(iconStream);
            }
            catch { /* icon not critical */ }

            _trayIcon.Text = "Ronin Portier";
            _trayIcon.Visible = false;

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open Ronin Portier", null, (s, ev) => RestoreFromTray());
            menu.Items.Add("-");
            menu.Items.Add("Exit", null, (s, ev) =>
            {
                _trayIcon.Dispose();
                System.Windows.Application.Current.Shutdown();
            });
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (s, ev) => RestoreFromTray();
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            _trayIcon.Visible = false;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
            _trayIcon.Visible = true;
            _trayIcon.ShowBalloonTip(2000, "Ronin Portier",
                "Running in background. Double-click to restore.", System.Windows.Forms.ToolTipIcon.Info);
        }

        private void ServerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingSelection) return;

            if (e.AddedItems.Count > 0 && e.AddedItems[0] is GameServer selected)
            {
                txtPorts.Text = selected.Ports;
                chkTCP.IsChecked = selected.UseTCP;
                chkUDP.IsChecked = selected.UseUDP;
                chkOutbound.IsChecked = selected.UseOutbound;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ServerCombo.Text = selected.Name;
                    if (ServerCombo.Template.FindName("PART_EditableTextBox", ServerCombo) is TextBox tb)
                        tb.CaretIndex = tb.Text.Length;
                }));

                WriteLog($"Loaded profile: {selected.Name}", "info");
            }
        }

        private void ResetFields()
        {
            _isUpdatingSelection = true;
            ServerCombo.SelectedIndex = -1;
            ServerCombo.Text = string.Empty;
            txtPorts.Text = string.Empty;
            chkTCP.IsChecked = false;
            chkUDP.IsChecked = false;
            chkOutbound.IsChecked = false;
            _isUpdatingSelection = false;
            ServerCombo.Focus();
        }

        private void WriteLog(string message, string level)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Paragraph paragraph = new Paragraph(new Run($"[{timestamp}] [{level.ToUpper()}] {message}"));
            paragraph.Foreground = level.ToLower() switch
            {
                "info"    => System.Windows.Media.Brushes.White,
                "success" => System.Windows.Media.Brushes.LightGreen,
                "warning" => System.Windows.Media.Brushes.Orange,
                "error"   => System.Windows.Media.Brushes.Red,
                _         => System.Windows.Media.Brushes.White,
            };
            ConsoleRTB.Document.Blocks.Add(paragraph);
            ConsoleRTB.ScrollToEnd();
        }
    }

    public class GameServer
    {
        public string Name { get; set; }
        public string Ports { get; set; }
        public bool UseTCP { get; set; }
        public bool UseUDP { get; set; }
        public bool UseOutbound { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsActive { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string StatusIcon => IsActive ? "🟢" : "⚫";

        public override string ToString()
        {
            string tcpIcon  = UseTCP ? "🔹TCP" : "";
            string udpIcon  = UseUDP ? "🔸UDP" : "";
            string spacer   = (UseTCP && UseUDP) ? " | " : "";
            string outIcon  = UseOutbound ? " ↕" : "";
            return $"{StatusIcon} {Name} — {Ports}  [{tcpIcon}{spacer}{udpIcon}{outIcon}]";
        }
    }
}
