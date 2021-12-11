using nullrout3site.Shared;
using System.Security.Cryptography;
using System.Text;

namespace nullrout3site.Server.Services
{
    public class InterceptorService : IInterceptorService
    {
        private readonly Random _random = new();
        private Timer _cleanupTimer;
        private const int _cleanupInterval = 2; // How often the cleanup check is ran (in hours)
        private const double _collectorRetention = 24.0d; // How long old collectors should be retained after their latest request timestamp. (in hours)

        /// <summary>
        /// Global collection of all uids and their associated Interceptor objects (which contain the request data).
        /// This object should be locked on read/write actions (Including enumeration) for thread safety.
        /// </summary>
        public Dictionary<string, List<Interceptor>> InterceptContainer = new();

        private Dictionary<string, string> UidTokens = new Dictionary<string, string>();

        public InterceptorService()
        {
            _cleanupTimer = new Timer(CleanupElapsed, null, 0, 1000 * 60 * 60 * _cleanupInterval);
        }


        /// <summary>
        /// Exposed function that will return a list of Interceptor objects from the InterceptContainer based on the key provided.
        /// </summary>
        /// <param name="_uid">Unique ID that will be used as a key for InterceptContainer.</param>
        /// <returns>List of Interceptor Objects.</returns>
        public List<Interceptor> GetInterceptorRequests(string _uid)
        {
            return InterceptContainer[_uid];
        }

        /// <summary>
        /// Exposed function that will return an Interceptor object from the InterceptContainer based on the key and requestId provided.
        /// </summary>
        /// <param name="uid">Unique ID used as a key.</param>
        /// <param name="requestId">Request ID that will be used for lookup.</param>
        /// <returns></returns>
        /// <exception cref="KeyNotFoundException"></exception>
        public Interceptor GetInterceptorRequestById(string uid, int requestId)
        {
            Dictionary<string, List<Interceptor>> _cachedContainer;

            lock (InterceptContainer)
                _cachedContainer = new(InterceptContainer);

            foreach (Interceptor interceptor in _cachedContainer[uid])
            {
                if (interceptor.RequestId == requestId)
                {
                    return interceptor;
                }
            }
            throw new KeyNotFoundException();
        }

        /// <summary>
        /// Exposed function that will return the last Interceptor object in the list that is stored under the uid key.
        /// </summary>
        /// <param name="uid">Key used to find the appropriate list of Interceptor objects.</param>
        /// <returns>Last Interceptor object in the list.</returns>
        public Interceptor GetInterceptorLastRequest(string uid)
        {
            Dictionary<string, List<Interceptor>> _cachedContainer;

            lock (InterceptContainer)
                _cachedContainer = new(InterceptContainer);

            return _cachedContainer[uid].Last();
        }

