using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using MudBlazor;
using nullrout3site.Client.Shared;
using nullrout3site.Shared;
using System.Net.Http.Json;

namespace nullrout3site.Client.Pages
{
    public partial class WebInterceptId
    {
        // uid of the collector, grabbed from the URI of the page
        [Parameter]
        public string? Uid { get; set; }

        /// <summary>
        /// Stores the url to the collector API. Pretty much just used as a variable to pass to the clipboard when the user clicks the 'copy collector url' button.
        /// </summary>
        private string _collectorUrl = "";

        /// <summary>
        /// For the tooltip that appears when there are no requests.
        /// </summary>
        private bool _popIsOpen =>
            !requestsData.Any();

        /// <summary>
        /// Connection to the SignalR hub. Uses websockets to subscribe to RPCs.
        /// </summary>
        private HubConnection? hubConnection;


        /// <summary>
        /// Local cache of Interceptors related to the uid, contains all request data. This object should be locked on read/write actions (Including enumeration) for thread safety.
        /// Static so that the list persist when the user navigates away and back-to the page.
        /// </summary>
        static List<Interceptor> requestsData = new();

        /// <summary>
        /// Tracks what card is active, or which request information should be shown.
        /// </summary>
        public int ActiveCard = 0;

        /// <summary>
        /// Changes which card is active and updates the render of the page.
        /// </summary>
        /// <param name="index"></param>
        protected void SetActiveCard(int index)
        {
            ActiveCard = index;
            StateHasChanged();
        }

        /// <summary>
        /// Asynchronous task to send GET request to the Interceptor API "/i/{uid}/out".
        /// The API will return a list of all intercepted requests for a specific uid as a List of Interceptor objects.
        /// The List overwrites requestData for local caching.
        /// </summary>
        /// <returns></returns>
        protected async Task GetRequestsData()
        {
            try
            {

                var _requestsData = await Http.GetFromJsonAsync<List<Interceptor>>("/i/" + Uid + "/out");
                if (_requestsData is not null)
                {
                    lock (requestsData)
                        requestsData = _requestsData;
                }
            }
            catch (HttpRequestException e)
            {
                if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    await browserStorage.RemoveSessionUid(); // Collector no longer exist, remove the uid from session storage
                }
            }
            StateHasChanged();
        }

