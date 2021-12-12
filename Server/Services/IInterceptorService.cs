using nullrout3site.Shared;

namespace nullrout3site.Server.Services
{
    public interface IInterceptorService
    {
        public List<Interceptor> GetInterceptorRequests(string _uid);

        public Interceptor GetInterceptorRequestById(string uid, int requestId);

        public bool DeleteRequest(string uid, int requestId, string token);

        public bool DeleteCollector(string uid, string token);

        public Dictionary<string, string> NewUid();

        public bool UidExists(string _uid);

        public List<string> UidsExist(List<string> inputUids);

        public Task<Interceptor> ProcessRequest(string _uid, HttpRequest _request);

    }
}
