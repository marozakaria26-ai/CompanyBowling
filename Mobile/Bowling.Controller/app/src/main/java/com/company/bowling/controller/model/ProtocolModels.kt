package com.company.bowling.controller.model

import com.google.gson.annotations.SerializedName

sealed class MessageBase(
    @SerializedName("type") val type: MessageType,
    @SerializedName("timestamp") val timestamp: Long = System.currentTimeMillis()
) {
    abstract val messageType: MessageType
}

enum class MessageType(val value: String) {
    UNKNOWN("unknown"),
    PHONE_CONNECTED("phoneConnected"),
    PHONE_DISCONNECTED("phoneDisconnected"),
    SENSOR_FRAME("sensorFrame"),
    CONTROLLER_READY("controllerReady"),
    THROW_STARTED("throwStarted"),
    THROW_RELEASED("throwReleased"),
    THROW_PARAMETERS("throwParameters"),
    SESSION_CREATED("sessionCreated"),
    SESSION_EXPIRED("sessionExpired"),
    ERROR("error"),
    PING("ping"),
    PONG("pong"),
    GATEWAY_INFO("gatewayInfo"),
    UNITY_CONNECTED("unityConnected"),
    UNITY_DISCONNECTED("unityDisconnected"),
    RESET_LANE("resetLane"),
    THROW_COMPLETE("throwComplete");

    companion object {
        fun fromString(value: String): MessageType = values().firstOrNull { it.value == value } ?: UNKNOWN
    }
}

data class Vector3(
    @SerializedName("x") val x: Double,
    @SerializedName("y") val y: Double,
    @SerializedName("z") val z: Double
)

data class Quaternion(
    @SerializedName("x") val x: Double,
    @SerializedName("y") val y: Double,
    @SerializedName("z") val z: Double,
    @SerializedName("w") val w: Double
)

data class PhoneConnectedMessage(
    @SerializedName("sessionToken") val sessionToken: String,
    @SerializedName("deviceId") val deviceId: String,
    @SerializedName("deviceName") val deviceName: String,
    @SerializedName("timestamp") val timestamp: Long = System.currentTimeMillis()
) : MessageBase(MessageType.PHONE_CONNECTED, timestamp) {
    override val messageType = MessageType.PHONE_CONNECTED
}

data class PhoneDisconnectedMessage(
    @SerializedName("sessionToken") val sessionToken: String,
    @SerializedName("reason") val reason: String,
    @SerializedName("timestamp") val timestamp: Long = System.currentTimeMillis()
) : MessageBase(MessageType.PHONE_DISCONNECTED, timestamp) {
    override val messageType = MessageType.PHONE_DISCONNECTED
}

data class SensorFrameMessage(
    @SerializedName("sessionToken") val sessionToken: String,
    @SerializedName("acceleration") val acceleration: Vector3,
    @SerializedName("gyroscope") val gyroscope: Vector3,
    @SerializedName("orientation") val orientation: Quaternion? = null,
    @SerializedName("deviceTimestamp") val deviceTimestamp: Long = 0,
    @SerializedName("timestamp") val timestamp: Long = System.currentTimeMillis()
) : MessageBase(MessageType.SENSOR_FRAME, timestamp) {
    override val messageType = MessageType.SENSOR_FRAME
}

data class ControllerReadyMessage(
    @SerializedName("sessionToken") val sessionToken: String,
    @SerializedName("timestamp") val timestamp: Long = System.currentTimeMillis()
) : MessageBase(MessageType.CONTROLLER_READY, timestamp) {
    override val messageType = MessageType.CONTROLLER_READY
}

data class ThrowStartedMessage(
    @SerializedName("sessionToken") val sessionToken: String,
    @SerializedName("startTimestamp") val startTimestamp: Long,
    @SerializedName("timestamp") val timestamp: Long = System.currentTimeMillis()
) : MessageBase(MessageType.THROW_STARTED, timestamp) {
    override val messageType = MessageType.THROW_STARTED
}

