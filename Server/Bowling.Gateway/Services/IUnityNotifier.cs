namespace Bowling.Gateway.Services;

using Bowling.Protocol;

public interface IUnityNotifier
{
    Task NotifyPhoneConnected(string sessionToken, string deviceId, string deviceName);
    Task NotifyPhoneDisconnected(string sessionToken, string reason);
    Task NotifySensorFrame(SensorFrameMessage message);
    Task NotifyControllerReady(ControllerReadyMessage message);
    Task NotifyThrowStarted(ThrowStartedMessage message);
    Task NotifyThrowReleased(ThrowReleasedMessage message);
    Task NotifyUnityConnected();
    Task NotifyUnityDisconnected();
    Task NotifyGatewayInfo(GatewayInfoMessage message);
}