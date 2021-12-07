﻿using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace nullrout3site.Server.Hubs
{
    public class NotifyHub : Hub
    {
        public static ConcurrentDictionary<string, NotifyClient> NotifyClients = new();

        /// <summary>
        /// Adds a new NotifyClient object to the Notifyclients dict which is used to track all connected clients. The NotifyClient object contains information related to the client connection.
        /// </summary>
        /// <returns></returns>
        public override Task OnConnectedAsync()
        {
            NotifyClients.TryAdd(Context.ConnectionId, new NotifyClient() { ConnectionId = Context.ConnectionId }); ;
            Console.WriteLine("con : " + Context.ConnectionId);
            return base.OnConnectedAsync();
        }

        /// <summary>
        /// Removes the Notifyclient object related to this connection from the NotifyClients dict.
        /// </summary>
        /// <param name="exception"></param>
        /// <returns></returns>
        public override Task OnDisconnectedAsync(Exception? exception)
        {
            NotifyClients.TryRemove(Context.ConnectionId, out _);
            Console.WriteLine("decon : " + Context.ConnectionId);
            return base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Generic RPC availible to send a message to a specific client.
        /// </summary>
        /// <param name="conId">Connection ID of the client to receive the message.</param>
        /// <param name="notifyType">String which contains the method to be called on the client.</param>
        /// <param name="message">String that is passed as an argument to the method called on the client.</param>
        /// <returns></returns>
        public async Task NotifyClient(string conId, string notifyType, string message)
        {
            await Clients.Client(conId).SendAsync(notifyType, message);
        }

        /// <summary>
        /// RPC from the client that is called when a client visits the collector page. This will add them to a group that is specific to a collector URL, so that they can receive notifications about that specific collector. 
        /// E.g. when a new request is added to that collector.
        /// Updates the NotifyClient object that corresponds with the connectionId.
        /// </summary>
        /// <param name="uid">uid to be added to the NotifyClient object that corresponds with the callers connectionId.</param>
        /// <returns></returns>
        public async Task InterceptorInit(string uid)
        {
            NotifyClient? _curClient;
            NotifyClient? _newClient;

            if (NotifyClients.TryGetValue(Context.ConnectionId, out _newClient))
                _newClient.InterceptorUID = uid;

            if (NotifyClients.TryGetValue(Context.ConnectionId, out _curClient))
                NotifyClients.TryUpdate(Context.ConnectionId, _curClient, _curClient);

            await Groups.AddToGroupAsync(Context.ConnectionId, "intercept-" + uid);
        }
    }


    /// <summary>
    /// NotifyClient object contains information about a connection. Object is created when a new connection is made, and can be updated by the hub.
    /// </summary>
    public class NotifyClient
    {
        public string? ConnectionId { get; set; }
        public string? InterceptorUID { get; set; }
    }
}
