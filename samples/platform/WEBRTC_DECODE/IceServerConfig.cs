namespace WEBRTC_DECODE
{
    /// <summary>
    /// Configuration data for ICE / NAT Traversal (STUN &amp; TURN servers)
    /// </summary>
    public class IceServerConfig
    {
        public bool Enabled { get; set; }
        public string StunUrl { get; set; } = "stun:stun.l.google.com:19302";
        public string TurnUrl { get; set; } = "turn:your-server.com:3478";
        public string TurnUsername { get; set; } = string.Empty;
        public string TurnPassword { get; set; } = string.Empty;
    }
}