data class ThrowParameters(
    @SerializedName("power") val power: Double,
    @SerializedName("direction") val direction: Double,
    @SerializedName("spin") val spin: Double,
    @SerializedName("releaseAngle") val releaseAngle: Double,
    @SerializedName("durationMs") val durationMs: Long,
    @SerializedName("peakAcceleration") val peakAcceleration: Double,
    @SerializedName("releaseVelocity") val releaseVelocity: Vector3
)

data class ThrowReleasedMessage(
    @SerializedName("sessionToken") val sessionToken: String,
    @SerializedName("parameters") val parameters: ThrowParameters,
    @SerializedName("timestamp") val timestamp: Long = System.currentTimeMillis()
) : MessageBase(MessageType.THROW_RELEASED, timestamp) {
    override val messageType = MessageType.THROW_RELEASED
}

data class SessionCreatedMessage(
    @SerializedName("sessionToken") val sessionToken: String,
    @SerializedName("gatewayUrl") val gatewayUrl: String,
    @SerializedName("port") val port: Int,
    @SerializedName("expiresAt") val expiresAt: Long,
    @SerializedName("timestamp") val timestamp: Long = System.currentTimeMillis()
) : MessageBase(MessageType.SESSION_CREATED, timestamp) {
    override val messageType = MessageType.SESSION_CREATED
}

data class SessionExpiredMessage(
    @SerializedName("sessionToken") val sessionToken: String,
    @SerializedName("timestamp") val timestamp: Long = System.currentTimeMillis()
) : MessageBase(MessageType.SESSION_EXPIRED, timestamp) {
    override val messageType = MessageType.SESSION_EXPIRED
}

data class ErrorMessage(
    @SerializedName("code") val code: String,
    @SerializedName("description") val description: String,
    @SerializedName("timestamp") val timestamp: Long = System.currentTimeMillis()
) : MessageBase(MessageType.ERROR, timestamp) {
    override val messageType = MessageType.ERROR
}

data class PingMessage(
    @SerializedName("timestamp") val timestamp: Long = System.currentTimeMillis()
) : MessageBase(MessageType.PING, timestamp) {
    override val messageType = MessageType.PING
}

data class PongMessage(
    @SerializedName("timestamp") val timestamp: Long = System.currentTimeMillis()
) : MessageBase(MessageType.PONG, timestamp) {
    override val messageType = MessageType.PONG
}

data class GatewayInfoMessage(
    @SerializedName("ipAddress") val ipAddress: String,
    @SerializedName("port") val port: Int,
    @SerializedName("version") val version: String,
    @SerializedName("connectedPhones") val connectedPhones: Int,
    @SerializedName("unityConnected") val unityConnected: Boolean,
    @SerializedName("timestamp") val timestamp: Long = System.currentTimeMillis()
) : MessageBase(MessageType.GATEWAY_INFO, timestamp) {
    override val messageType = MessageType.GATEWAY_INFO
}

data class UnityConnectedMessage(
    @SerializedName("timestamp") val timestamp: Long = System.currentTimeMillis()
) : MessageBase(MessageType.UNITY_CONNECTED, timestamp) {
    override val messageType = MessageType.UNITY_CONNECTED
}

data class UnityDisconnectedMessage(
    @SerializedName("timestamp") val timestamp: Long = System.currentTimeMillis()
) : MessageBase(MessageType.UNITY_DISCONNECTED, timestamp) {
    override val messageType = MessageType.UNITY_DISCONNECTED
}

data class ResetLaneMessage(
    @SerializedName("timestamp") val timestamp: Long = System.currentTimeMillis()
) : MessageBase(MessageType.RESET_LANE, timestamp) {
    override val messageType = MessageType.RESET_LANE
}

data class ThrowCompleteMessage(
    @SerializedName("pinsKnockedDown") val pinsKnockedDown: Int,
    @SerializedName("ballFinalPositionZ") val ballFinalPositionZ: Double,
    @SerializedName("timestamp") val timestamp: Long = System.currentTimeMillis()
) : MessageBase(MessageType.THROW_COMPLETE, timestamp) {
    override val messageType = MessageType.THROW_COMPLETE
}