namespace nullrout3site.Shared
{
    /// <summary>
    /// Contains information related to a request that has been processed by a collector.
    /// </summary>
    public class Interceptor
    {
        public DateTime TimeStamp { get; set; }

        public int RequestId { get; set; }

        public string? Method { get; set; }

        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();

        public string? Body { get; set; }

        public Dictionary<string, string> FormData { get; set; } = new Dictionary<string, string>();

        public Dictionary<string, string> QueryParams { get; set; } = new Dictionary<string, string>();

    }
}
