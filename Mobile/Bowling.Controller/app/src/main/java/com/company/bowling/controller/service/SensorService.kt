package com.company.bowling.controller.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.hardware.Sensor
import android.hardware.SensorManager
import android.os.Build
import android.os.IBinder
import android.os.PowerManager
import androidx.core.app.NotificationCompat
import com.company.bowling.controller.model.*
import com.company.bowling.controller.network.GatewayWebSocketClient
import com.company.bowling.controller.sensor.SensorManagerImpl
import com.company.bowling.controller.sensor.ThrowDetector
import com.company.bowling.controller.sensor.ThrowDetectorConfig
import com.company.bowling.controller.sensor.ThrowResult
import com.company.bowling.controller.sensor.ThrowState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import java.util.concurrent.atomic.AtomicBoolean

class SensorService : Service(), GatewayWebSocketClient.GatewayListener {

    private val scope = CoroutineScope(Dispatchers.IO)
    private val job = Job()
    private var wakeLock: PowerManager.WakeLock? = null
    private var sensorManagerImpl: SensorManagerImpl? = null
    private var throwDetector: ThrowDetector? = null
    private var webSocketClient: GatewayWebSocketClient? = null
    private var sessionToken: String = ""
    private var gatewayUrl: String = ""
    private var isConnected = AtomicBoolean(false)
    private var isThrowInProgress = AtomicBoolean(false)
    private var lastThrowState = ThrowState.IDLE

    companion object {
        const val ACTION_START_SESSION = "com.company.bowling.controller.START_SESSION"
        const val ACTION_STOP_SESSION = "com.company.bowling.controller.STOP_SESSION"
        const val EXTRA_SESSION_TOKEN = "session_token"
        const val EXTRA_GATEWAY_URL = "gateway_url"
        const val NOTIFICATION_ID = 1
        const val CHANNEL_ID = "bowling_sensor_channel"
    }

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
        acquireWakeLock()
        initializeComponents()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        intent?.let {
            when (it.action) {
                ACTION_START_SESSION -> {
                    sessionToken = it.getStringExtra(EXTRA_SESSION_TOKEN) ?: ""
                    gatewayUrl = it.getStringExtra(EXTRA_GATEWAY_URL) ?: ""
                    startSession()
                }
                ACTION_STOP_SESSION -> stopSession()
            }
        }
        return START_STICKY
    }

    private fun initializeComponents() {
        sensorManagerImpl = SensorManagerImpl(this, 60)
        throwDetector = ThrowDetector(ThrowDetectorConfig())
    }

    private fun startSession() {
        if (sessionToken.isEmpty() || gatewayUrl.isEmpty()) return

        val wsUrl = gatewayUrl.replace("http://", "ws://").replace("/api/session/create", "") + "/ws"
        webSocketClient = GatewayWebSocketClient(wsUrl, sessionToken, this)
        webSocketClient?.connect()

        val sensorChannel = sensorManagerImpl?.start()
        scope.launch(job) {
            sensorChannel?.consumeEach { sensorData ->
                processSensorData(sensorData)
            }
        }

        startForeground(NOTIFICATION_ID, createNotification("Connected - Ready to bowl"))
    }

    private fun stopSession() {
        job.cancel()
        sensorManagerImpl?.stop()
        webSocketClient?.close()
        isConnected.set(false)
        stopForeground(true)
        stopSelf()
    }

    private fun processSensorData(data: SensorManagerImpl.SensorData) {
        val result = throwDetector?.processFrame(data.timestamp, data.acceleration, data.gyroscope)
        
        webSocketClient?.sendSensorFrame(data.acceleration, data.gyroscope, null)

        result?.let { throwResult ->
            when (throwResult.finalState) {
                ThrowState.BACKSWING -> {
                    if (lastThrowState != ThrowState.BACKSWING) {
                        webSocketClient?.sendThrowStarted()
                        isThrowInProgress.set(true)
                    }
                }
                ThrowState.RELEASE -> {
                    if (throwResult.isValid && throwResult.parameters != null) {
                        // Throw confirmed by local detector
                    }
                    isThrowInProgress.set(false)
                }
                ThrowState.COOLDOWN -> {
                    isThrowInProgress.set(false)
                }
            }
            lastThrowState = throwResult.finalState
            
            updateNotification("Connected - ${throwResult.finalState}")
        }
    }

    // GatewayWebSocketClient.GatewayListener implementation
    override fun onConnected() {
        isConnected.set(true)
        updateNotification("Connected - Ready to bowl")
    }

    override fun onDisconnected(reason: String) {
        isConnected.set(false)
        updateNotification("Disconnected: $reason")
    }

    override fun onError(error: String) {
        updateNotification("Error: $error")
    }

    override fun onSessionCreated(message: SessionCreatedMessage) {
        // Session already created via QR
    }

    override fun onSessionExpired(message: SessionExpiredMessage) {
        stopSession()
    }

    override fun onSensorFrameAck() {
        // Sensor frame acknowledged
    }

    override fun onControllerReady() {
        // Controller ready confirmed
    }

    override fun onThrowReleased(message: ThrowReleasedMessage) {
        // Authoritative throw from server
    }

    override fun onErrorMessage(message: ErrorMessage) {
        updateNotification("Error: ${message.description}")
    }

    private fun acquireWakeLock() {
        val powerManager = getSystemService(Context.POWER_SERVICE) as PowerManager
        wakeLock = powerManager.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "BowlingController::SensorService")
        wakeLock?.acquire()
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                CHANNEL_ID,
                "Bowling Sensor Service",
                NotificationManager.IMPORTANCE_LOW
            ).apply {
                description = "Foreground service for bowling sensor data"
            }
            val manager = getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(channel)
        }
    }

    private fun createNotification(text: String): Notification {
        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle("Company Bowling Controller")
            .setContentText(text)
            .setSmallIcon(android.R.drawable.ic_dialog_info)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setOngoing(true)
            .build()
    }

    private fun updateNotification(text: String) {
        val manager = getSystemService(NotificationManager::class.java)
        manager.notify(NOTIFICATION_ID, createNotification(text))
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onDestroy() {
        super.onDestroy()
        job.cancel()
        sensorManagerImpl?.stop()
        webSocketClient?.close()
        wakeLock?.release()
    }
}