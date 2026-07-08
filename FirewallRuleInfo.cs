namespace Ronin_Portier
{
    // Row model for the "Firewall Rules" grid — one row per live Windows Firewall rule.
    public class FirewallRuleInfo
    {
        public string Name { get; set; } = "";
        public string Direction { get; set; } = "";
        public string Protocol { get; set; } = "";
        public string LocalPorts { get; set; } = "";
        public string Profile { get; set; } = "";
        public bool Enabled { get; set; }
        public bool IsPortierManaged { get; set; }
        public string ProcessName { get; set; } = "";

        public string OwnerIcon => IsPortierManaged ? "🏯" : "";
    }
}
