namespace Bowling.Gateway.Controllers;

using Bowling.Protocol;
using Bowling.Gateway.Services;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public sealed class SessionController : ControllerBase
{
    private readonly ISessionManager _sessionManager;
    private readonly IQrCodeService _qrCodeService;
    private readonly ILogger<SessionController> _logger;
    private readonly IConfiguration _configuration;

    public SessionController(
        ISessionManager sessionManager,
        IQrCodeService qrCodeService,
        ILogger<SessionController> logger,
        IConfiguration configuration)
    {
        _sessionManager = sessionManager;
        _qrCodeService = qrCodeService;
        _logger = logger;
        _configuration = configuration;
    }

    [HttpPost("create")]
    public ActionResult<SessionCreatedMessage> CreateSession()
    {
        var session = _sessionManager.CreateSession();
        var gatewayUrl = GetGatewayUrl();
        
        var qrUrl = $"{gatewayUrl}/join/{session.Token}";
        session.QrCodeBase64 = _qrCodeService.GenerateQrCodeBase64(qrUrl);

        var response = new SessionCreatedMessage(
            session.Token,
            gatewayUrl,
            _configuration.GetValue<int>("Gateway:Port", 5050),
            session.PairingExpiresAt.Ticks,
            session.ActiveExpiresAt.Ticks
        );

        _logger.LogInformation("Session created via API: {Token}", MaskToken(session.Token));
        return Ok(response);
    }

    [HttpGet("join/{token}")]
    [HttpGet("/join/{token}")]
    public ActionResult JoinSession(string token)
    {
        var session = _sessionManager.GetSession(token);
        if (session == null || session.IsPairingExpired || session.IsUsed)
        {
            return NotFound(new { error = "Session not found or expired" });
        }

        var qrUrl = $"{GetGatewayUrl()}/join/{token}";
        var qrCodeBase64 = _qrCodeService.GenerateQrCodeBase64(qrUrl);

        var html = GenerateJoinPage(token, qrCodeBase64);
        return Content(html, "text/html");
    }

    [HttpGet("status/{token}")]
    public ActionResult GetSessionStatus(string token)
    {
        var session = _sessionManager.GetSession(token);
        if (session == null)
        {
            return NotFound(new { error = "Session not found", expired = true });
        }

        return Ok(new
        {
            token = MaskToken(token),
            paired = session.IsPaired,
            deviceName = session.DeviceName,
            deviceId = session.DeviceId,
            pairingExpired = session.IsPairingExpired,
            activeExpired = session.IsActiveExpired,
            sensorFrameCount = session.SensorFrameCount,
            lastFrameAt = session.LastSensorFrameAt?.ToString("O")
        });
    }

    [HttpGet("qr/{token}")]
    public ActionResult GetQrCode(string token)
    {
        var session = _sessionManager.GetSession(token);
        if (session == null || session.IsPairingExpired)
        {
            return NotFound(new { error = "Session not found or expired" });
        }

        var qrUrl = $"{GetGatewayUrl()}/join/{token}";
        var qrCodeBase64 = _qrCodeService.GenerateQrCodeBase64(qrUrl);

        return Ok(new { qrCodeBase64, url = qrUrl });
    }

    private string GenerateJoinPage(string token, string qrCodeBase64)
    {
        return "<!DOCTYPE html>" +
               "<html><head>" +
               "<title>Company Bowling - Pairing</title>" +
               "<meta name='viewport' content='width=device-width, initial-scale=1'>" +
               "<style>" +
               "body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;display:flex;flex-direction:column;align-items:center;justify-content:center;min-height:100vh;margin:0;background:#1a1a2e;color:#eee;}" +
               ".container{text-align:center;padding:2rem;}" +
               "h1{color:#00d4aa;margin-bottom:0.5rem;}" +
               ".token{font-family:monospace;background:#16213e;padding:1rem;border-radius:8px;margin:1rem 0;word-break:break-all;font-size:0.9rem;}" +
               ".qr-code{margin:2rem 0;}" +
               ".qr-code img{max-width:100%;height:auto;border:4px solid #00d4aa;border-radius:12px;}" +
               ".instructions{color:#aaa;margin-top:2rem;line-height:1.6;}" +
               ".status{padding:1rem;border-radius:8px;margin-top:1rem;}" +
               ".waiting{background:#0f3460;border:2px solid #00d4aa;}" +
               ".expired{background:#662e2e;border:2px solid #e94560;}" +
               "</style></head><body>" +
               "<div class='container'>" +
               "<h1>[BOWLING] Company Bowling</h1>" +
               "<p>Scan with Bowling Controller App</p>" +
               "<div class='token'>Token: " + token + "</div>" +
               "<div class='qr-code'><img src='" + qrCodeBase64 + "' alt='QR Code for pairing' /></div>" +
               "<div class='status waiting' id='status'><strong>Waiting for phone to connect...</strong></div>" +
               "<div class='instructions'>" +
               "<p>1. Open Bowling Controller app on your phone</p>" +
               "<p>2. Tap 'Scan QR Code'</p>" +
               "<p>3. Point camera at the code above</p>" +
               "<p>4. Hold phone naturally and bowl!</p>" +
               "</div></div>" +
               "<script>" +
               "async function checkStatus(){try{const resp=await fetch('/api/session/status/" + token + "');const data=await resp.json();" +
               "if(data.paired){document.getElementById('status').innerHTML='<strong style=\"color:#00d4aa;\">[OK] Phone connected: '+data.deviceName+'</strong>';" +
               "document.getElementById('status').className='status waiting';}" +
               "else if(data.expired){document.getElementById('status').innerHTML='<strong style=\"color:#e94560;\">Session expired</strong>';" +
               "document.getElementById('status').className='status expired';}}catch(e){console.log(e);}}" +
               "setInterval(checkStatus,2000);checkStatus();" +
               "</script></body></html>";
    }

    private string GetGatewayUrl()
    {
        var ip = GetLocalIpAddress();
        var port = _configuration.GetValue<int>("Gateway:Port", 5050);
        return $"http://{ip}:{port}";
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(ip))
                {
                    return ip.ToString();
                }
            }
        }
        catch { }
        return "127.0.0.1";
    }

    private static string MaskToken(string token)
    {
        return token.Length > 8 ? token[..4] + "****" + token[^4..] : "****";
    }
}