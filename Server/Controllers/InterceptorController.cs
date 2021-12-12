using Microsoft.AspNetCore.Mvc;
using nullrout3site.Shared;
using nullrout3site.Server.Services;
using Microsoft.AspNetCore.SignalR;
using nullrout3site.Server.Hubs;

namespace nullrout3site.Server.Controllers
{
    /// <summary>
    /// The API controller for collectors, most request will require the colelctor uid to be in the URL. 
    /// This is basically the API endpoint that clients will interact with when requesting certain actions from the InterceptorService.
    /// </summary>
    [Route("i")]
    [ApiController]
    public class InterceptorController : ControllerBase
    {
        // Dependency injection
        private readonly IInterceptorService _interceptorService;
        private readonly IHubContext<NotifyHub> _hubContext;

        // Maximum size of requests that are accepted by the collector. This is to help prevent DOS attacks from rapidly filling the applications memory.
        // Note: This method of DOS is still possible, an automated attack could find the maximum value and repeatedly send a payload of that size to the server until memory is full. Consider adding some type of rate limiting.
        private const long _maxRequestSize = 50_000; 

        public InterceptorController(IInterceptorService interceptorService, IHubContext<NotifyHub> hubContext)
        {
            _interceptorService = interceptorService;
            _hubContext = hubContext;
        }

        /// <summary>
        /// API endpoint that take in an HTTP/HTTPS request as input and processes that request via the IntereceptorService.
        /// </summary>
        /// <param name="uid">The unique ID of the collector where the request will be stored.</param>
        /// <returns>Http response indicating success/failure.</returns>
        #region Request input/processing
        [HttpGet("{uid}")]
        [RequestSizeLimit(_maxRequestSize)]
        public async Task<IActionResult> ProcessGetRequest(string uid)
        {
            return await ProcessRequest(uid);
        }

        [HttpPost("{uid}")]
        [RequestSizeLimit(_maxRequestSize)]
        public async Task<IActionResult> ProcessPostRequest(string uid)
        {
            return await ProcessRequest(uid);
        }

        [HttpPut("{uid}")]
        [RequestSizeLimit(_maxRequestSize)]
        public async Task<IActionResult> ProcessPutRequest(string uid)
        {
            return await ProcessRequest(uid);
        }
        [HttpPatch("{uid}")]
        [RequestSizeLimit(_maxRequestSize)]
        public async Task<IActionResult> ProcessPatchRequest(string uid)
        {
            return await ProcessRequest(uid);
        }

        [HttpDelete("{uid}")]
        [RequestSizeLimit(_maxRequestSize)]
        public async Task<IActionResult> ProcessDeleteRequest(string uid)
        {
            return await ProcessRequest(uid);
        }
        [HttpOptions("{uid}")]
        [RequestSizeLimit(_maxRequestSize)]
        public async Task<IActionResult> ProcessOptionsRequest(string uid)
        {
            return await ProcessRequest(uid);
        }
        #endregion


        /// <summary>
        /// API endpoint that will return a list of Interceptor objects that are stored using the uid as a key. Returns object in JSON format.
        /// </summary>
        /// <param name="uid"></param>
        /// <returns>List of Interceptor objects. Serializes in JSON format.</returns>
        [HttpGet("{uid}/out")]
        public ActionResult<List<Interceptor>> GetResults(string uid)
        {
            if (_interceptorService.UidExists(uid))
            {
                return Ok(_interceptorService.GetInterceptorRequests(uid));
            }
            else
            {
                return NotFound("Interceptor ID not found.");
            }
            
        }

        /// <summary>
        /// API endpoint that will return the specified Interceptor object, based on it's requestId; uses the uid as a key to lookup the correct list of Interceptors. Returns object in JSON format.
        /// </summary>
        /// <param name="uid"></param>
        /// <returns>Interceptor object. Serializes in JSON format.</returns>
        [HttpGet("{uid}/out/{requestId}")]
        public ActionResult<Interceptor> GetResultById(string uid, int requestId)
        {
            if (_interceptorService.UidExists(uid))
            {
                try
                {
                    return Ok(_interceptorService.GetInterceptorRequestById(uid, requestId));

                } catch (KeyNotFoundException)
                {
                    return NotFound("Interceptor RequestId not found");
                }
            } else
            {
                return NotFound("Interceptor ID not found.");
            }
        }

