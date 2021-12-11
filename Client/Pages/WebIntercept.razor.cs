using nullrout3site.Shared;
using System.Net.Http.Json;

namespace nullrout3site.Client.Pages
{
    public partial class WebIntercept
    {
        private string? _uid;
        private string? _token;
        private Dictionary<string, string> _tokenCache = new();

        /// <summary>
        /// API call to create a new uid. Once a uid has been returned, it will be stored in session storage and the client will be redirected to the collector information page.
        /// </summary>
        /// <returns></returns>
        protected async Task GetUid()
        {
            var _result = await Http.GetFromJsonAsync<Dictionary<string, string>>("/i/newuid");
            if (_result is not null)
            {
                _uid = _result["uid"];
                _token = _result["token"];

                await browserStorage.SetSessionUid(_uid);

                await AddToken(_uid, _token);

                NavManager.NavigateTo("/wi/" + _uid);
            }

        }

        /// <summary>
        /// Takes in a list of uids and will send them to the API to validate them. The API should return the difference of valid uids compared to the uids provided in the API call.
        /// </summary>
        /// <param name="inUids">uids that will be checked for validity by the API call.</param>
        /// <returns></returns>
        protected async Task<List<string>> ValidateUids(List<string> inUids)
        {
            var _response = await Http.PostAsJsonAsync("/i/checkuids", inUids);
            if (_response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var _results = await _response.Content.ReadFromJsonAsync<List<string>>() ?? new List<string>();

                return _results;
            }
            else
            {
                return new List<string>();
            }

        }

        /// <summary>
        /// Takes a uid and token value, then will append this to the tokenCache, as well as add it to the browser local storage under the key "ColTokens".
        /// </summary>
        /// <param name="uid">uid to add as a key to the token.</param>
        /// <param name="token">token value to pair with the uid key, and be added to the list of tokens.</param>
        /// <returns></returns>
        protected async Task AddToken(string uid, string token)
        {
            await RefreshTokenCache();

            lock (_tokenCache)
                _tokenCache.Add(uid, token);
            await browserStorage.SetCollectorTokensAsync(_tokenCache);
        }

        /// <summary>
        /// Keeps the tokens associated with the uids provided as an argument, and removes the remaining tokens.
        /// </summary>
        /// <param name="uids">uids paired with the tokens that this function will not remove.</param>
        /// <returns></returns>
        protected async Task ClearAllTokensExcept(List<string> uids)
        {
            Dictionary<string, string> _validTokens = new Dictionary<string, string>();

            lock (_tokenCache)
            {
                foreach (var token in _tokenCache)
                {
                    if (uids.Contains(token.Key))
                        _validTokens.Add(token.Key, token.Value);
                }
                _tokenCache = _validTokens;
            }

            await browserStorage.SetCollectorTokensAsync(_validTokens);
        }

        /// <summary>
        /// Updates the clients token cache with the most resent token values stored in the browser local storage.
        /// </summary>
        /// <returns></returns>
        protected async Task RefreshTokenCache()
        {
            if (await browserStorage.ContainsCollectorTokens())
            {
                var _tokens = await browserStorage.GetCollectorTokens();
                lock (_tokenCache)
                    _tokenCache = _tokens;
            }
        }

        /// <summary>
        /// If the user has a uid already in session storage, then this will redirect them to the collector information page.
        /// </summary>
        /// <returns></returns>
        protected override async Task OnInitializedAsync()
        {
            _uid = await browserStorage.GetSessionUid();

            await RefreshTokenCache();

            if (_tokenCache.Any())
            {
                List<string> _tokenUids = new List<string>();
                lock (_tokenCache)
                    _tokenUids = _tokenCache.Keys.ToList();
                var _validUids = await ValidateUids(_tokenUids);

                await ClearAllTokensExcept(_validUids);
            }

            if (_uid != null)
            {
                NavManager.NavigateTo("/wi/" + _uid);
            }
        }
    }
}
