using Blazored.LocalStorage;
using Blazored.SessionStorage;

namespace nullrout3site.Client.Services
{
    /// <summary>
    /// Service that handles interaction with the browser's local and session storage.
    /// Manages adding and removing uids and tokens in the local storage.
    /// </summary>
    public sealed class BrowserStorageService
    {
        private readonly ISessionStorageService _sessionStorage;
        private readonly ILocalStorageService _localStorage;


        public BrowserStorageService(ISessionStorageService sessionStorage, ILocalStorageService localStorage)
        {
            _sessionStorage = sessionStorage;
            _localStorage = localStorage;
        }


        public async Task<string> GetTokenFromUidAsync(string uid)
        {
            if (await _localStorage.ContainKeyAsync("ColTokens"))
            {
                var _tokens = await _localStorage.GetItemAsync<Dictionary<string, string>>("ColTokens");

                if (_tokens.ContainsKey(uid))
                    return _tokens[uid];
            }
            return string.Empty;
        }

        public async Task<Dictionary<string, string>> GetCollectorTokens()
        {
            if (await ContainsCollectorTokens())
                return await _localStorage.GetItemAsync<Dictionary<string, string>>("ColTokens");
            else
                throw new NullReferenceException();
        }

        public async Task<bool> ContainsCollectorTokens()
        {
            return await _localStorage.ContainKeyAsync("ColTokens");
        }

        public async Task SetCollectorTokensAsync(Dictionary<string, string> collectorTokens)
        {
            await _localStorage.SetItemAsync<Dictionary<string, string>>("ColTokens", collectorTokens);
        }

        public async Task SetSessionUid(string uid)
        {
            await _sessionStorage.SetItemAsStringAsync("intercept-uid", uid);
        }

        public async Task<string> GetSessionUid()
        {
            return await _sessionStorage.GetItemAsStringAsync("intercept-uid");
        }

        public async Task RemoveSessionUid()
        {
            await _sessionStorage.RemoveItemAsync("intercept-uid");
        }

    }
}