        /// <summary>
        /// API endpoint (POST) that will delete a specified request from the list of Interceptor objects that are stored under the specified uid key.
        /// </summary>
        /// <param name="uid">uid used as key to lookup the corresponding list of Interceptors.</param>
        /// <param name="requestId">requestId extracted from POST request, used to determine which request to delete.</param>
        /// <returns>HTTP response indicating success/failure.</returns>
        [HttpPost("{uid}/del")]
        public async Task<ActionResult> DeleteRequest(string uid, [FromBody] Dictionary<string,string> paramData)
        {
            var requestId = int.Parse(paramData["requestId"]);
            var token = paramData["token"];
            if (_interceptorService.UidExists(uid))
            {
                if (_interceptorService.DeleteRequest(uid, requestId, token))
                {
                    // Use websocket to alert clients of this request deletion. Only clients associated with this collector will be notified.
                    await _hubContext.Clients.Group("intercept-" + uid).SendAsync("InterceptorNotifyDel", requestId);
                    return Ok();
                }
                else
                {
                    return BadRequest("Either the collector or request does not exist, or an invalid token was supplied.");
                }
            }
            else
            {
                return NotFound("Interceptor ID not found.");
            }

        }

        /// <summary>
        /// API endpoint (POST) that will delete the specified collector URL and all of it's corresponding request data.
        /// </summary>
        /// <param name="uid">uid of the collector that will be requested to delete.</param>
        /// <param name="token">token that corresponds to the collector, must be valid for the deletion request to be approved.</param>
        /// <returns>HTTP response indicating whether the action was successful or not.</returns>
        [HttpPost("delcol/{uid}")]
        public async Task<ActionResult> DeleteCollector(string uid, [FromBody]string token)
        {
            if (_interceptorService.UidExists(uid))
            {
                if (_interceptorService.DeleteCollector(uid, token))
                {
                    // Use websocket to alert clients of this collectors deletion. Only clients associated with this collector will be notified.
                    await _hubContext.Clients.Group("intercept-" + uid).SendAsync("InterceptorNotifyCollectorDel");
                    return Ok();
                }
                else
                {
                    return BadRequest("Either the collector does not exist, or an invalid token was supplied.");
                }
            }
            else
            {
                return NotFound("Interceptor ID not found.");
            }
        }

        //TODO: Implement rate limiting to prevent automated creation of new collectors.
        //      The default settings allow 34^8 of collector uid possibilities, which would take 280 years to fill up at ~200 request per second..
        //      BUT it does add a memory footprint of ~32bytes for each one created (with no request data) or ~6.4kb/sec of memory at ~200 rps. (That's a whopping 23Mb every hour.)

        /// <summary>
        /// Create a new collector url and collector container instance in the InterceptorService.InterceptContainer global collection of collectors and requests.
        /// </summary>
        /// <returns>HTTP reponse indicating success.</returns>
        [HttpGet("newuid")]
        public IActionResult CreateUid()
        {
            return Ok(_interceptorService.NewUid());
        }


        [HttpPost("checkuids")]
        [RequestSizeLimit(1_000)]
        public IActionResult CheckUids([FromBody]List<string> inUids)
        {
            if (inUids.Count <= 10)
            {
                var _validUids = _interceptorService.UidsExist(inUids);
                return Ok(_validUids);
            }
            else
            {
                return BadRequest("Too many uids supplied.");
            }
        }

        /// <summary>
        /// Passes the request onto the InterceptorService's request processor which packages the request into an Interceptor object and adds it to the global collection of Interceptors.
        /// </summary>
        /// <param name="uid"></param>
        /// <returns></returns>
        private async Task<IActionResult> ProcessRequest(string uid)
        {
            if (_interceptorService.UidExists(uid))
            {
                var _processesReq = await _interceptorService.ProcessRequest(uid, Request);
                // Use websocket to alert clients of this request creation. Only clients associated with this collector will be notified.
                await _hubContext.Clients.Group("intercept-" + uid).SendAsync("InterceptorNotify", _processesReq.RequestId);
                return Ok();
            }
            else
            {
                return NotFound("Uid not found.");
            }
        }
    }
}
