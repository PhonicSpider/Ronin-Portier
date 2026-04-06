using NetFwTypeLib; // Ensure you have the reference added to your project
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Xml.Linq;

namespace Ronin_Portier
{
    public partial class MainWindow : Window
    {
        // A list that WPF can "watch" for changes
        private System.Collections.ObjectModel.ObservableCollection<GameServer> _serverList;

        public MainWindow()
        {
            InitializeComponent();
            ConsoleRTB.Document.Blocks.Clear();

            // --- START LOADING LOGIC ---
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "servers.json");

            if (File.Exists(filePath))
            {
                try
                {
                    string jsonString = File.ReadAllText(filePath);
                    var savedServers = JsonSerializer.Deserialize<System.Collections.Generic.List<GameServer>>(jsonString);
                    _serverList = new System.Collections.ObjectModel.ObservableCollection<GameServer>(savedServers);
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
            // --- END LOADING LOGIC ---

            // Link the list to the UI
            ServerCombo.ItemsSource = _serverList;

            // Selection Change Logic
            ServerCombo.SelectionChanged += (s, e) => {
                if (ServerCombo.SelectedItem is GameServer selected)
                {
                    // This ensures the editable text box only shows the Name
                    ServerCombo.Text = selected.Name;

                    txtPorts.Text = selected.Ports;
                    chkTCP.IsChecked = selected.UseTCP;
                    chkUDP.IsChecked = selected.UseUDP;

                    WriteLog($"Selected Profile: {selected.Name}", "info");
                }
            };

            WriteLog("Ronin Portier initialized. Ready to manage firewall rules.", "info");
        }

        //      ____  _   _ _____ _____ ___  _   _ ____  
        //     | __ )| | | |_   _|_   _/ _ \| \ | / ___| 
        //     |  _ \| | | | | |   | || | | |  \| \___ \ 
        //     | |_) | |_| | | |   | || |_| | |\  |___) |
        //     |____/ \___/  |_|   |_| \___/|_| \_|____/ 
        //                                               

        // Apply Button Click Handler - Applies firewall rules based on the name and ports entered in the text boxes (Handles both TCP and UDP variants)
        private void btnApply_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string currentName = ServerCombo.Text; // Use .Text because it's editable
                string currentports = txtPorts.Text.Replace(" ", ""); // Remove any spaces
                
                if ((string.IsNullOrWhiteSpace(currentName) || currentName == "Select or Type Server Name...") || 
                   (string.IsNullOrWhiteSpace(currentports) || currentports == "Type Your Ports Here..."))
                {
                    MessageBox.Show("Please enter both a Rule Name and Ports.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 1. Initialize the firewall manager
                Type fwPolicy2Type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                INetFwPolicy2 fwPolicy2 = Activator.CreateInstance(fwPolicy2Type) as INetFwPolicy2;

                // 2. Clean up existing rules with the same name (Handles TCP and UDP variants)
                var rulesToRemove = new System.Collections.Generic.List<string>();

                foreach (INetFwRule rule in fwPolicy2.Rules) // First, identify rules to remove
                {
                    if (rule.Name != null && rule.Name.StartsWith(currentName)) // Check for both TCP and UDP variants
                    {
                        rulesToRemove.Add(rule.Name);
                        WriteLog($"Marked existing rule '{rule.Name}' for removal.", "info");
                    }
                }

                foreach (var name in rulesToRemove) // Remove marked rules
                {
                    fwPolicy2.Rules.Remove(name);
                    WriteLog($"Existing rule '{name}' removed successfully.", "info");
                }

                // 3. Create new rules using the helper method
                if (chkTCP.IsChecked == true)
                {
                    try
                    {
                        CreateFirewallRule($"{currentName} - TCP", currentports, NET_FW_IP_PROTOCOL_.NET_FW_IP_PROTOCOL_TCP);
                        WriteLog($"TCP rule '{currentName} - TCP' applied successfully for ports: {currentports}", "info");
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Error applying TCP rule: {ex.Message}", "error");
                    }
                }

                if (chkUDP.IsChecked == true)
                {
                    try
                    {
                        CreateFirewallRule($"{currentName} - UDP", currentports, NET_FW_IP_PROTOCOL_.NET_FW_IP_PROTOCOL_UDP);
                        WriteLog($"UDP rule '{currentName} - UDP' applied successfully for ports: {currentports}", "info");
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Error applying UDP rule: {ex.Message}", "error");
                    }
                }

                // Save the current server configuration to the list if it's not already there
                bool exists = false;
                foreach (var s in _serverList) { if (s.Name == currentName) exists = true; }

                if (!exists && !string.IsNullOrWhiteSpace(currentName))
                {
                    _serverList.Add(new GameServer
                    {
                        Name = currentName,
                        Ports = currentports,
                        UseTCP = chkTCP.IsChecked ?? false,
                        UseUDP = chkUDP.IsChecked ?? false
                    });
                    SaveServers();
                    WriteLog($"Added '{currentName}' to saved servers.", "Info");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying firewall rules: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Remove Button Click Handler - Removes rules based on the name entered in the txtName TextBox (Handles both TCP and UDP variants)
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

                // Initialize the firewall manager
                Type fwPolicy2Type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                INetFwPolicy2 fwPolicy2 = (INetFwPolicy2)Activator.CreateInstance(fwPolicy2Type);
                
                // Remove rules with the specified name (Handles TCP and UDP variants)
                var rulesToRemove = new System.Collections.Generic.List<string>();

                foreach (INetFwRule rule in fwPolicy2.Rules) // First, identify rules to remove
                {
                    try
                    {
                        if (rule.Name != null && rule.Name.StartsWith(ruleName))
                        {
                            rulesToRemove.Add(rule.Name);
                            WriteLog($"Marked rule '{rule.Name}' for removal.", "info");
                        }
                    }
                    catch (Exception ex) // Catch any errors while checking rules
                    {
                        WriteLog($"Error checking rule '{rule.Name}': {ex.Message}", "error");
                    }
                }

                foreach (var name in rulesToRemove) // Remove marked rules
                {
                    try
                    {
                        fwPolicy2.Rules.Remove(name);
                        WriteLog($"Rule '{name}' removed successfully.", "info");
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Error removing rule '{name}': {ex.Message}", "error");
                    }
                }
                
                var serverInList = System.Linq.Enumerable.FirstOrDefault(_serverList, s => s.Name == ruleName);

                if (serverInList != null)
                {
                    _serverList.Remove(serverInList);
                    SaveServers(); // Update the JSON file
                    WriteLog($"Removed '{ruleName}' from the saved server list.", "info");
                }
                // --- END DROP-DOWN REMOVAL LOGIC ---

                MessageBox.Show("Firewall rules and saved configuration removed!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during removal: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // Clear the UI so no "ghost" data remains
            ServerCombo.SelectedIndex = -1;
            ServerCombo.Text = "";
            txtPorts.Text = "";
            chkTCP.IsChecked = false;
            chkUDP.IsChecked = false;
        }

        //      _   _ _____ _     ____  _____ ____  ____  
        //     | | | | ____| |   |  _ \| ____|  _ \/ ___| 
        //     | |_| |  _| | |   | |_) |  _| | |_) \___ \ 
        //     |  _  | |___| |___|  __/| |___|  _ < ___) |
        //     |_| |_|_____|_____|_|   |_____|_| \_\____/ 
        //                                                                                       

        // Build and add the rule objects
        private void CreateFirewallRule(string name, string ports, NET_FW_IP_PROTOCOL_ protocol)
        {
            Type fwPolicy2Type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            INetFwPolicy2 fwPolicy2 = (INetFwPolicy2)Activator.CreateInstance(fwPolicy2Type);

            Type fwRuleType = Type.GetTypeFromProgID("HNetCfg.FWRule");
            INetFwRule rule = (INetFwRule)Activator.CreateInstance(fwRuleType);

            rule.Name = name;                                               // Set the rule name
            rule.Protocol = (int)protocol;                                  // Set the protocol (TCP or UDP)
            rule.LocalPorts = ports;                                        // Set the local ports (can be a single port or a comma-separated list)

            //Set the default params for the rules and rule folder organization
            rule.Direction = NET_FW_RULE_DIRECTION_.NET_FW_RULE_DIR_IN;     // Set the direction to inbound
            rule.Action = NET_FW_ACTION_.NET_FW_ACTION_ALLOW;               // Set the action to allow
            rule.Enabled = true;                                            // Enable the rule
            rule.Profiles = 7;                                              // 1 = Domain, 2 = Private, 4 = Public (7 means all profiles)
            rule.Grouping = "Ronin Portier Rules";                          // Grouping for better organization in the firewall rules list
            rule.Description = $"Created by Ronin Portier for ({name} - {protocol}) on ports: {ports}"; // Description for the rule
            
            fwPolicy2.Rules.Add(rule);                                      // Add the rule to the firewall policy
        }

        // Save the server list to a JSON file
        private void SaveServers()
        {
            try
            {
                // Define the file path (saves in the same folder as your .exe)
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "servers.json");

                // Convert the list to a pretty-printed JSON string
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(_serverList, options);

                // Write it to the disk
                File.WriteAllText(filePath, jsonString);
                WriteLog("Server list saved to disk.", "Info");
            }
            catch (Exception ex)
            {
                WriteLog($"Failed to save servers: {ex.Message}", "Error");
            }
        }

        private void ServerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Check if an actual GameServer object was selected
            if (ServerCombo.SelectedItem is GameServer selected)
            {
                // 1. Fill the other fields normally
                txtPorts.Text = selected.Ports;
                chkTCP.IsChecked = selected.UseTCP;
                chkUDP.IsChecked = selected.UseUDP;

                // 2. This is the key: Force the ComboBox text to be JUST the name.
                // We use Dispatcher because WPF sometimes tries to overwrite the text 
                // back to the ToString() version immediately after selection.
                Dispatcher.BeginInvoke(new Action(() => {
                    ServerCombo.Text = selected.Name;
                }));

                WriteLog($"Loaded profile: {selected.Name}", "info");
            }
        }

        // Write log messages to the RichTextBox with timestamps and color coding based on the log level
        private void WriteLog(string message, string level)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            
            Paragraph paragraph = new Paragraph(new Run($"[{timestamp}] [{level.ToUpper()}] {message}"));
            
            paragraph.Foreground = level.ToLower()
            switch {
                "info" => System.Windows.Media.Brushes.Green,
                "warning" => System.Windows.Media.Brushes.Orange,
                "error" => System.Windows.Media.Brushes.Red,
                _ => System.Windows.Media.Brushes.Black,
            };

            ConsoleRTB.Document.Blocks.Add(paragraph);
            ConsoleRTB.ScrollToEnd();
        }
    }

    // A simple class to represent a game server configuration, which can be saved and loaded from disk
    public class GameServer
    {
        public string Name { get; set; }
        public string Ports { get; set; }
        public bool UseTCP { get; set; }
        public bool UseUDP { get; set; }

        // This creates the "Iconic" look in your dropdown
        public override string ToString()
        {
            // Using emoji/symbols for quick visual ID
            string tcpIcon = UseTCP ? "🔹TCP" : "";
            string udpIcon = UseUDP ? "🔸UDP" : "";

            // Combine them with a separator if both are present
            string spacer = (UseTCP && UseUDP) ? " | " : "";

            return $"{Name} — {Ports}  [{tcpIcon}{spacer}{udpIcon}]";
        }
    }
}