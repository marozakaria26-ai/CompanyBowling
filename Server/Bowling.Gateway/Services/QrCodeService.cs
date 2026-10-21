namespace Bowling.Gateway.Services;

using Bowling.Gateway.Configuration;
using Microsoft.Extensions.Options;
using QRCoder;
using System.Drawing;

public interface IQrCodeService
{
    string GenerateQrCode(string url);
    string GenerateQrCodeBase64(string url);
}

public sealed class QrCodeService : IQrCodeService
{
    private readonly ILogger<QrCodeService> _logger;
    private readonly GatewayOptions _options;

    public QrCodeService(ILogger<QrCodeService> logger, IOptions<GatewayOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public string GenerateQrCode(string url)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);
        var qrCodeBytes = qrCode.GetGraphic(20);
        return Convert.ToBase64String(qrCodeBytes);
    }

    public string GenerateQrCodeBase64(string url)
    {
        return $"data:image/png;base64,{GenerateQrCode(url)}";
    }
}