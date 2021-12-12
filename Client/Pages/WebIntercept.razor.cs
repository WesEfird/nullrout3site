using MudBlazor;
using nullrout3site.Client.Shared;
using nullrout3site.Shared;
using System.Net.Http.Json;

namespace nullrout3site.Client.Pages
{
    /// <summary>
    /// All of the client code that can potentially run on the /webintercept page.
    /// Mostly handles user actions, and a few automated things such as cleaning out obsolete entries of uids (including their tokens) from the browser storage.
    /// </summary>
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

                await AddToken(_uid, _token);

                NavManager.NavigateTo("/wi/" + _uid);
            }

        }

        /// <summary>
        /// For onclick events to navigate to the collector.
        /// </summary>
        /// <param name="uid">uid of the collector where the client will be navigated.</param>
        protected void NavigateToUid(string uid)
        {
            NavManager.NavigateTo("/wi/" + uid);
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

        protected async Task RemoveToken(string uid)
        {
            await RefreshTokenCache();

            lock (_tokenCache)
                _tokenCache.Remove(uid);
            await browserStorage.SetCollectorTokensAsync(_tokenCache);

            StateHasChanged();
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
        /// Validates the collector uids in the token cache to make sure they still exist. Any collectors that are no longer active will be removed. Keeps the tokens in browser storage up to date, and cleans out any obsolete collector uids.
        /// </summary>
        /// <returns></returns>
        protected async Task ValidateTokenCache()
        {
            if (_tokenCache.Any()) // Checks if there are any entries in the token cache.
            {
                List<string> _tokenUids = new List<string>();
                List<string> _validUids = new();

                lock (_tokenCache)
                    _tokenUids = _tokenCache.Keys.ToList(); // Cache a list of all of the uids from the _tokenCache dictionary.

                // The API will only validate 10 uids at a time (to limit bruteforcing), so we will have to make multiple API requests if the client has more than 10 collectors in their 'watchlist'.
                if (_tokenUids.Count > 10)
                {
                    // Divide the amount of uids by 10 and check if the remainder is greater than 0, add an extra iteration if the remainder is greater than 0.
                    // i.e. : 12/10 = 1R2 (1.2), so it would take two requests, one for the first 10 uids, and another for the 2 remaining uids.
                    int iterationAmount = _tokenUids.Count % 10 != 0 ? (_tokenUids.Count / 10) + 1 : _tokenUids.Count / 10;

                    for (; iterationAmount > 0; iterationAmount--)
                    {
                        List<string> _uidChunk = new();
                        if (iterationAmount != 1) // If we are not on the last iteration
                        {
                            _uidChunk.AddRange(_tokenUids.GetRange(0, 10)); // get the first 10 items in the list and adds them to the chunk
                            _validUids.AddRange(await ValidateUids(_uidChunk)); // add the successfully validated uids to the _validUids list
                            _tokenUids.RemoveRange(0, 10); // remove first 10 items in the _tokenUids list
                        }
                        else // Last iteration
                        {
                            _uidChunk.AddRange(_tokenUids); // add all the remaining elements to the chunk
                            _validUids.AddRange(await ValidateUids(_uidChunk));
                            // No need to clear the _tokenUids list since it will be discarded as we go out of scope.
                        }
                    }

                    await ClearAllTokensExcept(_validUids);
                }
                else // This is a lot easier with less than 10 uids :P
                {
                    _validUids = await ValidateUids(_tokenUids);

                    await ClearAllTokensExcept(_validUids);
                }

            }
        }

        /// <summary>
        /// Shows a dialog box which reaveals the token for the selected collector. This token is pulled from browser storage, and not from the server. 
        /// The only way for the client to have received this token is on the initial creation of the collector. Or if it was shared to them by another user.
        /// </summary>
        /// <param name="uid">uid used as a key to search browser local storage for the token value.</param>
        /// <returns></returns>
        protected async Task ShowToken(string uid)
        {
            var parameters = new DialogParameters();
            var options = new DialogOptions() { CloseButton = true, MaxWidth = MaxWidth.Small };
            var token = await browserStorage.GetTokenFromUidAsync(uid);
            #region Dialog params
            parameters.Add("ContentText", "This token can be used to delete the collector, be careful when sharing it.");
            parameters.Add("ButtonText", "Ok");
            parameters.Add("TokenString", token);
            parameters.Add("Color", Color.Primary);
            #endregion

            var dialog = DialogService.Show<DialogTokenTemplate>("Token", parameters, options);
        }

        private async Task DeleteCollector(string uid, string token)
        {
            // Bunch of boilerplate to populate the parameters for the dialog box.
            #region Delete Collector Dialog
            var parameters = new DialogParameters();
            parameters.Add("ContentText", "This will permanently delete the collector URL and all associated request data. This process cannot be undone.");
            parameters.Add("ButtonText", "Delete");
            parameters.Add("Color", Color.Error);
            parameters.Add("CancelButton", true);
            var options = new DialogOptions() { CloseButton = true, MaxWidth = MaxWidth.ExtraSmall };

            var dialog = DialogService.Show<DialogTemplate>("Delete collector?", parameters, options);

            var result = await dialog.Result; // Get the result of the dialog box (Whether they confirmed or canceled the action)
            #endregion

            if (!result.Cancelled) // If the dialog box was not canceled. (By the 'X' or the 'Cancel' buttons.)
            {
                var _response = await Http.PostAsJsonAsync<string>("/i/delcol/" + uid, token);

                if (_response.StatusCode == System.Net.HttpStatusCode.OK) // If delete request was successful
                {
                    await RemoveToken(uid);
                }
                else
                {
                    #region error dialog
                    parameters = new DialogParameters(); // Dialog popup if the API returns anything but a 200 OK
                    parameters.Add("ContentText", "Error: " + await _response.Content.ReadAsStringAsync());
                    parameters.Add("ButtonText", "Ok");
                    parameters.Add("Color", Color.Primary);
                    DialogService.Show<DialogTemplate>("Error :(", parameters, options);
                    #endregion
                }
            }
        }

        /// <summary>
        /// If the user has a uid already in session storage, then this will redirect them to the collector information page.
        /// </summary>
        /// <returns></returns>
        protected override async Task OnInitializedAsync()
        {

            await RefreshTokenCache();

            await ValidateTokenCache();

        }
    }
}
