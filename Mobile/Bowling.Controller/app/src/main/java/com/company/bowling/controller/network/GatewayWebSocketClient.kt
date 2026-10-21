package com.company.bowling.controller.network

import android.util.Log
import com.company.bowling.controller.model.*
import com.google.gson.Gson
import com.google.gson.GsonBuilder
import org.java_websocket.client.WebSocketClient
import org.java_websocket.handshake.ServerHandshake
import java.net.URI
import java.util.concurrent.ConcurrentLinkedQueue

class GatewayWebSocketClient(
    private val serverUri: String,
    private val sessionToken: String,
    private val listener: GatewayListener
) : WebSocketClient(URI(serverUri)) {

    private val gson = GsonBuilder().create()
    private val sendQueue = ConcurrentLinkedQueue<String>()
    private var isAuthenticated = false
    private var reconnectAttempts = 0
    private val maxReconnectAttempts = 5

    interface GatewayListener {
        fun onConnected()
        fun onDisconnected(reason: String)
        fun onError(error: String)
        fun onSessionCreated(message: SessionCreatedMessage)
        fun onSessionExpired(message: SessionExpiredMessage)
        fun onSensorFrameAck()
        fun onControllerReady()
        fun onThrowReleased(message: ThrowReleasedMessage)
        fun onErrorMessage(message: ErrorMessage)
    }

    override fun onOpen(handshakedata: ServerHandshake) {
        Log.d("GatewayWS", "Connected to gateway")
        isAuthenticated = false
        reconnectAttempts = 0
        sendRegisterPhone()
    }

    override fun onMessage(message: String) {
        try {
            val baseMessage = gson.fromJson(message, MessageBase::class.java)
            when (baseMessage?.messageType) {
                MessageType.SESSION_CREATED -> {
                    val msg = gson.fromJson(message, SessionCreatedMessage::class.java)
                    isAuthenticated = true
                    listener.onSessionCreated(msg!!)
                }
                MessageType.SESSION_EXPIRED -> {
                    val msg = gson.fromJson(message, SessionExpiredMessage::class.java)
                    listener.onSessionExpired(msg!!)
                }
                MessageType.THROW_RELEASED -> {
                    val msg = gson.fromJson(message, ThrowReleasedMessage::class.java)
                    listener.onThrowReleased(msg!!)
                }
                MessageType.ERROR -> {
                    val msg = gson.fromJson(message, ErrorMessage::class.java)
                    listener.onErrorMessage(msg!!)
                }
                MessageType.CONTROLLER_READY -> {
                    listener.onControllerReady()
                }
                MessageType.PONG -> {
                    // Pong received
                }
                else -> {
                    Log.d("GatewayWS", "Received: ${baseMessage?.messageType}")
                }
            }
        } catch (e: Exception) {
            Log.e("GatewayWS", "Failed to parse message: ${e.message}")
        }
    }

    override fun onClose(code: Int, reason: String, remote: Boolean) {
        Log.d("GatewayWS", "Disconnected: $reason")
        isAuthenticated = false
        listener.onDisconnected(reason)
        attemptReconnect()
    }

    override fun onError(ex: Exception) {
        Log.e("GatewayWS", "Error: ${ex.message}")
        listener.onError(ex.message ?: "Unknown error")
    }

    private fun sendRegisterPhone() {
        val deviceId = android.os.Build.SERIAL
        val deviceName = android.os.Build.MODEL
        
        val message = RegisterPhoneMessage(sessionToken, deviceId, deviceName)
        sendMessage(message)
    }

    fun sendSensorFrame(acceleration: Vector3, gyroscope: Vector3, orientation: Quaternion?) {
        val message = SensorFrameMessage(
            sessionToken = sessionToken,
            acceleration = acceleration,
            gyroscope = gyroscope,
            orientation = orientation,
            deviceTimestamp = System.currentTimeMillis()
        )
        sendMessage(message)
    }

    fun sendControllerReady() {
        val message = ControllerReadyMessage(sessionToken = sessionToken)
        sendMessage(message)
    }

    fun sendThrowStarted() {
        val message = ThrowStartedMessage(
            sessionToken = sessionToken,
            startTimestamp = System.currentTimeMillis()
        )
        sendMessage(message)
    }

    fun sendPing() {
        val message = PingMessage()
        sendMessage(message)
    }

    private fun sendMessage(message: Any) {
        val json = gson.toJson(message)
        if (isOpen) {
            send(json)
        } else {
            sendQueue.offer(json)
        }
    }

    private fun attemptReconnect() {
        if (reconnectAttempts < maxReconnectAttempts) {
            reconnectAttempts++
            Log.d("GatewayWS", "Reconnecting... attempt $reconnectAttempts")
            // Reconnect logic would go here
        }
    }

    private abstract class MessageBase {
        var type: MessageType = MessageType.UNKNOWN
    }

    private data class RegisterPhoneMessage(
        @Suppress("UNUSED_PARAMETER") val sessionToken: String,
        @Suppress("UNUSED_PARAMETER") val deviceId: String,
        @Suppress("UNUSED_PARAMETER") val deviceName: String
    ) {
        val type = MessageType.PHONE_CONNECTED
    }
}