        /// <summary>
        /// Deletes an Interceptor object based on it's requestId
        /// </summary>
        /// <param name="uid">Key used to find the appropriate list of Interceptor objects.</param>
        /// <param name="requestId">The ID tied to the specific Interceptor object that will be removed.</param>
        /// <returns>true or false depending on the success of the deletion.</returns>
        public bool DeleteRequest(string uid, int requestId)
        {
            lock (InterceptContainer)
            {
                foreach (Interceptor interceptor in InterceptContainer[uid])
                {
                    if (interceptor.RequestId == requestId)
                    {
                        InterceptContainer[uid].Remove(interceptor);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Deletes the list of Interceptors based on the key provided. Takes a token to validate the request. The token was provided to the client when the collector was first created.
        /// </summary>
        /// <param name="uid">Key used to find the appropriate list of Interceptor objects.</param>
        /// <param name="token">Token that validates the request. The token was provided to the client that initially requested the collector be created.</param>
        /// <returns>true or false depending on the sucess of the deletion.</returns>
        public bool DeleteCollector(string uid, string token)
        {
            lock (InterceptContainer)
            {
                if (UidTokens[uid].Equals(token))
                {
                    if (InterceptContainer.Remove(uid))
                    {
                        lock (UidTokens)
                            UidTokens.Remove(uid);
                        return true;
                    }
                }
            }

            return false;
        }


        /// <summary>
        /// Takes a uid and HttpRequest, packages them into an Interceptor object, and adds that object to the InterceptContainer. (The global collection of all Interceptor request data)
        /// The timestamp associated with the Interceptor object is applied to the object when this function is called.
        /// </summary>
        /// <param name="_uid">Unique ID that will be used as a key for the InterceptContainer.</param>
        /// <param name="_request">HttpRequest that will be packaged into an Interceptor object and added to the InterceptContainer.</param>
        /// <returns></returns>
        public async Task<Interceptor> ProcessRequest(string _uid, HttpRequest _request)
        {
            Interceptor _interceptor = new();

            // Buffering is enabled and the position set to 0 so that we can read the body contents.
            _request.EnableBuffering();
            _request.Body.Position = 0;

            _interceptor.TimeStamp = DateTime.Now;
            _interceptor.Method = _request.Method;
            _interceptor.Headers = _request.Headers.ToDictionary(x => x.Key, x => string.Join(";", x.Value)); // Headers copied to a dictionary, StringValues seperated by a semicolon. (Can not otherwise implicitly cast IHeaderDictionary to a Dictionary)
            if (_interceptor.Headers.ContainsKey(":method"))
                _interceptor.Headers.Remove(":method");
            _interceptor.Body = await new StreamReader(_request.Body).ReadToEndAsync();
            try
            {
                _interceptor.FormData = _request.Form.ToDictionary(k => k.Key, v => string.Join(";", v.Value));
            } catch { }
            try
            {
                _interceptor.QueryParams = _request.Query.ToDictionary(k => k.Key, v => string.Join(";", v.Value));
            } catch { }

            lock (InterceptContainer)
            {
                if (InterceptContainer[_uid].Any())
                    _interceptor.RequestId = InterceptContainer[_uid].Last().RequestId + 1;
                else
                    _interceptor.RequestId = 1;

                InterceptContainer[_uid].Add(_interceptor);
            }

            return _interceptor;
        }

        /// <summary>
        /// Creates a new uid value and ensures the value is unique and not already stored in memory.
        /// Continuously generates a new uid until a unique one has been generated, and will add this value as a key to the InterceptContainer dictionary.
        /// </summary>
        /// <returns>String containing the final Unique ID. String is in the form of last 8 characters of an MD5 hash. E.g. (6842F7ED)</returns>
        public Dictionary<string, string> NewUid()
        {
            string _uid = GenerateUid();
            //Create unique token that will allow the user to delete the collector uid
            string _token = Convert.ToBase64String(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()));

            while (UidExists(_uid))
            {
                _uid = GenerateUid();
            }

            lock (InterceptContainer)
                InterceptContainer.Add(_uid, new List<Interceptor>());

            lock (UidTokens)
                UidTokens.Add(_uid, _token);

            var _result = new Dictionary<string, string>() { 
                { "uid" , _uid},
                { "token", _token }
            };
            return (_result);
        }

        /// <summary>
        /// Checks if uid exist in memory within InterceptContainer
        /// </summary>
        /// <param name="_uid"></param>
        /// <returns>true if the uid exist, otherwise returns false</returns>
        public bool UidExists(string _uid)
        {
            lock (InterceptContainer)
                if (InterceptContainer.ContainsKey(_uid)) return true; else return false;
        }

        /// <summary>
        /// Takes an array of uids and drops the ones that do not exist, returning an array of only existing uids.
        /// Length validation should be done if this method is invoked by a client in any way. (via an API-call, for example)
        /// Otherwise, a client could supply a huge array to find all valid uids; this would be a negative impact to performance and security.
        /// </summary>
        /// <returns>Array of uids that exist compared to the array of uids supplied as an argument.</returns>
        public List<string> UidsExist(List<string> inputUids)
        {
            List<string> _validUids = new List<string>();
            foreach (var inUid in inputUids)
            {
                if (UidExists(inUid))
                    _validUids.Add(inUid);
            }

            return _validUids;
        }

        /// <summary>
        /// Generates a new uid. Uses MD5 hash value based on a random int between Int32 min and max values.
        /// Strips all but the last 8 characters.
        /// </summary>
        /// <returns>String of last 8 characters of generated MD5 hash value.</returns>
        private string GenerateUid()
        {
            //Create random initilization vector
            string _iv = _random.Next(Int32.MinValue, Int32.MaxValue).ToString();
            string _uid = CreateMD5Hash(_iv);
            _uid = _uid.Substring(_uid.Length - 8);


            return _uid;
        }

        /// <summary>
        /// Create MD5 hash from input string
        /// </summary>
        /// <param name="input"></param>
        /// <returns>String result of MD5 hash algorithm</returns>
        private string CreateMD5Hash(string input)
        {
            MD5 md5 = MD5.Create();
            byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("X2"));
            }

            return sb.ToString();
        }



        /// <summary>
        /// Automated InterceptContainer cleanup proccess, cleans up old entries from memory. Invoked by an elapsed timer. Removes the UID and associated Interceptor objects if the timestamp of the last Interceptor object exceedes 24h.
        /// </summary>
        /// <param name="stateInfo">Object passed from the TimerCallback delegate. null in this case.</param>
        private void CleanupElapsed(Object? stateInfo)
        //TODO: Find a way to cleanup empty InterceptContainers without just removing them every _cleanupInterval.
        //      If someone creates a collector URL but never actually uses it, then there will be no TimeStamp to check
        {
            Dictionary<string, List<Interceptor>>? _cachedContainer;

            // Copy the dictionary so we can do potentially lengthy enumeration without having to lock the dictionary for the entire duration.
            // Shallow-copying should be relatively fast as we are creating a new object from memory and not doing enumeration.
            lock (InterceptContainer)
                _cachedContainer = new(InterceptContainer); 

            if (_cachedContainer.Any())
            {
                foreach (var container in _cachedContainer)
                {
                    int lastIndex = container.Value.Count() - 1;

                    if (lastIndex > -1)
                    {
                        TimeSpan elaspsedTime = DateTime.Now - container.Value[lastIndex].TimeStamp;

                        if (elaspsedTime.TotalHours >= _collectorRetention)
                        {
                            lock(InterceptContainer) // Lock the main dictionary only for the removal process.
                                InterceptContainer.Remove(container.Key);
                        }

                    }
                }
            }
        }

    }
}
