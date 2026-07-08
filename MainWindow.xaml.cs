using NetFwTypeLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace Ronin_Portier
{
    public partial class MainWindow : Window
    {
        private System.Collections.ObjectModel.ObservableCollection<GameServer> _serverList;
        private System.Collections.ObjectModel.ObservableCollection<FirewallRuleInfo> _allRules
            = new System.Collections.ObjectModel.ObservableCollection<FirewallRuleInfo>();

        private ICollectionView _allRulesView;
        private ICollectionView _portierView;

        private Dictionary<int, string> _portProcessMap = new Dictionary<int, string>();
        private GameServer _editingServer;
        private FirewallRuleInfo _selectedForeignRule;

        public MainWindow()
        {
            InitializeComponent();
            ConsoleRTB.Document.Blocks.Clear();

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

            _allRulesView = CollectionViewSource.GetDefaultView(_allRules);
            _allRulesView.Filter = FilterRule;
            AllRulesGrid.ItemsSource = _allRulesView;

            _portierView = CollectionViewSource.GetDefaultView(_serverList);
            _portierView.Filter = FilterServer;
            PortierGrid.ItemsSource = _portierView;

            ShowAddPanel();
            WriteLog("Ronin Portier initialized. Ready to manage firewall rules.", "info");

            _ = RefreshAllAsync();
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
                string currentName = txtRuleName.Text.Trim();
                string currentPorts = txtRulePorts.Text.Replace(" ", "");

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

                ShowAddPanel();
                RefreshListsOnly();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying firewall rules: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void btnRemove_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string ruleName = txtRuleName.Text.Trim();

                if (string.IsNullOrWhiteSpace(ruleName))
                {
                    MessageBox.Show("Please select a profile to remove.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var proceed = MessageBox.Show(
                    $"Remove all firewall rules and the saved profile for '{ruleName}'?",
                    "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (proceed != MessageBoxResult.Yes) return;

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

                ShowAddPanel();
                RefreshListsOnly();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during removal: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnDuplicate_Click(object sender, RoutedEventArgs e)
        {
            var source = _editingServer ?? _serverList.FirstOrDefault(s => s.Name == txtRuleName.Text.Trim());

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
            RefreshListsOnly();
            WriteLog($"Duplicated '{source.Name}' as '{newName}'.", "success");
        }

        private void btnUseAsTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedForeignRule == null) return;
            var template = _selectedForeignRule;

            AllRulesGrid.SelectedItem = null;
            ShowAddPanel();
            txtRuleName.Text = template.Name;
            txtRulePorts.Text = template.LocalPorts == "*" ? "" : template.LocalPorts;
            chkTCP.IsChecked = template.Protocol == "TCP";
            chkUDP.IsChecked = template.Protocol == "UDP";
            WriteLog($"Loaded '{template.Name}' as a template for a new rule.", "info");
        }

        private void btnRemoveForeign_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedForeignRule == null) return;
            var rule = _selectedForeignRule;

            string processInfo = string.IsNullOrEmpty(rule.ProcessName)
                ? "No process is currently listening on this port."
                : $"Process '{rule.ProcessName}' is currently using this port and will stop working.";

            var result = MessageBox.Show(
                $"Remove firewall rule '{rule.Name}' (port(s) {rule.LocalPorts})?\n\n{processInfo}\n\n" +
                "This will permanently remove the rule unless it is recreated and the affected process is restarted.",
                "Confirm Rule Removal", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                Type fwPolicy2Type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                INetFwPolicy2 fwPolicy2 = (INetFwPolicy2)Activator.CreateInstance(fwPolicy2Type);
                fwPolicy2.Rules.Remove(rule.Name);
                WriteLog($"Removed firewall rule '{rule.Name}'.", "success");
            }
            catch (Exception ex)
            {
                WriteLog($"Error removing rule '{rule.Name}': {ex.Message}", "error");
                MessageBox.Show($"Error removing rule: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ShowAddPanel();
            RefreshListsOnly();
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

        // Full refresh: re-scans live listening ports (background thread) then re-enumerates
        // Windows Firewall rules (must stay on the UI/STA thread — these are COM objects).
        private async Task RefreshAllAsync()
        {
            SearchBarStatus.Text = "Scanning firewall rules and live ports...";
            RefreshBtn.IsEnabled = false;
            try
            {
                _portProcessMap = await Task.Run(() => PortLookup.GetPortProcessMap());
                RefreshListsOnly();
                SearchBarStatus.Text = $"{_allRules.Count} rule(s) shown  •  updated {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                WriteLog($"Error scanning firewall/ports: {ex.Message}", "error");
                SearchBarStatus.Text = "Scan failed. See console.";
            }
            finally
            {
                RefreshBtn.IsEnabled = true;
            }
        }

        // Cheap refresh that reuses the last live-port scan — used after Apply/Remove/Duplicate
        // so those actions feel instant instead of re-scanning the whole system every time.
        private void RefreshListsOnly()
        {
            RefreshFirewallRulesList(_portProcessMap);
            RefreshAllStatuses();
            UpdateStatusBar();
            _allRulesView?.Refresh();
            _portierView?.Refresh();
        }

        // Enumerate every Windows Firewall rule into the "Firewall Rules" grid, tagging which
        // ones Portier owns and which live process (if any) is currently using each rule's ports.
        private void RefreshFirewallRulesList(Dictionary<int, string> portMap)
        {
            _allRules.Clear();
            try
            {
                Type fwPolicy2Type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                INetFwPolicy2 fwPolicy2 = (INetFwPolicy2)Activator.CreateInstance(fwPolicy2Type);
                bool showAll = chkShowAll.IsChecked == true;

                foreach (INetFwRule rule in fwPolicy2.Rules)
                {
                    try
                    {
                        bool hasPorts = !string.IsNullOrWhiteSpace(rule.LocalPorts) && rule.LocalPorts != "*";
                        if (!showAll && (!rule.Enabled || !hasPorts)) continue;

                        _allRules.Add(new FirewallRuleInfo
                        {
                            Name = rule.Name ?? "",
                            Direction = rule.Direction == NET_FW_RULE_DIRECTION_.NET_FW_RULE_DIR_IN ? "In" : "Out",
                            Protocol = ProtocolName(rule.Protocol),
                            LocalPorts = rule.LocalPorts ?? "*",
                            Profile = ProfileName(rule.Profiles),
                            Enabled = rule.Enabled,
                            IsPortierManaged = rule.Grouping == "Ronin Portier Rules",
                            ProcessName = hasPorts ? FindProcessForPorts(rule.LocalPorts, portMap) : ""
                        });
                    }
                    catch { /* skip rules that throw on read (some store-app rules do) */ }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Could not enumerate firewall rules: {ex.Message}", "warning");
            }
        }

        private static string ProtocolName(int protocol) => protocol switch
        {
            6 => "TCP",
            17 => "UDP",
            256 => "Any",
            _ => protocol.ToString()
        };

        private static string ProfileName(int profiles)
        {
            if (profiles == 7 || profiles == 0x7FFFFFFF) return "All";
            var parts = new List<string>();
            if ((profiles & 1) != 0) parts.Add("Domain");
            if ((profiles & 2) != 0) parts.Add("Private");
            if ((profiles & 4) != 0) parts.Add("Public");
            return parts.Count > 0 ? string.Join("/", parts) : profiles.ToString();
        }

        // Finds the first live process bound to any port within the given port string.
        private string FindProcessForPorts(string portsString, Dictionary<int, string> portMap)
        {
            if (portMap == null || portMap.Count == 0 || string.IsNullOrWhiteSpace(portsString)) return "";
            foreach (var p in ExpandPorts(portsString))
                if (portMap.TryGetValue(p, out var name) && !string.IsNullOrEmpty(name))
                    return name;
            return "";
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
                {
                    server.IsActive = activeNames.Any(n => n.StartsWith(server.Name, StringComparison.OrdinalIgnoreCase));
                    server.ProcessName = FindProcessForPorts(server.Ports, _portProcessMap);
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Could not check firewall status: {ex.Message}", "warning");
            }
        }

        private void UpdateStatusBar()
        {
            int active = _serverList.Count(s => s.IsActive);
            StatusBarText.Text = $"Profiles: {_serverList.Count}  |  Active: {active}  |  Firewall Rules: {_allRules.Count}";
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

        //      _   _ ___
        //     | | | |_ _|
        //     | | | || |
        //     | |_| || |
        //      \___/|___|

        private bool FilterRule(object obj)
        {
            if (obj is not FirewallRuleInfo r) return false;
            return MatchesSearch(r.Name, r.LocalPorts, r.ProcessName);
        }

        private bool FilterServer(object obj)
        {
            if (obj is not GameServer s) return false;
            return MatchesSearch(s.Name, s.Ports, s.ProcessName);
        }

        private bool MatchesSearch(string name, string ports, string process)
        {
            string query = txtSearch?.Text?.Trim() ?? "";
            if (query.Length == 0) return true;

            if (!string.IsNullOrEmpty(name) && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(ports) && ports.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(process) && process.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            if (int.TryParse(query, out int port) && !string.IsNullOrWhiteSpace(ports))
                return ExpandPorts(ports).Contains(port);

            return false;
        }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _allRulesView?.Refresh();
            _portierView?.Refresh();
        }

        private void chkShowAll_Changed(object sender, RoutedEventArgs e)
        {
            RefreshFirewallRulesList(_portProcessMap);
            UpdateStatusBar();
            _allRulesView?.Refresh();
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            _ = RefreshAllAsync();
        }

        // Guard against DataGrid selection changes bubbling up as if the tab itself changed
        // (Selector.SelectionChanged bubbles through the visual tree in WPF).
        private void LeftTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, LeftTabs)) return;
            AllRulesGrid.SelectedItem = null;
            PortierGrid.SelectedItem = null;
            ShowAddPanel();
        }

        private void AllRulesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AllRulesGrid.SelectedItem is not FirewallRuleInfo rule) return;
            PortierGrid.SelectedItem = null;

            if (rule.IsPortierManaged)
            {
                var match = _serverList.FirstOrDefault(s => rule.Name.StartsWith(s.Name, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    ShowPortierEdit(match);
                    return;
                }
            }

            ShowForeignRule(rule);
        }

        private void PortierGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PortierGrid.SelectedItem is not GameServer server) return;
            AllRulesGrid.SelectedItem = null;
            ShowPortierEdit(server);
        }

        private void ShowAddPanel()
        {
            _editingServer = null;
            _selectedForeignRule = null;

            AddEditPanel.Visibility = Visibility.Visible;
            ForeignRulePanel.Visibility = Visibility.Collapsed;

            AddEditHeader.Text = "ADD NEW RULE";
            txtRuleName.Text = "";
            txtRulePorts.Text = "";
            chkTCP.IsChecked = false;
            chkUDP.IsChecked = false;
            chkOutbound.IsChecked = false;
            PortierEditButtons.Visibility = Visibility.Collapsed;
        }

        private void ShowPortierEdit(GameServer server)
        {
            _editingServer = server;
            _selectedForeignRule = null;

            AddEditPanel.Visibility = Visibility.Visible;
            ForeignRulePanel.Visibility = Visibility.Collapsed;

            AddEditHeader.Text = "EDIT PROFILE";
            txtRuleName.Text = server.Name;
            txtRulePorts.Text = server.Ports;
            chkTCP.IsChecked = server.UseTCP;
            chkUDP.IsChecked = server.UseUDP;
            chkOutbound.IsChecked = server.UseOutbound;
            PortierEditButtons.Visibility = Visibility.Visible;

            WriteLog($"Loaded profile: {server.Name}", "info");
        }

        private void ShowForeignRule(FirewallRuleInfo rule)
        {
            _editingServer = null;
            _selectedForeignRule = rule;

            AddEditPanel.Visibility = Visibility.Collapsed;
            ForeignRulePanel.Visibility = Visibility.Visible;

            ForeignName.Text = rule.Name;
            ForeignDetails.Text = $"{rule.Direction}bound  •  {rule.Protocol}  •  Port(s) {rule.LocalPorts}  •  {rule.Profile}";
            ForeignProcess.Text = string.IsNullOrEmpty(rule.ProcessName)
                ? "No process is currently listening on this port."
                : $"Process '{rule.ProcessName}' is currently using this port.";
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
        public string ProcessName { get; set; } = "";

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