        /// <summary>
        /// Asynchronous task to send GET request to the Interceptor API "/i/{uid}/out/last".
        /// The API will return the last Interceptor object in the list of Interceptors that is mapped to the uid.
        /// </summary>
        /// <returns></returns>
        protected async Task GetLastRequestData()
        {
            try
            {
                Interceptor? _lastInter = await Http.GetFromJsonAsync<Interceptor>("/i/" + Uid + "/out/last");
                if (_lastInter is not null)
                {
                    lock (requestsData)
                        requestsData?.Add(_lastInter);
                }

            }
            catch (HttpRequestException e)
            {
                if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    await browserStorage.RemoveSessionUid(); // Collector no longer exist (probably), remove the uid from session storage
                }
            }
            StateHasChanged();
        }

        protected async Task GetRequestById(int requestId)
        {
            try
            {
                Interceptor? _request = await Http.GetFromJsonAsync<Interceptor>("/i/" + Uid + "/out/" + requestId);
                if (_request is not null)
                {
                    lock(requestsData)
                        requestsData?.Add(_request);
                }
            }
            catch (HttpRequestException e)
            {
                if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // TODO: Display some error to user
                }
            }
            StateHasChanged();
        }

        /// <summary>
        /// API POST request is made to /i/{uid}/del containing the requestId of the request that will be deleted by the InterceptorService
        /// </summary>
        /// <param name="requestId"></param>
        /// <returns></returns>
        protected async Task DeleteRequest(int? requestId)
        {
            await Http.PostAsJsonAsync("/i/" + Uid + "/del", requestId);
        }


        /// <summary>
        /// Called when the client first initializes this page. (Everytime they navigate to it)
        /// Subscribes to RPCs from the hub via websockets to get notifed when a request is created/deleted etc.
        /// </summary>
        /// <returns></returns>
        protected override async Task OnInitializedAsync()
        {
            _collectorUrl = NavManager.BaseUri + "i/" + Uid;
            if (!requestsData.Any())
            {
                await GetRequestsData();
            }

            hubConnection = new HubConnectionBuilder().WithUrl(NavManager.BaseUri + "notifyhub").Build();

            hubConnection.On<int>("InterceptorNotify", async (requestId) =>
            {
                await GetRequestById(requestId);
            });

            hubConnection.On<int>("InterceptorNotifyDel", (requestId) =>
            {
                DeleteRequestLocal(requestId);
            });
            hubConnection.On("InterceptorNotifyCollectorDel", async () =>
            {
                await DeleteCollectorLocal();
            });

            await hubConnection.StartAsync();

            await NotifyInitAsync();
        }

        /// <summary>
        /// Checks if we are connected to the hub.
        /// </summary>
        public bool IsConnected =>
            hubConnection?.State == HubConnectionState.Connected;

        /// <summary>
        /// Runs when client navigates away from the page, disposes of our websocket connection. Until next time, I suppose.
        /// </summary>
        /// <returns></returns>
        public async ValueTask DisposeAsync()
        {
            if (hubConnection is not null)
            {
                await hubConnection.DisposeAsync();
            }
        }

        /// <summary>
        /// RPC to the hub on initial connection, supplies our connection id and the collector uid.
        /// </summary>
        /// <returns></returns>
        private async Task NotifyInitAsync()
        {
            if (IsConnected && hubConnection is not null)
            {
                await hubConnection.SendAsync("InterceptorInit", Uid);
            }

        }

        /// <summary>
        /// Helper to copy text to the clipboard.
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        private async Task CopyToClipboard(string text)
        {
            try
            {
                await clipboardService.WriteTextAsync(text);

            }
            catch
            {
                // TODO: Something here to display error to the user
            }
        }

        /// <summary>
        /// Called on the local client whenever it receives an RPC indicating a request has been removed by someone else.
        /// </summary>
        /// <param name="requestId"></param>
        private void DeleteRequestLocal(int? requestId)
        {
            lock (requestsData)
            {
                foreach (Interceptor request in requestsData)
                {
                    if (request.RequestId == requestId)
                    {
                        requestsData.Remove(request);

                        if (ActiveCard > 0)
                            ActiveCard -= 1;
                        break;
                    }
                }
            }
            StateHasChanged();
        }

        /// <summary>
        /// Called when the user clicks the 'delete collector' button. Creates a dialog popup asking the user to confirm their actions, and sends a request to the API /i/delcol/{uid}
        /// </summary>
        /// <returns></returns>
        private async Task DeleteCollector()
        {
            // Bunch of boilerplate to populate the parameters for the dialog box.
            var parameters = new DialogParameters();
            parameters.Add("ContentText", "This will permanently delete the collector URL and all associated request data. This process cannot be undone.");
            parameters.Add("ButtonText", "Delete");
            parameters.Add("Color", Color.Error);
            var options = new DialogOptions() { CloseButton = true, MaxWidth = MaxWidth.ExtraSmall };

            var dialog = DialogService.Show<DialogTemplate>("Delete collector?", parameters, options);

            var result = await dialog.Result; // Get the result of the dialog box (Whether they confirmed or canceled the action)

            if (!result.Cancelled) // If the dialog box was not canceled. (By the 'X' or the 'Cancel' buttons.)
            {
                var _token = Uid is not null ? await browserStorage.GetTokenFromUidAsync(Uid) : string.Empty; // Have to do this stupid thing so the compiler will accept that Uid will never be null. Ternary op that sets the token to an empty string if Uid is null. (which it never will be)

                var _response = await Http.PostAsJsonAsync<string>("/i/delcol/" + Uid, _token); // Make the API call. Sends HTTP POST request.
                if (_response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    await browserStorage.RemoveSessionUid(); // Remove uid from session storage so they will be greeted by the 'create collector' page instead of being redirected back here.
                    lock (requestsData)
                        requestsData.Clear();
                    NavManager.NavigateTo("/webintercept");
                }
                else
                {
                    parameters = new DialogParameters(); // Dialog popup if the API returns anything but a 200 OK
                    parameters.Add("ContentText", "Error: " + await _response.Content.ReadAsStringAsync());
                    parameters.Add("ButtonText", "Ok");
                    parameters.Add("Color", Color.Primary);
                    DialogService.Show<DialogTemplate>("Error :(", parameters, options);
                }
            }

        }

        /// <summary>
        /// RPC from the server and called on the local client when a different client deletes the collector.
        /// </summary>
        /// <returns></returns>
        private async Task DeleteCollectorLocal()
        {
            // TODO: Add a message telling the user that the collector they were using has been removed by someone.
            //       Maybe even add a timed option to let the user keep the collector
            lock (requestsData)
            {
                requestsData.Clear();
            }
            await browserStorage.RemoveSessionUid();
            StateHasChanged();
        }
    }
}
